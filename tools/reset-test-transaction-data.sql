/*
  CaneFactory: TEST transactional-data cleanup

  PURPOSE
  -------
  Removes test/business transaction data only. Master data and operational
  configuration are deliberately retained.

  RETAINED
  --------
  Growers, Zones, Villages, Banks, VehicleTypes, VarietyTypes, Varieties,
  Rates, SaleItemRates, Items, Crops, Parties, Seasons, PaymentModes,
  LoanTypes, ExpenseTypes, Users/Roles/Permissions, and all application,
  printer, camera, SMS, device, company and backup configuration.

  DELETED
  -------
  Cane/Sale weighments and image metadata, payments and their detail/evidence,
  loans/recoveries, cash book, expenses, test SMS/audit/OTP/session history,
  and rate-override evidence/audit data.

  IMPORTANT
  ---------
  1. Stop IIS / the CaneFactory API before executing this script so no user can
     save a transaction while the reset runs.
  2. Take and verify a fresh FULL SQL backup first.
  3. This script DOES NOT remove physical JPG/image files. After it succeeds,
     remove only TEST files from the configured WeighmentImage and RateEditImage
     storage roots after verifying the fresh backup.
  4. Run the PREVIEW section first. For the actual reset, run the RESET section.
  5. This is intentionally DELETE (not TRUNCATE): SQL Server cannot TRUNCATE a
     table referenced by a foreign key. DBCC CHECKIDENT resets true identity
     columns; NumberSequences resets application business IDs.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* ============================== PREVIEW ============================== */
SELECT 'WeighmentRateOverrides' AS TableName, COUNT_BIG(*) AS RowCounts FROM dbo.WeighmentRateOverrides
UNION ALL SELECT 'RateOverrideEvidences', COUNT_BIG(*) FROM dbo.RateOverrideEvidences
UNION ALL SELECT 'PaymentImages', COUNT_BIG(*) FROM dbo.PaymentImages
UNION ALL SELECT 'PaymentPurchases', COUNT_BIG(*) FROM dbo.PaymentPurchases
UNION ALL SELECT 'CashBookEntries', COUNT_BIG(*) FROM dbo.CashBookEntries
UNION ALL SELECT 'LoanRecoveries', COUNT_BIG(*) FROM dbo.LoanRecoveries
UNION ALL SELECT 'Payments', COUNT_BIG(*) FROM dbo.Payments
UNION ALL SELECT 'PurchaseImages', COUNT_BIG(*) FROM dbo.PurchaseImages
UNION ALL SELECT 'Purchases', COUNT_BIG(*) FROM dbo.Purchases
UNION ALL SELECT 'SalePurchaseImages', COUNT_BIG(*) FROM dbo.SalePurchaseImages
UNION ALL SELECT 'SalePurchases', COUNT_BIG(*) FROM dbo.SalePurchases
UNION ALL SELECT 'Expenses', COUNT_BIG(*) FROM dbo.Expenses
UNION ALL SELECT 'SmsLogs', COUNT_BIG(*) FROM dbo.SmsLogs
UNION ALL SELECT 'UserOtps', COUNT_BIG(*) FROM dbo.UserOtps
UNION ALL SELECT 'RefreshTokens', COUNT_BIG(*) FROM dbo.RefreshTokens
UNION ALL SELECT 'DeviceConfigHistories', COUNT_BIG(*) FROM dbo.DeviceConfigHistories
UNION ALL SELECT 'AuditLogs', COUNT_BIG(*) FROM dbo.AuditLogs
ORDER BY TableName;

