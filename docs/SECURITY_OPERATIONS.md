# Security deployment and recovery

## November 2026 service incident

- Azure App Service returned HTTP 503 because `WEBSITE_RUN_FROM_PACKAGE=1` had ZIPs in `SitePackages` but no `packagename.txt`. Restoring the pointer to a verified existing ZIP and restarting the app returned `/api/ping` and `/api/health` to HTTP 200. `tools/deploy-azure-api.ps1` now uses `--clean false` and checks the active package pointer before pruning old ZIPs. After every deployment, check both endpoints and the package pointer; keep a rollback ZIP.
- LAN IIS returned HTTP 500.30. The Windows Application log showed Azure SQL error 40615: the factory PC's changing public IP was absent from the Azure SQL firewall. Narrow /32 firewall entries allowed temporary startup, but the egress IP changed again during the same session. Keep firewall entries narrow; do not allow all IPs as a workaround. A separate direct API run against the machine's local `AFFLLPCaneFactory` SQL database returned `/api/health` 200. `Program.cs` now prefers the explicit machine connection string over a stale IIS process value. The prepared LAN API package still needs deployment to the IIS folder and a health check; until then the installed IIS binary remains dependent on Azure SQL.
- The LAN client was built with the factory Ethernet URL `http://192.168.31.10:5000`; the Ethernet adapter was disconnected. The Wi-Fi address changed from `10.175.41.59` to `192.168.31.11` during this investigation. Reserve a fixed address for the factory PC in the router, then build with `tools/build-windows-client.ps1 -ApiBaseUrl http://<factory-PC-IP>:5000`. Verify `/api/ping` from another client PC on the same network before distributing the build.
- The first Azure ZIP upload lost its connection before activation; the second succeeded and passed health checks. A cleanup bug then removed `packagename.txt`. It was restored and the app was restarted; `/api/health` returned 200 and production `/swagger/index.html` returned 404. Automatic package deletion was removed from `tools/deploy-azure-api.ps1`.

### Finish the LAN IIS update

The current IIS folder was not replaced during the investigation. The latest LAN API package is `artifacts/lan-api-invoice-layout-final2-20261102`. An existing copy of the IIS folder is at `C:\Balvant\API\AFFLLPCaneFactoryAPI202627_rollback_20261102`, and a local SQL backup is at `C:\Backup\AFFLLPCaneFactory_pre_security_20261102.bak`. From an **elevated PowerShell** in the repository root, deploy during a maintenance window:

```powershell
$target = 'C:\Balvant\API\AFFLLPCaneFactoryAPI202627'
$package = (Resolve-Path 'artifacts\lan-api-invoice-layout-final2-20261102').Path
$offline = Join-Path $target 'app_offline.htm'
Set-Content -LiteralPath $offline -Value 'Updating API'
Start-Sleep -Seconds 5
try {
    Get-ChildItem -LiteralPath $package -Force |
        Copy-Item -Destination $target -Recurse -Force -ErrorAction Stop
} finally {
    Remove-Item -LiteralPath $offline -Force
}
Invoke-WebRequest http://127.0.0.1:5000/api/health
```

The package excludes `appsettings.json` and `web.config`, so server-only settings remain in place. The first startup applies pending EF migrations to the local SQL database; check the Windows Application event log if health does not return 200. Distribute the Windows LAN client only after `/api/health` succeeds from another LAN PC. The current Wi-Fi address can change, so reserve it before distributing a URL-specific client package.

## Required settings before deploying this change

