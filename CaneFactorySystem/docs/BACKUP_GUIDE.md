# BACKUP GUIDE — SQL Server 2019 Express

Express edition has no SQL Agent — use Windows Task Scheduler with `sqlcmd`.

## Phase 13: generate the scripts from the app instead of hand-writing them
Developer Dashboard → Configuration → Backup tab (or `GET/PUT /api/backup/config`,
`Backup.View`/`Backup.Configure`) stores the desired Frequency/TimeOfDay/RetentionDays/Folder
policy. From that policy:
- `GET /api/backup/script` downloads the exact FULL+DIFFERENTIAL+TRANSACTION LOG `.sql` script
  below, pre-filled with your folder/retention.
- `GET /api/backup/task-scheduler-xml` downloads a ready-to-import Task Scheduler XML
  (`schtasks /Create /XML CaneFactoryBackup-Task.xml /TN CaneFactoryBackup`).
Save both next to the database server and import the XML — no manual editing needed. The
manual script below remains as reference/fallback.

## Strategy
| Type | Frequency | Retention |
|---|---|---|
| FULL backup | daily 02:00 | 14 days local + external copy |
| DIFFERENTIAL | every 4 hours (working season) | until next full |
| TRANSACTION LOG | every 30 min (database uses FULL recovery) | until next full |
| Image folder (`D:\CanePaymentData`) | daily robocopy mirror | per retention policy |

## Scripts (save as .sql, schedule via Task Scheduler → sqlcmd -S localhost\SQLEXPRESS -i script.sql)
```sql
-- FULL
DECLARE @f NVARCHAR(300) = N'E:\Backup\AFFLLPCaneFactory_FULL_' + FORMAT(GETDATE(),'yyyyMMdd_HHmm') + '.bak';
BACKUP DATABASE AFFLLPCaneFactory TO DISK=@f WITH INIT, CHECKSUM, COMPRESSION;
RESTORE VERIFYONLY FROM DISK=@f WITH CHECKSUM;   -- verification

-- DIFFERENTIAL
DECLARE @d NVARCHAR(300) = N'E:\Backup\AFFLLPCaneFactory_DIFF_' + FORMAT(GETDATE(),'yyyyMMdd_HHmm') + '.bak';
BACKUP DATABASE AFFLLPCaneFactory TO DISK=@d WITH DIFFERENTIAL, INIT, CHECKSUM;

-- TRANSACTION LOG
DECLARE @l NVARCHAR(300) = N'E:\Backup\AFFLLPCaneFactory_LOG_' + FORMAT(GETDATE(),'yyyyMMdd_HHmm') + '.trn';
BACKUP LOG AFFLLPCaneFactory TO DISK=@l WITH INIT, CHECKSUM;
```
Note: COMPRESSION is ignored on Express (allowed syntax); it activates automatically after a
Standard upgrade.

## Restore test procedure (perform monthly)
```sql
RESTORE DATABASE AFFLLPCaneFactory_TEST FROM DISK='E:\Backup\<full>.bak'
  WITH MOVE 'AFFLLPCaneFactory' TO 'E:\RestoreTest\AFFLLPCaneFactory_TEST.mdf',
       MOVE 'AFFLLPCaneFactory_log' TO 'E:\RestoreTest\AFFLLPCaneFactory_TEST_log.ldf',
       NORECOVERY;
RESTORE DATABASE AFFLLPCaneFactory_TEST FROM DISK='E:\Backup\<diff>.bak' WITH NORECOVERY;
RESTORE LOG AFFLLPCaneFactory_TEST FROM DISK='E:\Backup\<log>.trn' WITH RECOVERY;
-- sanity checks
SELECT COUNT(*) FROM AFFLLPCaneFactory_TEST.dbo.Purchases;
DBCC CHECKDB('AFFLLPCaneFactory_TEST');
DROP DATABASE AFFLLPCaneFactory_TEST;
```

## Monitoring
The dashboard health panel shows Storage % with configurable Warning/Critical thresholds
(System Settings → Health.Storage.Warning/Critical). Keep backups on a separate physical disk
and copy off-site/cloud when internet is available.