/* =============================== RESET =============================== */
/*
BEGIN TRANSACTION;

-- Deepest dependent / evidence rows first.
DELETE FROM dbo.WeighmentRateOverrides;
DELETE FROM dbo.RateOverrideEvidences;
DELETE FROM dbo.PaymentImages;
DELETE FROM dbo.PaymentPurchases;
DELETE FROM dbo.CashBookEntries;
DELETE FROM dbo.LoanRecoveries;

-- Main financial and weighment transactions.
DELETE FROM dbo.Payments;
DELETE FROM dbo.Loans;
DELETE FROM dbo.PurchaseImages;
DELETE FROM dbo.Purchases;
DELETE FROM dbo.SalePurchaseImages;
DELETE FROM dbo.SalePurchases;
DELETE FROM dbo.Expenses;

-- Test/security/operational history. User accounts and configuration remain.
DELETE FROM dbo.SmsLogs;
DELETE FROM dbo.UserOtps;
DELETE FROM dbo.RefreshTokens;
DELETE FROM dbo.DeviceConfigHistories;
DELETE FROM dbo.AuditLogs;

-- Reset SQL identity columns. The next inserted row becomes 1 on empty tables.
DBCC CHECKIDENT ('dbo.RateOverrideEvidences', RESEED, 0);
DBCC CHECKIDENT ('dbo.WeighmentRateOverrides', RESEED, 0);
DBCC CHECKIDENT ('dbo.PaymentImages', RESEED, 0);
DBCC CHECKIDENT ('dbo.PaymentPurchases', RESEED, 0);
DBCC CHECKIDENT ('dbo.CashBookEntries', RESEED, 0);
DBCC CHECKIDENT ('dbo.PurchaseImages', RESEED, 0);
DBCC CHECKIDENT ('dbo.SalePurchaseImages', RESEED, 0);
DBCC CHECKIDENT ('dbo.Expenses', RESEED, 0);
DBCC CHECKIDENT ('dbo.SmsLogs', RESEED, 0);
DBCC CHECKIDENT ('dbo.UserOtps', RESEED, 0);
DBCC CHECKIDENT ('dbo.RefreshTokens', RESEED, 0);
DBCC CHECKIDENT ('dbo.DeviceConfigHistories', RESEED, 0);
DBCC CHECKIDENT ('dbo.AuditLogs', RESEED, 0);

-- These are application-controlled transaction IDs, NOT SQL identity columns.
-- Do not delete/modify GrowerId or GrowerSeq:* because Growers are retained.
MERGE dbo.NumberSequences AS target
USING (VALUES
    (N'PurchaseId', CAST(1 AS bigint)),
    (N'SalePurchaseId', CAST(1 AS bigint)),
    (N'PaymentId', CAST(1 AS bigint)),
    (N'AdviceNumber', CAST(1 AS bigint)),
    (N'LoanId', CAST(1 AS bigint)),
    (N'LRId', CAST(1 AS bigint))
) AS source([Name], NextValue)
ON target.[Name] = source.[Name]
WHEN MATCHED THEN UPDATE SET NextValue = source.NextValue
WHEN NOT MATCHED THEN INSERT ([Name], NextValue) VALUES (source.[Name], source.NextValue);

-- Verification: every transactional table must be empty.
IF EXISTS (SELECT 1 FROM dbo.WeighmentRateOverrides)
   OR EXISTS (SELECT 1 FROM dbo.RateOverrideEvidences)
   OR EXISTS (SELECT 1 FROM dbo.PaymentImages)
   OR EXISTS (SELECT 1 FROM dbo.PaymentPurchases)
   OR EXISTS (SELECT 1 FROM dbo.CashBookEntries)
   OR EXISTS (SELECT 1 FROM dbo.LoanRecoveries)
   OR EXISTS (SELECT 1 FROM dbo.Payments)
   OR EXISTS (SELECT 1 FROM dbo.Loans)
   OR EXISTS (SELECT 1 FROM dbo.PurchaseImages)
   OR EXISTS (SELECT 1 FROM dbo.Purchases)
   OR EXISTS (SELECT 1 FROM dbo.SalePurchaseImages)
   OR EXISTS (SELECT 1 FROM dbo.SalePurchases)
   OR EXISTS (SELECT 1 FROM dbo.Expenses)
   OR EXISTS (SELECT 1 FROM dbo.SmsLogs)
   OR EXISTS (SELECT 1 FROM dbo.UserOtps)
   OR EXISTS (SELECT 1 FROM dbo.RefreshTokens)
   OR EXISTS (SELECT 1 FROM dbo.DeviceConfigHistories)
   OR EXISTS (SELECT 1 FROM dbo.AuditLogs)
    THROW 51000, 'Verification failed: one or more transactional tables still contain data. Transaction rolled back.', 1;

COMMIT TRANSACTION;
*/

/* ===================== OPTIONAL GROWER-MASTER RESET ==================== */
/*
  Run this ONLY after the RESET section above has committed and only when every
  testing Grower itself must be removed. It keeps System Users (Developer,
  Admin, Sub Admin, Operator, Accountant), Roles, Permissions and every other
  master. Farmer login accounts are deleted because Users.GrowerId restricts a
  physical Grower delete.

  Important: Grower IDs intentionally start at 100001, not 1. The application
  enforces six-digit Grower IDs, so resetting this sequence to 1 would break
  the grower creation workflow.

BEGIN TRANSACTION;

-- Remove only farmer accounts that are linked to a Grower. UserRoles and any
-- already-cleared RefreshTokens are cascade/dependent data; system users have
-- GrowerId NULL and remain untouched.
DELETE FROM dbo.Users WHERE GrowerId IS NOT NULL;

-- All Purchase/Payment/Loan/CashBook transaction rows must already be empty.
IF EXISTS (SELECT 1 FROM dbo.Purchases)
   OR EXISTS (SELECT 1 FROM dbo.Payments)
   OR EXISTS (SELECT 1 FROM dbo.Loans)
   OR EXISTS (SELECT 1 FROM dbo.CashBookEntries)
    THROW 51001, 'Growers cannot be reset until transaction cleanup has completed.', 1;

DELETE FROM dbo.Growers;

-- Restart the six-digit Grower master number and clear per-village serials.
MERGE dbo.NumberSequences AS target
USING (VALUES (N'GrowerId', CAST(100001 AS bigint))) AS source([Name], NextValue)
ON target.[Name] = source.[Name]
WHEN MATCHED THEN UPDATE SET NextValue = source.NextValue
WHEN NOT MATCHED THEN INSERT ([Name], NextValue) VALUES (source.[Name], source.NextValue);

DELETE FROM dbo.NumberSequences WHERE [Name] LIKE N'GrowerSeq:%';

IF EXISTS (SELECT 1 FROM dbo.Growers)
    THROW 51002, 'Verification failed: Grower rows remain. Transaction rolled back.', 1;

COMMIT TRANSACTION;
*/

/* =========================== POST-RESET CHECK ======================== */
SELECT [Name], NextValue
FROM dbo.NumberSequences
WHERE [Name] IN (N'PurchaseId', N'SalePurchaseId', N'PaymentId', N'AdviceNumber', N'LoanId', N'LRId', N'GrowerId')
ORDER BY [Name];