1. Replace the previously tracked Scale Bridge key on **both** the API and the bridge. The API should receive `CANE_SCALE_AGENT_KEY` as a server environment variable. Set the same new high-entropy value in the bridge's private deployment configuration; never commit it. The checked-in `AgentKey` fields are intentionally empty. Restart both services.
2. ClamAV scanning is optional and currently off. Uploads continue through server-side size, signature, image decoding/re-encoding and basic PDF checks, which cannot guarantee malware detection. To add ClamAV later, run a private daemon reachable only by the API and set `QuotationScanner:Enabled=true`, `QuotationScanner:Host` and `QuotationScanner:Port` (3310 by default). Once enabled, uploads fail closed if the scanner is unavailable. Verify a harmless JPG/PDF upload and a deliberately malformed file in a staging environment before rollout.
   Existing payment evidence and company logos were saved before these checks; inspect or rescan those stored objects separately. Virus signatures cannot prove that every new or previously unknown malicious document is safe. PDFs are scanned and checked for obvious active features, but a full PDF content-disarm-and-reconstruction service is still needed for stronger protection against complex PDF payloads.
3. Staff login MFA remains disabled by the owner's decision (`Security:StaffMfaEnabled=false`). Normal login needs a password but no email OTP. Forgot Password codes are separate. Keep this setting false in local and cloud configuration; Company SMTP is still needed for quotation email and email password-reset fallback.
4. Configure the HTTP SMS provider for password-reset codes. If it is unavailable, the API tries the user's registered email through Company SMTP. Android SIM gateway queue delivery is deliberately not used for reset codes because that queue stores message text; those users need a registered email until an encrypted OTP queue is deployed. Codes are never logged. Test delivery and reset in staging.
5. Require HTTPS at the public Azure App Service/reverse proxy and use a valid certificate. LAN HTTP remains available for the digitizer setup and requires a separate TLS rollout. Configure the SQL connection string with `Encrypt=True;TrustServerCertificate=False` after installing a trusted SQL Server certificate. Keep connection strings and API secrets out of version control.
6. Production API logs are written as structured JSON to stdout. Enable App Service diagnostic logging / Azure Monitor collection, set retention and alerts for 401/403 spikes and server errors, and restrict log-reader roles. The database audit trail remains separate. Do not put passwords, OTPs or upload bytes in logs.

## Access-token duration

Set `Jwt:AccessTokenMinutes` to `30` in the API's server-only configuration, or set the environment variable `Jwt__AccessTokenMinutes=30`, then restart the API. The default checked-in value is 15. The 7-day refresh duration is `Jwt:RefreshTokenDays`. Longer access tokens extend the window before a stolen token expires, although this API also checks user status and token version.

## App version

Change `version: 1.0.0+1` in `frontend/cane_factory_app/pubspec.yaml`. The number before `+` is the visible Android/iOS app version; the number after `+` is the monotonically increasing build number. Build again after changing it. Flutter passes this version to the Android and iOS projects. For a Windows executable's file properties, inspect `frontend/cane_factory_app/windows/runner/Runner.rc` as well.

## Azure recovery

Azure SQL Database provides automatic point-in-time backups. In Azure Portal: SQL databases > select the database > Backups/Retention policies to inspect the actual retention and redundancy. Restore creates a new database from a chosen point in time; test that process periodically. Configure long-term retention separately if required. The application cannot read or download Azure SQL automatic `.bak` files.

Azure Blob Storage does **not** gain an equivalent recoverable backup merely because files are uploaded. In the Storage Account > Data protection, inspect/enable blob soft delete, container soft delete, versioning and point-in-time restore where supported. For protection from account deletion/ransomware, configure Azure Backup for Blobs with a backup policy and appropriate vault/retention. Test restoration of a sample image. Keep backup administration separate from the app identity.

For local SQL Server backups, the current job uses `BACKUP ... WITH CHECKSUM` but does not encrypt the `.bak`. Keep the backup folder ACL restricted, copy backups to an independent protected destination, configure SQL Server backup encryption/TDE with DBA-managed certificates where supported, and test a restore on another machine. Preserve encryption certificates and keys outside the database server.

## Certificate pinning

No certificate pin was added. The Android app permits configurable cloud and LAN API addresses and the Azure certificate can rotate. A wrong/stale pin would prevent valid clients from connecting. Standard TLS certificate validation remains in place. Add pinning only after fixed production hostnames, primary and backup public-key pins, expiry and rotation tests are available.
