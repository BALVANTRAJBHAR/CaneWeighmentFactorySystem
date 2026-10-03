using System.Security.Cryptography;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Services;

public sealed class RateOverrideValidationException(string message) : Exception(message);

/// <summary>Single server-side policy for Cane Tare and Sale Gross rate reductions.</summary>
public sealed class RateOverrideService(AppDbContext db)
{
    public async Task<decimal> ValidateAndStageAsync(string transactionType, int transactionId,
        decimal masterRate, decimal? requestedRate, int? approvedByUserId, int? rateReasonId,
        string? remark, string? evidenceToken, int currentUserId, CancellationToken ct = default)
    {
        masterRate = Math.Round(masterRate, 2, MidpointRounding.AwayFromZero);
        var effectiveRate = Math.Round(requestedRate ?? masterRate, 2, MidpointRounding.AwayFromZero);
        if (effectiveRate <= 0)
            throw new RateOverrideValidationException("Rate must be greater than zero.");
        if (effectiveRate > masterRate)
            throw new RateOverrideValidationException($"Rate cannot exceed the defined master rate {masterRate:F2}.");
        if (effectiveRate == masterRate) return masterRate;

        if (approvedByUserId is null or <= 0)
            throw new RateOverrideValidationException("Approved By is required when rate is changed.");
        if (rateReasonId is null or <= 0)
            throw new RateOverrideValidationException("Rate Reason is required when rate is changed.");
        if (string.IsNullOrWhiteSpace(evidenceToken))
            throw new RateOverrideValidationException("JPG/JPEG attachment or camera evidence is required when rate is changed.");

        var approver = await db.Users.AsNoTracking()
            .Where(u => u.Id == approvedByUserId && !u.IsDeleted && u.Status &&
                u.UserRoles.Any(ur => ur.Role.Name == "Admin" && !ur.Role.IsDeleted && ur.Role.Status))
            .Select(u => new { u.Id, u.FullName, u.Username })
            .FirstOrDefaultAsync(ct);
        if (approver == null)
            throw new RateOverrideValidationException("Selected approver is not an active Admin user.");

        var reason = await db.RateReasons.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rateReasonId && !x.IsDeleted && x.Status, ct);
        if (reason == null)
            throw new RateOverrideValidationException("Selected Rate Reason does not exist or is inactive.");

        var evidence = await db.RateOverrideEvidences.FirstOrDefaultAsync(x =>
            x.Token == evidenceToken.Trim() && x.TransactionType == transactionType &&
            x.TransactionId == transactionId && x.CreatedBy == currentUserId &&
            x.ConsumedAt == null && x.ExpiresAt >= DateTime.UtcNow && !x.IsDeleted, ct);
        if (evidence == null)
            throw new RateOverrideValidationException("Rate evidence is invalid, expired, belongs to another transaction, or was already used. Capture/upload it again.");
        if (!File.Exists(evidence.FilePath))
            throw new RateOverrideValidationException("Rate evidence file is missing. Capture/upload it again.");
        var actualHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(evidence.FilePath, ct)));
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualHash), Convert.FromHexString(evidence.FileHash)))
            throw new RateOverrideValidationException("Rate evidence integrity check failed. Capture/upload it again.");
        if (await db.WeighmentRateOverrides.AnyAsync(x => x.TransactionType == transactionType && x.TransactionId == transactionId, ct))
            throw new RateOverrideValidationException("A rate override is already recorded for this transaction.");

        evidence.ConsumedAt = DateTime.UtcNow;
        evidence.UpdatedAt = DateTime.UtcNow;
        evidence.UpdatedBy = currentUserId;
        db.WeighmentRateOverrides.Add(new WeighmentRateOverride
        {
            TransactionType = transactionType,
            TransactionId = transactionId,
            MasterRate = masterRate,
            ApprovedRate = effectiveRate,
            ApprovedByUserId = approver.Id,
            ApprovedByUserName = string.IsNullOrWhiteSpace(approver.FullName) ? approver.Username : approver.FullName,
            RateReasonId = reason.Id,
            RateReasonText = reason.ReasonName,
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim()[..Math.Min(remark.Trim().Length, 500)],
            EvidenceId = evidence.Id,
            CreatedBy = currentUserId
        });
        return effectiveRate;
    }
}
