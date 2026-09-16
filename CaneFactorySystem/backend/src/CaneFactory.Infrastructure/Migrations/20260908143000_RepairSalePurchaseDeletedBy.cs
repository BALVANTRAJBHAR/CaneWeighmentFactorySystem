using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>
/// Repairs databases created by the first SalePurchase migration. SalePurchase derives from
/// BaseEntity, but that migration omitted the nullable DeletedBy audit column. EF therefore
/// generated INSERT/SELECT statements that referenced a column that did not exist.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260908143000_RepairSalePurchaseDeletedBy")]
public partial class RepairSalePurchaseDeletedBy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A guarded statement makes this safe for a development database where the column was
        // added manually before this repair migration is applied. It never alters existing rows.
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[SalePurchases]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.SalePurchases', N'DeletedBy') IS NULL
            BEGIN
                ALTER TABLE [dbo].[SalePurchases] ADD [DeletedBy] int NULL;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[SalePurchases]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.SalePurchases', N'DeletedBy') IS NOT NULL
            BEGIN
                ALTER TABLE [dbo].[SalePurchases] DROP COLUMN [DeletedBy];
            END
            """);
    }
}
