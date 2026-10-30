/*
  TEST SALE WEIGHMENT CLEANUP (SQL Server / Azure SQL)
  Run in the correct CaneFactory database in SSMS.

  1. Leave @Apply = 0 to inspect the target rows and image paths.
  2. Stop all API instances and take a verified database backup.
  3. Set @Apply = 1 and execute again to delete the selected sale weighments.

  @SalePurchaseId = NULL selects ALL SalePurchases (including soft-deleted rows).
  Set an ID to delete only that one test transaction. Do not reset the
  SalePurchaseId sequence after a partial delete.

  This removes SQL image metadata, matching sale SMS and matching audit events.
  It does NOT remove JPEG files from Azure Blob/local storage, other weighments,
  payments, master data, users, or configuration. Use the previewed FilePath
  list to remove only the corresponding TEST image files after verification.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @SalePurchaseId int = NULL;
DECLARE @Apply bit = 0;

IF OBJECT_ID('tempdb..#SaleIds') IS NOT NULL DROP TABLE #SaleIds;
CREATE TABLE #SaleIds (Id int NOT NULL PRIMARY KEY);
INSERT INTO #SaleIds (Id)
SELECT Id FROM dbo.SalePurchases
WHERE @SalePurchaseId IS NULL OR Id = @SalePurchaseId;

IF @SalePurchaseId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM #SaleIds)
    THROW 51010, 'SalePurchase ID was not found; nothing deleted.', 1;

SELECT COUNT(*) AS SaleWeighmentsToDelete FROM #SaleIds;
SELECT s.Id, s.WeighmentStatus, s.VehicleNumber, s.TareDateTime, s.GrossDateTime
FROM dbo.SalePurchases AS s JOIN #SaleIds AS t ON t.Id = s.Id ORDER BY s.Id;

SELECT i.SalePurchaseId, i.Id AS ImageId, i.CaptureStage, i.FilePath
FROM dbo.SalePurchaseImages AS i JOIN #SaleIds AS t ON t.Id = i.SalePurchaseId
ORDER BY i.SalePurchaseId, i.Id;

SELECT 'SalePurchaseImages' AS RelatedTable, COUNT_BIG(*) AS RowsToDelete
FROM dbo.SalePurchaseImages AS i JOIN #SaleIds AS t ON t.Id = i.SalePurchaseId
UNION ALL
SELECT 'SmsLogs', COUNT_BIG(*) FROM dbo.SmsLogs AS s
WHERE s.EventCode = N'SALE_PURCHASE_COMPLETED'
  AND EXISTS (SELECT 1 FROM #SaleIds AS t
              WHERE s.ReferenceId LIKE CONCAT(N'SP-', t.Id, N'-%'))
UNION ALL
SELECT 'AuditLogs', COUNT_BIG(*) FROM dbo.AuditLogs AS a
WHERE (a.Entity = N'SalePurchase' OR (a.Module = N'SalePurchase' AND a.Entity = N'SaleInvoice'))
  AND EXISTS (SELECT 1 FROM #SaleIds AS t WHERE a.EntityId = CONVERT(nvarchar(20), t.Id));

IF @Apply = 0
BEGIN
    PRINT 'PREVIEW ONLY. Set @Apply = 1 after backup and stopping all API instances.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Refuse to delete a transaction that appeared after the preview selection.
    IF EXISTS (SELECT 1 FROM #SaleIds AS t
               WHERE NOT EXISTS (SELECT 1 FROM dbo.SalePurchases AS s WITH (UPDLOCK, HOLDLOCK) WHERE s.Id = t.Id))
        THROW 51011, 'SalePurchase rows changed after preview. Transaction rolled back.', 1;

    DELETE i FROM dbo.SalePurchaseImages AS i
    JOIN #SaleIds AS t ON t.Id = i.SalePurchaseId;

    DELETE s FROM dbo.SmsLogs AS s
    WHERE s.EventCode = N'SALE_PURCHASE_COMPLETED'
      AND EXISTS (SELECT 1 FROM #SaleIds AS t
                  WHERE s.ReferenceId LIKE CONCAT(N'SP-', t.Id, N'-%'));

    DELETE a FROM dbo.AuditLogs AS a
    WHERE (a.Entity = N'SalePurchase' OR (a.Module = N'SalePurchase' AND a.Entity = N'SaleInvoice'))
      AND EXISTS (SELECT 1 FROM #SaleIds AS t WHERE a.EntityId = CONVERT(nvarchar(20), t.Id));

    DELETE s FROM dbo.SalePurchases AS s JOIN #SaleIds AS t ON t.Id = s.Id;

    IF EXISTS (SELECT 1 FROM dbo.SalePurchases AS s JOIN #SaleIds AS t ON t.Id = s.Id)
       OR EXISTS (SELECT 1 FROM dbo.SalePurchaseImages AS i JOIN #SaleIds AS t ON t.Id = i.SalePurchaseId)
        THROW 51012, 'Sale weighment cleanup verification failed. Transaction rolled back.', 1;

    -- SalePurchase IDs come from NumberSequences (not SQL IDENTITY). Only a
    -- full cleanup may restart the business ID at 1. Partial-delete IDs are
    -- never reused while other sale records remain.
    IF @SalePurchaseId IS NULL AND NOT EXISTS (SELECT 1 FROM dbo.SalePurchases)
    BEGIN
        UPDATE dbo.NumberSequences SET NextValue = 1 WHERE [Name] = N'SalePurchaseId';
        IF @@ROWCOUNT <> 1
            THROW 51013, 'SalePurchaseId sequence was not found; transaction rolled back.', 1;
    END;

    COMMIT TRANSACTION;
    PRINT 'Selected test sale weighments and related SQL records deleted.';
    IF @SalePurchaseId IS NULL
        PRINT 'Full cleanup complete. The next SalePurchase ID will be 1.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
