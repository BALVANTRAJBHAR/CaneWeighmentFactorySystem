using CaneFactory.API.Auth;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Services;

/// <summary>
/// Keeps the security identity for a grower synchronized with the grower master.  Mobile is a
/// login identifier only; GrowerId is the immutable ownership boundary used by farmer APIs.
/// </summary>
public sealed class FarmerAccountService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly UserStateService _userState;
    private readonly ILogger<FarmerAccountService> _log;
    private static readonly PasswordHasher<User> Hasher = new();

    public FarmerAccountService(AppDbContext db, IAuditService audit, UserStateService userState,
        ILogger<FarmerAccountService> log)
    {
        _db = db;
        _audit = audit;
        _userState = userState;
        _log = log;
    }

    public static string BuildTemporaryPassword(string growerName, string mobile)
    {
        var prefix = new string((growerName ?? string.Empty)
            .Where(c => !char.IsWhiteSpace(c)).Take(4).ToArray()).ToUpperInvariant();
        if (prefix.Length == 0 || mobile.Length != 10 || !mobile.All(char.IsDigit))
            throw new InvalidOperationException("A valid grower name and 10-digit mobile are required for farmer login.");
        return prefix + mobile;
    }

    public async Task<FarmerAccountSyncResult> EnsureForGrowerAsync(Grower grower,
        bool identityChanged = false, CancellationToken cancellationToken = default)
    {
        var farmerRole = await _db.Roles.FirstOrDefaultAsync(
            r => r.Name == "Farmer" && !r.IsDeleted && r.Status, cancellationToken)
            ?? throw new InvalidOperationException("The Farmer system role is missing or inactive.");

        var user = await _db.Users.Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.GrowerId == grower.Id && !u.IsDeleted, cancellationToken);
        var created = false;
        var changed = false;

        if (user == null)
        {
            // Safe legacy adoption: only one unlinked Farmer identity with this mobile may be linked.
            var legacy = await _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .Where(u => u.GrowerId == null && !u.IsDeleted && u.Mobile == grower.Mobile
                    && u.UserRoles.Any(ur => ur.Role.Name == "Farmer"))
                .Take(2).ToListAsync(cancellationToken);
            if (legacy.Count > 1)
                throw new InvalidOperationException("More than one legacy Farmer account uses this mobile number.");
            user = legacy.SingleOrDefault();
        }

        if (user == null)
        {
            user = new User
            {
                GrowerId = grower.Id,
                Username = $"farmer-{grower.Id}",
                FullName = grower.GrowerName,
                FullNameHi = grower.GrowerNameHi,
                Mobile = grower.Mobile,
                Email = grower.Email,
                Status = grower.Status && !grower.IsDeleted,
                MustChangePassword = true,
                TokenVersion = 1
            };
            user.PasswordHash = Hasher.HashPassword(user,
                BuildTemporaryPassword(grower.GrowerName, grower.Mobile));
            user.UserRoles.Add(new UserRole { RoleId = farmerRole.Id });
            _db.Users.Add(user);
            created = true;
            changed = true;
        }
        else
        {
            var profileChanged = user.GrowerId != grower.Id
                || user.FullName != grower.GrowerName
                || user.FullNameHi != grower.GrowerNameHi
                || user.Mobile != grower.Mobile
                || user.Email != grower.Email
                || user.Status != (grower.Status && !grower.IsDeleted);
            user.GrowerId = grower.Id;
            var securityChanged = user.Mobile != grower.Mobile
                || user.Status != (grower.Status && !grower.IsDeleted);
            user.FullName = grower.GrowerName;
            user.FullNameHi = grower.GrowerNameHi;
            user.Mobile = grower.Mobile;
            user.Email = grower.Email;
            user.Status = grower.Status && !grower.IsDeleted;
            if (profileChanged) user.UpdatedAt = DateTime.UtcNow;

            if (identityChanged && user.MustChangePassword)
                user.PasswordHash = Hasher.HashPassword(user,
                    BuildTemporaryPassword(grower.GrowerName, grower.Mobile));

            if (!user.UserRoles.Any(ur => ur.RoleId == farmerRole.Id))
            {
                user.UserRoles.Add(new UserRole { RoleId = farmerRole.Id });
                changed = true;
            }

            if (securityChanged || identityChanged)
            {
                user.TokenVersion++;
                await RevokeSessionsAsync(user.Id, cancellationToken);
            }
            changed |= profileChanged || securityChanged || identityChanged;
        }

        if (!changed)
            return new FarmerAccountSyncResult(user.Id, false, user.MustChangePassword);

        await _db.SaveChangesAsync(cancellationToken);
        _userState.Invalidate(user.Id);
        await _audit.LogAsync(created ? "FarmerAccountProvisioned" : "FarmerAccountSynchronized",
            "Grower", "User", user.Id.ToString(), newValue: new
            {
                grower.Id,
                LoginIdentifier = "registered mobile",
                user.Status,
                user.MustChangePassword
            });
        return new FarmerAccountSyncResult(user.Id, created, user.MustChangePassword);
    }

    public async Task SynchronizeExistingGrowersAsync(CancellationToken cancellationToken = default)
    {
        var duplicateMobiles = await _db.Growers.AsNoTracking()
            .Where(g => !g.IsDeleted && g.Mobile != "")
            .GroupBy(g => g.Mobile).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToListAsync(cancellationToken);
        foreach (var mobile in duplicateMobiles)
            _log.LogWarning("Farmer login not provisioned for duplicate grower mobile ending {Suffix}.",
                mobile.Length >= 4 ? mobile[^4..] : "****");

        var growers = await _db.Growers
            .Where(g => !g.IsDeleted && !duplicateMobiles.Contains(g.Mobile))
            .OrderBy(g => g.Id).ToListAsync(cancellationToken);
        foreach (var grower in growers)
        {
            if (grower.Mobile.Length != 10 || !grower.Mobile.All(char.IsDigit))
            {
                _log.LogWarning("Farmer login not provisioned for Grower {GrowerId}: invalid mobile.", grower.Id);
                continue;
            }
            try
            {
                await EnsureForGrowerAsync(grower, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Farmer login synchronization failed for Grower {GrowerId}.", grower.Id);
            }
        }
    }

    private async Task RevokeSessionsAsync(int userId, CancellationToken cancellationToken)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in active)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedByIp = "SYSTEM";
        }
    }
}

public sealed record FarmerAccountSyncResult(int UserId, bool Created, bool MustChangePassword);
