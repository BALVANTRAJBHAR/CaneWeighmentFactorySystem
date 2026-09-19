/*
  One-time repair for an already-created SQL Server database.
  Aadhaar is optional: all three storage columns must accept NULL and only
  actual Aadhaar hashes must be unique. Run this against AFFLLPCaneFactory.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Growers', N'U') IS NULL
    THROW 51000, 'dbo.Growers was not found. Run the base database schema first.', 1;

IF COL_LENGTH(N'dbo.Growers', N'AadhaarEncrypted') IS NULL
    ALTER TABLE dbo.Growers ADD AadhaarEncrypted nvarchar(max) NULL;

IF COL_LENGTH(N'dbo.Growers', N'AadhaarLast4') IS NULL
    ALTER TABLE dbo.Growers ADD AadhaarLast4 nvarchar(max) NULL;

IF COL_LENGTH(N'dbo.Growers', N'AadhaarHash') IS NULL
    ALTER TABLE dbo.Growers ADD AadhaarHash nvarchar(450) NULL;

/* Do not remove the existing constraint if data corruption already exists. */
IF EXISTS
(
    SELECT AadhaarHash
    FROM dbo.Growers
    WHERE AadhaarHash IS NOT NULL
    GROUP BY AadhaarHash
    HAVING COUNT(*) > 1
)
    THROW 51001, 'Duplicate non-empty Aadhaar hashes exist. Resolve duplicates before applying this repair.', 1;

BEGIN TRANSACTION;

ALTER TABLE dbo.Growers ALTER COLUMN AadhaarEncrypted nvarchar(max) NULL;
ALTER TABLE dbo.Growers ALTER COLUMN AadhaarLast4 nvarchar(max) NULL;
ALTER TABLE dbo.Growers ALTER COLUMN AadhaarHash nvarchar(450) NULL;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Growers') AND name = N'IX_Growers_AadhaarHash')
    DROP INDEX IX_Growers_AadhaarHash ON dbo.Growers;

CREATE UNIQUE INDEX IX_Growers_AadhaarHash
    ON dbo.Growers (AadhaarHash)
    WHERE AadhaarHash IS NOT NULL;

COMMIT TRANSACTION;
