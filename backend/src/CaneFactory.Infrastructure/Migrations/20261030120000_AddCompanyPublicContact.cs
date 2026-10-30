using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261030120000_AddCompanyPublicContact")]
public partial class AddCompanyPublicContact : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[] { "ContactMobile1", "ContactMobile2", "ContactEmail",
                     "InstagramUrl", "FacebookUrl", "TwitterUrl", "YoutubeUrl", "LinkedinUrl",
                     "SmtpHost", "SmtpUsername", "SmtpPasswordEncrypted" })
            migrationBuilder.AddColumn<string>(name: name, table: "CompanyConfigs",
                type: "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<int>(name: "SmtpPort", table: "CompanyConfigs",
            type: "int", nullable: false, defaultValue: 587);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[] { "ContactMobile1", "ContactMobile2", "ContactEmail",
                     "InstagramUrl", "FacebookUrl", "TwitterUrl", "YoutubeUrl", "LinkedinUrl",
                     "SmtpHost", "SmtpUsername", "SmtpPasswordEncrypted", "SmtpPort" })
            migrationBuilder.DropColumn(name: name, table: "CompanyConfigs");
    }
}
