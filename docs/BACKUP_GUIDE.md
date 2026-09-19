# Backup guide — SQL Server Express

The CaneFactory server API runs the saved backup policy itself. SQL Server Express has no SQL
Agent and does not support `WITH COMPRESSION`; the application deliberately uses neither.

## Configure in the software

Open **Configuration → Backup** on the PC running the API and SQL Server.

1. Set **Backup Folder Path** to a folder on that server, for example `D:\CaneFactoryBackup`.
   The API creates the folder when the policy is saved.
2. Set frequency and time, turn **Backup Enabled** on, and click **Save Backup Policy**.
   - `Hourly` with `01:00` means 01:00, 02:00, 03:00, and so on.
   - `Daily` with `01:00` means every day at 01:00.
   - `Weekly` means Sunday at the chosen time.
3. Keep the server API running continuously. For IIS, set its application pool to
   **AlwaysRunning**. The scheduler checks the policy every 20 seconds.

The SQL Server service account and the API/IIS application-pool account must both have **Modify**
permission on the backup folder. SQL Server resolves the folder on the server, never on a LAN client.

## Current manual backup download

`POST /api/backup/current/download` creates a verified `COPY_ONLY` full backup, leaves it in the
configured folder, and streams it to the operator. `COPY_ONLY` does not disturb scheduled
differential backups.

## Optional Task Scheduler fallback

The Backup tab downloads a SQL script and Task Scheduler XML as a fallback if the API cannot stay
running. Copy `CaneFactoryBackup.sql` into the configured backup folder on the SQL Server PC, import
the XML, and select a Windows account that can use `sqlcmd` and access SQL Server.

Generated SQL is Express-safe:

```sql
BACKUP DATABASE [AFFLLPCaneFactory] TO DISK = @full WITH INIT, CHECKSUM;
RESTORE VERIFYONLY FROM DISK = @full WITH CHECKSUM;
```

## Restore test

Monthly, restore the newest full backup to a test database and run `DBCC CHECKDB`. Keep an encrypted
off-site copy of completed `.bak` files; a backup on the database disk alone does not protect against disk failure.
