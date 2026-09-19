using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>
/// Makes the optional Aadhaar contract explicit in existing SQL Server
/// databases.  In particular, any number of growers may have no Aadhaar.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260916160000_FixOptionalGrowerAadhaar")]
public partial class FixOptionalGrowerAadhaar : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Growers_AadhaarHash",
            table: "Growers");

        migrationBuilder.AlterColumn<string>(
            name: "AadhaarHash",
            table: "Growers",
            type: "nvarchar(450)",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(450)",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "AadhaarLast4",
            table: "Growers",
            type: "nvarchar(max)",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "AadhaarEncrypted",
            table: "Growers",
            type: "nvarchar(max)",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Growers_AadhaarHash",
            table: "Growers",
            column: "AadhaarHash",
            unique: true,
            filter: "[AadhaarHash] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Growers_AadhaarHash",
            table: "Growers");

        migrationBuilder.CreateIndex(
            name: "IX_Growers_AadhaarHash",
            table: "Growers",
            column: "AadhaarHash",
            unique: true);
    }
}
