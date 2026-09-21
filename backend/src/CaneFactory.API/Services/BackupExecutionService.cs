using System.Data.Common;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Services;

public enum BackupKind { Full, Differential, TransactionLog }

public sealed record BackupOperationResult(bool Success, string Message, string? FilePath = null);

/// <summary>
/// Runs SQL Server backups from the API server. The configured directory is a directory on the
/// SQL Server host. SQL Server writes the file, so its service account needs Modify permission.
/// If that directory is not writable by SQL Server, the engine's own default backup directory is
/// tried automatically so scheduled backups do not silently stop.
/// </summary>
public sealed class BackupExecutionService
{
    private readonly AppDbContext _db;
    private readonly ILogger _log;

    public BackupExecutionService(AppDbContext db, ILogger log)
        => (_db, _log) = (db, log);

    public async Task<BackupOperationResult> CreateAsync(
        BackupKind kind, bool copyOnly, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsSqlServer())
            return new(false, "Database backup is available only when the server uses SQL Server.");

        var config = await _db.BackupConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted, cancellationToken) ?? new BackupConfig();
        return await CreateAsync(config, kind, copyOnly, cancellationToken);
    }

    public async Task<BackupOperationResult> CreateAsync(
        BackupConfig config, BackupKind kind, bool copyOnly, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsSqlServer())
            return new(false, "Database backup is available only when the server uses SQL Server.");

        string configuredFolder;
        try
        {
            configuredFolder = Path.GetFullPath(config.BackupFolderPath);
            Directory.CreateDirectory(configuredFolder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new(false, $"Backup folder cannot be created or accessed by the API/IIS service: {ex.Message}");
        }

        // The connection belongs to this DbContext; do not dispose it here because
        // the scheduler persists run status through the same context afterwards.
        var connection = _db.Database.GetDbConnection();
        try
        {
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            var databaseName = connection.Database;
            if (string.IsNullOrWhiteSpace(databaseName))
                return new(false, "The SQL Server database name could not be determined.");

            if (kind == BackupKind.TransactionLog)
            {
                var recoveryModel = await ScalarStringAsync(connection,
                    "SELECT recovery_model_desc FROM sys.databases WHERE name = DB_NAME();",
                    cancellationToken);
                if (!string.Equals(recoveryModel, "FULL", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(recoveryModel, "BULK_LOGGED", StringComparison.OrdinalIgnoreCase))
                {
                    return new(true,
                        "Transaction log backup skipped because this database uses SIMPLE recovery. Full and differential backups remain enabled.");
                }
            }

            var defaultFolder = await GetSqlDefaultBackupFolderAsync(connection, cancellationToken);
            var configuredAttempt = await TryCreateBackupAsync(connection, databaseName,
                configuredFolder, kind, copyOnly, cancellationToken);

            BackupAttempt attempt = configuredAttempt;
            var usedDefaultFolder = false;
            if (!attempt.Success
                && !string.IsNullOrWhiteSpace(defaultFolder)
                && !PathsEqual(configuredFolder, defaultFolder))
            {
                _log.LogWarning(
                    "Backup to configured folder {ConfiguredFolder} failed; retrying SQL Server default folder {DefaultFolder}. Error: {Error}",
                    configuredFolder, defaultFolder, attempt.Error);
                attempt = await TryCreateBackupAsync(connection, databaseName,
                    defaultFolder!, kind, copyOnly, cancellationToken);
                usedDefaultFolder = attempt.Success;
            }

            if (!attempt.Success || string.IsNullOrWhiteSpace(attempt.Path))
            {
                var serviceAccount = await GetSqlServiceAccountAsync(connection, cancellationToken);
                var accountText = string.IsNullOrWhiteSpace(serviceAccount)
                    ? "the SQL Server service account"
                    : $"SQL Server service account '{serviceAccount}'";
                var fallbackText = !string.IsNullOrWhiteSpace(defaultFolder)
                    && !PathsEqual(configuredFolder, defaultFolder)
                    ? $" Default-folder retry ({defaultFolder}) also failed: {attempt.Error}"
                    : string.Empty;
                return new(false,
                    $"SQL Server could not create the backup in '{configuredFolder}'. Grant Modify permission on this folder to {accountText}. " +
                    $"SQL detail: {configuredAttempt.Error}.{fallbackText}");
            }

            var actualFolder = Path.GetDirectoryName(attempt.Path) ?? configuredFolder;
            await PruneExpiredFilesAsync(actualFolder, databaseName, config.RetentionDays, cancellationToken);

            if (copyOnly)
            {
                try
                {
                    await using var probe = new FileStream(attempt.Path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
                    if (probe.Length == 0)
                        return new(false, $"SQL Server created an empty backup file at '{attempt.Path}'.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new(false,
                        $"SQL Server created the backup at '{attempt.Path}', but the API/IIS service cannot read it for download. " +
                        $"Grant Read permission on that folder to the IIS application-pool identity. Detail: {ex.Message}");
                }
            }

            var locationNote = usedDefaultFolder
                ? $" The configured folder was not writable by SQL Server, so its default backup folder was used: {actualFolder}."
                : string.Empty;
            _log.LogInformation("{Kind} backup completed for {Database}; file {FileName}",
                kind, databaseName, Path.GetFileName(attempt.Path));
            // WITH CHECKSUM makes SQL Server validate the pages while creating the backup.
            // RESTORE VERIFYONLY requires CREATE DATABASE/server-level restore permissions,
            // which would be an unnecessarily powerful grant for the API account. A DBA can
            // still perform a periodic restore verification separately.
            return new(true, $"{attempt.Label} backup completed with SQL Server checksum.{locationNote}", attempt.Path);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "{Kind} backup failed", kind);
            return new(false, "Database backup failed. Technical detail: " + ex.Message);
        }
    }

    private static async Task<BackupAttempt> TryCreateBackupAsync(
        DbConnection connection, string databaseName, string folder, BackupKind kind,
        bool copyOnly, CancellationToken cancellationToken)
    {
        var (label, extension, options) = kind switch
        {
            BackupKind.Differential => ("DIFF", ".bak", "WITH DIFFERENTIAL, INIT, CHECKSUM"),
            BackupKind.TransactionLog => ("LOG", ".trn", "WITH INIT, CHECKSUM"),
            _ => ("FULL", ".bak", $"WITH INIT, CHECKSUM{(copyOnly ? ", COPY_ONLY" : string.Empty)}")
        };
        var fileName = $"{databaseName}_{label}_{DateTime.Now:yyyyMMdd_HHmmss_fff}{extension}";
        var path = Path.Combine(folder, fileName);
        var quotedDb = $"[{databaseName.Replace("]", "]]", StringComparison.Ordinal)}]";
        var statement = kind == BackupKind.TransactionLog
            ? $"BACKUP LOG {quotedDb} TO DISK = @backupPath {options};"
            : $"BACKUP DATABASE {quotedDb} TO DISK = @backupPath {options};";

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 600;
            command.CommandText = $"DECLARE @backupPath nvarchar(4000) = @path; {statement}";
            var pathParameter = command.CreateParameter();
            pathParameter.ParameterName = "@path";
            pathParameter.Value = path;
            command.Parameters.Add(pathParameter);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new(true, label, path, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(false, label, path, ex.Message);
        }
    }

    private static async Task<string?> GetSqlDefaultBackupFolderAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            var value = await ScalarStringAsync(connection,
                "SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath'));",
                cancellationToken);
            return string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('\\', '/');
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetSqlServiceAccountAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            return await ScalarStringAsync(connection,
                "SELECT TOP (1) service_account FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server (%';",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> ScalarStringAsync(
        DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'),
                Path.GetFullPath(right).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.TrimEnd('\\', '/'), right.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task PruneExpiredFilesAsync(
        string folder, string databaseName, int retentionDays, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            var olderThan = DateTime.Now.AddDays(-Math.Max(1, retentionDays));
            foreach (var file in Directory.EnumerateFiles(folder, $"{databaseName}_*.*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.GetLastWriteTime(file) < olderThan) File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Backup retention cleanup could not complete in {Folder}", folder);
        }

        await Task.CompletedTask;
    }

    private sealed record BackupAttempt(
        bool Success, string Label, string? Path, string? Error);
}
