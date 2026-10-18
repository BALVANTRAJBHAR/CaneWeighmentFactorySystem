/*
  Delete the ONE cancelled test payment and restart Payment ID / Advice No at 1.

  Safety rules:
  - Stop the API/IIS site before running.
  - Take a verified FULL backup first.
  - The transaction aborts unless @PaymentId exists, is CANCELLED, and is the
    only Payment row. This prevents a sequence reset from colliding with real data.
  - SQL removes image metadata, not the physical local JPG / Azure Blob. The
    final result set lists those objects so they can be removed separately.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @PaymentId int = 1; -- change only if the cancelled test payment has another ID
DECLARE @PaymentIdText nvarchar(32) = CONVERT(nvarchar(32), @PaymentId);
DECLARE @ImageFiles table (FilePath nvarchar(1000), ImageName nvarchar(260));

/* Preview exactly what will be removed. */
SELECT Id, AdviceNumber, GrowerId, NetPayableAmount, PaymentStatus, PaymentDate
FROM dbo.Payments WHERE Id = @PaymentId;

SELECT 'PaymentPurchases' AS RelatedTable, COUNT_BIG(*) AS RowCounts FROM dbo.PaymentPurchases WHERE PaymentId = @PaymentId
UNION ALL SELECT 'PaymentImages', COUNT_BIG(*) FROM dbo.PaymentImages WHERE PaymentId = @PaymentId
UNION ALL SELECT 'CashBookEntries', COUNT_BIG(*) FROM dbo.CashBookEntries WHERE PaymentId = @PaymentId
UNION ALL SELECT 'LoanRecoveries', COUNT_BIG(*) FROM dbo.LoanRecoveries WHERE PaymentId = @PaymentId
UNION ALL SELECT 'SmsLogs', COUNT_BIG(*) FROM dbo.SmsLogs
    WHERE ReferenceId = CONCAT('PAY-', @PaymentId) OR ReferenceId LIKE CONCAT('PAY-', @PaymentId, '-OWNER-%')
UNION ALL SELECT 'AuditLogs', COUNT_BIG(*) FROM dbo.AuditLogs
    WHERE EntityId = @PaymentIdText AND Module IN ('Payment', 'CashEvidence');

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK)
                   WHERE Id = @PaymentId AND PaymentStatus = 'CANCELLED')
        THROW 51010, 'Target payment does not exist or is not CANCELLED. Nothing was deleted.', 1;

    IF (SELECT COUNT_BIG(*) FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK)) <> 1
        THROW 51011, 'More than one Payment exists. Sequence reset to 1 would be unsafe.', 1;

    IF EXISTS (SELECT 1 FROM dbo.LoanRecoveries
               WHERE PaymentId = @PaymentId AND RecoveryStatus <> 'REVERSED')
        THROW 51012, 'A payment-linked loan recovery is not REVERSED. Cancel correctly before reset.', 1;

    INSERT INTO @ImageFiles (FilePath, ImageName)
    SELECT FilePath, ImageName FROM dbo.PaymentImages WHERE PaymentId = @PaymentId;

    /* Restore linked purchases to a clean payable state before removing links. */
    UPDATE p
       SET p.PaymentStatus = 'PENDING',
           p.PaymentFlag = 'N',
           p.AdviceNumber = NULL,
           p.UpdatedAt = SYSUTCDATETIME()
    FROM dbo.Purchases p
    INNER JOIN dbo.PaymentPurchases pp ON pp.PurchaseId = p.Id
    WHERE pp.PaymentId = @PaymentId;

    DELETE FROM dbo.PaymentImages WHERE PaymentId = @PaymentId;
    DELETE FROM dbo.CashBookEntries WHERE PaymentId = @PaymentId;
    DELETE FROM dbo.LoanRecoveries WHERE PaymentId = @PaymentId;
    DELETE FROM dbo.PaymentPurchases WHERE PaymentId = @PaymentId;

    DELETE FROM dbo.SmsLogs
    WHERE ReferenceId = CONCAT('PAY-', @PaymentId)
       OR ReferenceId LIKE CONCAT('PAY-', @PaymentId, '-OWNER-%');

    DELETE FROM dbo.AuditLogs
    WHERE EntityId = @PaymentIdText AND Module IN ('Payment', 'CashEvidence');

    DELETE FROM dbo.Payments WHERE Id = @PaymentId;

    IF EXISTS (SELECT 1 FROM dbo.Payments)
        THROW 51013, 'Payment rows remain; sequence was not reset.', 1;

    MERGE dbo.NumberSequences WITH (HOLDLOCK) AS target
    USING (VALUES
        (N'PaymentId', CAST(1 AS bigint)),
        (N'AdviceNumber', CAST(1 AS bigint))
    ) AS source([Name], NextValue)
    ON target.[Name] = source.[Name]
    WHEN MATCHED THEN UPDATE SET NextValue = source.NextValue
    WHEN NOT MATCHED THEN INSERT ([Name], NextValue) VALUES (source.[Name], source.NextValue);

    /* These are SQL IDENTITY detail tables; only reset when now empty. */
    IF NOT EXISTS (SELECT 1 FROM dbo.PaymentImages)
        DBCC CHECKIDENT ('dbo.PaymentImages', RESEED, 0) WITH NO_INFOMSGS;
    IF NOT EXISTS (SELECT 1 FROM dbo.PaymentPurchases)
        DBCC CHECKIDENT ('dbo.PaymentPurchases', RESEED, 0) WITH NO_INFOMSGS;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT [Name], NextValue
FROM dbo.NumberSequences
WHERE [Name] IN (N'PaymentId', N'AdviceNumber')
ORDER BY [Name];

/* Delete only these physical test files/blobs after checking the paths. */
SELECT FilePath, ImageName FROM @ImageFiles;

