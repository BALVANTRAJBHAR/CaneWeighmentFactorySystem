using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>
/// Converts the Grower primary key itself into the public six-digit Grower ID.
/// Every enforced and historical GrowerId reference is moved in the same
/// transaction, so Purchase/Payment/Loan links remain intact.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261003090000_UseSixDigitGrowerIds")]
public partial class UseSixDigitGrowerIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) != true)
        {
            migrationBuilder.Sql("UPDATE NumberSequences SET NextValue = CASE WHEN NextValue < 100001 THEN 100001 ELSE NextValue END WHERE Name = 'GrowerId';");
            migrationBuilder.Sql("INSERT INTO NumberSequences (Name, NextValue) SELECT 'GrowerId', 100001 WHERE NOT EXISTS (SELECT 1 FROM NumberSequences WHERE Name = 'GrowerId');");
            return;
        }

        migrationBuilder.Sql("""
            SET XACT_ABORT ON;

            IF EXISTS (SELECT 1 FROM Growers WHERE Id < 100001)
            BEGIN
                IF EXISTS (SELECT 1 FROM Growers WHERE Id >= 100001)
                    THROW 51001, 'Cannot remap Grower IDs because legacy and six-digit IDs are mixed. Restore backup and run the supported migration once.', 1;

                ALTER TABLE Purchases DROP CONSTRAINT FK_Purchases_Growers_GrowerId;
                ALTER TABLE Loans DROP CONSTRAINT FK_Loans_Growers_GrowerId;
                ALTER TABLE Payments DROP CONSTRAINT FK_Payments_Growers_GrowerId;
                ALTER TABLE CashBookEntries DROP CONSTRAINT FK_CashBookEntries_Growers_GrowerId;

                UPDATE Purchases SET GrowerId = GrowerId + 100000 WHERE GrowerId < 100001;
                UPDATE Loans SET GrowerId = GrowerId + 100000 WHERE GrowerId < 100001;
                UPDATE Payments SET GrowerId = GrowerId + 100000 WHERE GrowerId < 100001;
                UPDATE CashBookEntries SET GrowerId = GrowerId + 100000 WHERE GrowerId IS NOT NULL AND GrowerId < 100001;

                IF COL_LENGTH(N'dbo.LoanRecoveries', N'GrowerId') IS NOT NULL
                    EXEC(N'UPDATE dbo.LoanRecoveries SET GrowerId = GrowerId + 100000 WHERE GrowerId < 100001;');
                IF COL_LENGTH(N'dbo.PurchaseImages', N'GrowerId') IS NOT NULL
                    EXEC(N'UPDATE dbo.PurchaseImages SET GrowerId = GrowerId + 100000 WHERE GrowerId < 100001;');
                IF COL_LENGTH(N'dbo.PaymentImages', N'GrowerId') IS NOT NULL
                    EXEC(N'UPDATE dbo.PaymentImages SET GrowerId = GrowerId + 100000 WHERE GrowerId < 100001;');
                IF COL_LENGTH(N'dbo.SmsLogs', N'GrowerId') IS NOT NULL
                    EXEC(N'UPDATE dbo.SmsLogs SET GrowerId = GrowerId + 100000 WHERE GrowerId IS NOT NULL AND GrowerId < 100001;');

                UPDATE Growers SET Id = Id + 100000 WHERE Id < 100001;

                ALTER TABLE Purchases WITH CHECK ADD CONSTRAINT FK_Purchases_Growers_GrowerId
                    FOREIGN KEY (GrowerId) REFERENCES Growers(Id) ON DELETE NO ACTION;
                ALTER TABLE Loans WITH CHECK ADD CONSTRAINT FK_Loans_Growers_GrowerId
                    FOREIGN KEY (GrowerId) REFERENCES Growers(Id) ON DELETE NO ACTION;
                ALTER TABLE Payments WITH CHECK ADD CONSTRAINT FK_Payments_Growers_GrowerId
                    FOREIGN KEY (GrowerId) REFERENCES Growers(Id) ON DELETE NO ACTION;
                ALTER TABLE CashBookEntries WITH CHECK ADD CONSTRAINT FK_CashBookEntries_Growers_GrowerId
                    FOREIGN KEY (GrowerId) REFERENCES Growers(Id) ON DELETE NO ACTION;
            END;

            IF EXISTS (SELECT 1 FROM Growers WHERE Id > 999999)
                THROW 51002, 'A Grower ID exceeds the supported six-digit range.', 1;

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Growers_SixDigitId')
                ALTER TABLE Growers WITH CHECK ADD CONSTRAINT CK_Growers_SixDigitId
                    CHECK (Id BETWEEN 100001 AND 999999);

            DECLARE @NextGrowerId bigint = ISNULL((SELECT MAX(CONVERT(bigint, Id)) + 1 FROM Growers), 100001);
            IF @NextGrowerId < 100001 SET @NextGrowerId = 100001;

            MERGE NumberSequences WITH (HOLDLOCK) AS target
            USING (SELECT CAST('GrowerId' AS nvarchar(64)) AS Name, @NextGrowerId AS NextValue) AS source
               ON target.Name = source.Name
            WHEN MATCHED THEN UPDATE SET NextValue = source.NextValue
            WHEN NOT MATCHED THEN INSERT (Name, NextValue) VALUES (source.Name, source.NextValue);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately irreversible: reversing primary keys after new six-digit
        // growers have been created could corrupt transaction history.
    }
}
