using System.Data;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Services;

/// <summary>Raw read helper for the independent item-rate history.  It deliberately has no
/// navigation dependency, keeping historical sale-rate lookup available during master changes.</summary>
public static class SaleItemRateStore
{
    public sealed record Snapshot(int Id, int ItemId, string ItemName, decimal Rate,
        DateTime EffectiveFrom, DateTime? EffectiveTo, bool Status, DateTime CreatedAt);

    public static async Task<List<Snapshot>> ListAsync(AppDbContext db, bool includeInactive, CancellationToken ct = default)
    {
        const string sql = """
            SELECT r.Id, r.ItemId, i.ItemName, r.Rate, r.EffectiveFrom, r.EffectiveTo, r.Status, r.CreatedAt
            FROM SaleItemRates r INNER JOIN Items i ON i.Id = r.ItemId
            WHERE r.IsDeleted = 0 AND (@includeInactive = 1 OR r.Status = 1)
            ORDER BY r.EffectiveFrom DESC, r.Id DESC
            """;
        var result = new List<Snapshot>();
        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var p = command.CreateParameter(); p.ParameterName = "@includeInactive"; p.Value = includeInactive ? 1 : 0;
            command.Parameters.Add(p);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(new Snapshot(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetDecimal(3),
                    reader.GetDateTime(4), reader.IsDBNull(5) ? null : reader.GetDateTime(5), reader.GetBoolean(6), reader.GetDateTime(7)));
        }
        finally { if (!wasOpen) await connection.CloseAsync(); }
        return result;
    }

    public static async Task<Snapshot?> CurrentAsync(AppDbContext db, int itemId, DateTime date, CancellationToken ct = default)
    {
        var rates = await ListAsync(db, includeInactive: false, ct);
        return rates.Where(r => r.ItemId == itemId && r.EffectiveFrom.Date <= date.Date &&
                (r.EffectiveTo == null || r.EffectiveTo.Value.Date >= date.Date))
            .OrderByDescending(r => r.EffectiveFrom).FirstOrDefault();
    }
}
