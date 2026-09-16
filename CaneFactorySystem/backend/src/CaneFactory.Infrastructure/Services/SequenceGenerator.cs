using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.Infrastructure.Services;

/// <summary>
/// Transaction-safe continuous business serial generator (PurchaseId, AdviceNumber, GrowerSequence, VillageId, ...).
/// Must be called INSIDE an active DbContext transaction. The row lock taken by UPDATE serializes
/// concurrent operators; the number is consumed only when the caller commits. On rollback the
/// number remains available, so business serials stay continuous with no unexplained gaps.
/// </summary>
public class SequenceGenerator : ISequenceGenerator
{
    private readonly AppDbContext _db;
    public SequenceGenerator(AppDbContext db) => _db = db;

    public async Task<long> NextAsync(string sequenceName, long startValue = 1)
    {
        if (_db.Database.CurrentTransaction == null)
            throw new InvalidOperationException($"Sequence '{sequenceName}' must be reserved inside a transaction.");

        int rows = _db.Database.IsSqlServer()
            ? await _db.Database.ExecuteSqlRawAsync(
                "UPDATE NumberSequences WITH (ROWLOCK, XLOCK, HOLDLOCK) SET NextValue = NextValue + 1 WHERE Name = {0}", sequenceName)
            : await _db.Database.ExecuteSqlRawAsync(
                "UPDATE NumberSequences SET NextValue = NextValue + 1 WHERE Name = {0}", sequenceName);

        if (rows == 0)
        {
            _db.NumberSequences.Add(new NumberSequence { Name = sequenceName, NextValue = startValue + 1 });
            await _db.SaveChangesAsync();
            return startValue;
        }

        var next = await _db.NumberSequences.AsNoTracking()
            .Where(s => s.Name == sequenceName).Select(s => s.NextValue).FirstAsync();
        return next - 1;
    }
}
