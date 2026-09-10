using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260910090000_AddHindiMasterNames")]
public partial class AddHindiMasterNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Guarded additions keep this migration safe for development databases where a table
        // or one of these columns was already created during an interrupted migration attempt.
        var columns = new (string Table, string Column, string Type)[]
        {
            ("Zones", "ZoneNameHi", "nvarchar(100)"),
            ("Villages", "VillageNameHi", "nvarchar(100)"),
            ("Growers", "GrowerNameHi", "nvarchar(100)"),
            ("Growers", "FatherNameHi", "nvarchar(100)"),
            ("VehicleTypes", "VehicleTypeNameHi", "nvarchar(50)"),
            ("Items", "ItemNameHi", "nvarchar(50)"),
            ("Parties", "PartyNameHi", "nvarchar(100)"),
            ("CompanyConfigs", "CompanyNameHi", "nvarchar(max)"),
            ("CompanyConfigs", "AddressHi", "nvarchar(max)"),
            ("Users", "FullNameHi", "nvarchar(100)"),
        };

        foreach (var (table, column, type) in columns)
        {
            migrationBuilder.Sql($"""
                IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[{table}] ADD [{column}] {type} NULL;
                END
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        var columns = new (string Table, string Column)[]
        {
            ("Zones", "ZoneNameHi"), ("Villages", "VillageNameHi"),
            ("Growers", "GrowerNameHi"), ("Growers", "FatherNameHi"),
            ("VehicleTypes", "VehicleTypeNameHi"), ("Items", "ItemNameHi"),
            ("Parties", "PartyNameHi"), ("CompanyConfigs", "CompanyNameHi"),
            ("CompanyConfigs", "AddressHi"), ("Users", "FullNameHi")
        };

        foreach (var (table, column) in columns)
        {
            migrationBuilder.Sql($"""
                IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.{table}', N'{column}') IS NOT NULL
                BEGIN
                    ALTER TABLE [dbo].[{table}] DROP COLUMN [{column}];
                END
                """);
        }
    }
}
