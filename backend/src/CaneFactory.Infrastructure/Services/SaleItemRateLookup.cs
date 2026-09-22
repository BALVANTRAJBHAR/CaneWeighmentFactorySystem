using System.Data;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.Infrastructure.Services;

/// <summary>
/// Read-only effective-date lookup used by infrastructure components that must
/// show a sale rate without taking a dependency on the API project.
/// </summary>
public static class SaleItemRateLookup
{
    public static async Task<decimal?> CurrentAsync(
        AppDbContext db, int itemId, DateTime effectiveDate, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP (1) Rate
            FROM SaleItemRates
            WHERE ItemId = @itemId
              AND IsDeleted = 0
              AND Status = 1
              AND EffectiveFrom < DATEADD(day, 1, @effectiveDate)
              AND (EffectiveTo IS NULL OR EffectiveTo >= @effectiveDate)
            ORDER BY EffectiveFrom DESC, Id DESC
            """;

        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            var itemParameter = command.CreateParameter();
            itemParameter.ParameterName = "@itemId";
            itemParameter.Value = itemId;
            command.Parameters.Add(itemParameter);

            var dateParameter = command.CreateParameter();
            dateParameter.ParameterName = "@effectiveDate";
            dateParameter.Value = effectiveDate.Date;
            command.Parameters.Add(dateParameter);

            var value = await command.ExecuteScalarAsync(ct);
            return value is null or DBNull ? null : Convert.ToDecimal(value);
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }
}
