# Azure deployment: API, SQL, images and clients

This project supports two evidence-image modes from **Developer > Configuration > Cameras**:

- **Local / LAN server**: images are stored below the configured `ImageRoot` on the PC that runs the API.
- **Azure Cloud Blob**: images are stored in a private Azure Blob container. Database rows keep an
  `azureblob://container/folder/file.jpg` locator; View Images, reports, rate-edit validation and A4
  printing read the same object through the API.

The cloud folder layout remains recognizable:

```text
<prefix>/Cane Weighment/yyyy/MM/dd/PUR-<id>/...
<prefix>/SalePurchase Weighment/yyyy/MM/dd/SP-<id>/...
<prefix>/Payment/yyyy-MM-dd/...
<prefix>/Rate Edit/Cane|Sale/yyyy-MM-dd/...
```

## Important camera limitation

Azure App Service cannot normally reach cameras whose addresses are private factory-LAN addresses
such as `192.168.x.x`. Blob storage solves durable image storage, but it does not create a network
route from Azure to those cameras.

For the first Azure deployment choose one of these designs:

1. **Recommended:** keep an on-premises edge service for digitizer/camera capture; it uploads the
   captured JPEG to the cloud API/Blob store. Do not expose camera RTSP/HTTP ports publicly.
2. Connect Azure to the factory LAN with a private VPN and App Service VNet integration. This is not
   normally a free-tier design.
3. Until the edge uploader/VPN is ready, keep the API that performs camera capture on the LAN.

The current Cloud Blob switch is complete for storage and retrieval. A cloud API can capture a
camera only when that API has a valid network route to the camera.

## Azure resources

Create one resource group and these resources in the same/nearest India region:

1. Azure SQL logical server and database.
2. Azure Storage account (StorageV2) and private Blob container, for example
   `canefactory-images`.
3. Azure App Service for the ASP.NET Core 8 API.
4. Later, use Azure Static Web Apps for the Flutter web build, or host it in a separate App Service.
   The Windows and mobile apps remain client applications and call the HTTPS API URL.

Azure SQL's free offer is suitable for initial development/proof-of-concept use, but it has no SLA
and can pause when its monthly free allowance is exhausted. Select the "auto-pause until next month"
or bill-overage behavior deliberately. Also note that App Service VNet integration is not available on
the Free/Shared tiers, so a free App Service cannot use that route to private factory cameras.

Do not publish SQL port 1433, camera ports or the digitizer to the Internet.

## API App Service settings

Set these in **App Service > Environment variables > App settings** (never commit their values):

```text
ASPNETCORE_ENVIRONMENT=Production
CANE_DB_PROVIDER=SqlServer
CANE_CONNECTION_STRING=<Azure SQL connection string>
CANE_JWT_SECRET=<long random secret>
CANE_ENCRYPTION_KEY=<long random encryption key>
CANE_SEED_DEV_PASSWORD=<one-time strong developer password>
CANE_CAMERA_SIMULATOR=false
CANE_CAMERA_CAPTURE_MODE=EDGE_PC
CANE_IMAGE_STORAGE_MODE=AZURE_BLOB
CANE_AZURE_BLOB_SERVICE_URI=https://<storage-account>.blob.core.windows.net
```

Existing `CANE_*` values from the LAN server do not automatically move to Azure. Add them as App
Service settings. Keep the encryption key identical when encrypted configuration values are migrated.

Preferred Blob authentication:

1. Enable the App Service **system-assigned managed identity**.
2. On the Storage Account IAM page assign that identity **Storage Blob Data Contributor**.
3. Set only `CANE_AZURE_BLOB_SERVICE_URI`; do not store an account key.

Fallback authentication is supported with the server-only
`CANE_AZURE_BLOB_CONNECTION_STRING`, but managed identity is preferred.

After the API starts, sign in as Developer and select **Azure Cloud Blob**, enter the container and
optional prefix, then Save. The API verifies/creates the private container before enabling cloud mode.
Select **Local / LAN server** to return new captures to the configured LAN path. Blob-backed records
remain portable. A record that still contains a `C:\...` local path is readable only while that file is
available to the API host; copy historical local evidence to Blob and update its locator before moving
the API to Azure, or archive it with the local server. Do not assume an Azure App Service can read the
old factory PC's drive.

## Database and migration

The API currently runs pending EF Core migrations during startup and then runs the idempotent seed.
Therefore the Azure SQL login used by the first deployment needs schema-change permission. For a
more controlled production release, run an EF migration bundle once in the deployment pipeline and
use a lower-privilege runtime login afterward.

Before switching clients:

1. Restore/migrate a copy of the database to Azure SQL.
2. Start the API and confirm `/api/health` over HTTPS.
3. Confirm login, one read-only report and one test write.
4. Enable Azure Blob mode and test upload, View Images and PDF rendering.
5. Only then rebuild Windows/mobile/web clients with the Azure HTTPS API base URL.

## Publishing

Use the verified deployment script from the repository root:

```powershell
.\tools\deploy-azure-api.ps1 `
  -ResourceGroup AuthenticFoodFarm `
  -AppName authenticfoodfarmApi
```

It creates a compact Windows x64 ZIP, temporarily enables the App Service SCM publishing policy,
uploads and activates the package, verifies `/api/health`, and restores SCM publishing to disabled.
The script prevents a VS Code deployment notification from being treated as success before Kudu has
actually activated the package. It also excludes source settings, stale nested publish folders,
FFmpeg (owned by the factory edge PC), and debug symbols from the cloud ZIP.

To deploy an already-built ZIP:

```powershell
.\tools\deploy-azure-api.ps1 `
  -ResourceGroup AuthenticFoodFarm `
  -AppName authenticfoodfarmApi `
  -PackagePath .\artifacts\AZURE_API_COMPACT_20261021.zip
```

Configure App Service Health Check to `/api/health` and enable Application Insights/log streaming
before the client cut-over.

Build clients with the final HTTPS API URL. A Windows package can be shared as a ZIP through
WhatsApp for small deployments, but a versioned HTTPS download link (Azure Blob with a time-limited
SAS, Azure Static Web Apps, or a release page) is easier to update and verify. Always share the full
Windows release folder, not only the `.exe`.

## Web application

Flutter web does not have to run in the API App Service. The preferred split is:

```text
Azure Static Web Apps (Flutter web) -> HTTPS -> App Service (API) -> Azure SQL + private Blob
```

Add only the exact web origin to `Cors:AllowedOrigins`. Keep authentication, authorization and all
database/image operations in the API; the browser must never receive a SQL or Storage connection
string.

## Cut-over checklist

- Azure SQL firewall/private access is configured for the App Service.
- App Service HTTPS-only is enabled.
- JWT/encryption secrets are App Service settings, not source files.
- Managed identity has Blob Data Contributor on only the intended storage scope.
- Blob container public access is disabled.
- Database backup/restore has been tested.
- Camera capture has an edge uploader or private VPN route before cloud-only operation.
- Windows/mobile/web API base URLs point to the Azure HTTPS URL.
- CORS lists only the deployed web origin.
- Image upload, image viewing, rate edit, purchase/sale final save, PDF and reprint are smoke-tested.
# Factory-PC camera edge with Azure

Camera live view and capture must remain on the factory LAN; Azure App Service
cannot connect to private camera addresses such as `192.168.x.x`.

- Azure API: set `CANE_CAMERA_CAPTURE_MODE=EDGE_PC` and configure Azure SQL
  plus private Blob storage.
- Factory-PC API: run the same API build on loopback (for example
  `http://127.0.0.1:5001`), point it to the same Azure SQL database and Blob
  container, set `CANE_CAMERA_CAPTURE_MODE=SERVER` and
  `CANE_IMAGE_STORAGE_MODE=AZURE_BLOB`. Bind it to loopback
  unless another trusted factory PC must use it.
- Windows cloud build: pass
  `API_BASE_URL=https://<azure-api>` and
  `CAMERA_EDGE_API_BASE_URL=http://127.0.0.1:5001`. The repository build command is:

  ```powershell
  .\tools\build-windows-client.ps1 `
    -ApiBaseUrl https://<azure-api> `
    -CameraEdgeApiBaseUrl http://127.0.0.1:5001 `
    -OutputDirectory .\artifacts\windows-cloud
  ```

  The compiled value is only the first-run default. A Developer can change the
  camera endpoint later from **Configuration > Cameras > This PC / LAN Camera
  Service URL**. The override is stored only on that Windows client and does
  not change the Azure business API URL.

- Offline/LAN build: point `API_BASE_URL` at the local server and omit
  `CAMERA_EDGE_API_BASE_URL`; camera and business calls then use the same server:

  ```powershell
  .\tools\build-windows-client.ps1 `
    -ApiBaseUrl http://192.168.31.10:5000 `
    -OutputDirectory .\artifacts\windows-lan
  ```
- The Azure API commits the weighment first. The Windows client then asks the
  local edge API to capture enabled cameras. The edge API uploads the JPEG to
  Blob storage and writes its metadata to Azure SQL. Camera credentials never
  need to be exposed to Azure or to the Flutter client.
- Configured-camera rate evidence follows a stricter split: the Edge API returns
  the current JPEG to the Windows client, and the client uploads it to the Azure
  business API. The evidence token, metadata and Blob object are therefore
  created by one cloud authority and cannot reference an Edge PC `C:\` path.
- Azure and edge API instances must use the same JWT signing configuration so
  the logged-in user's token is accepted by the edge capture endpoints. They
  must also use the same encryption key so the edge API can decrypt the camera
  passwords stored in Azure SQL. A factory PC normally uses a Blob connection
  string in its protected service environment because Azure managed identity
  is available to App Service, not to that PC. Store Azure SQL/Blob/JWT secrets
  in environment variables or Key Vault, never in the distributed Windows app.

If Camera Test, Snapshot and both weighment screens all report connection
errors, check the camera service URL first. `Connection refused` for
`127.0.0.1:5001` means no Camera Edge API is listening on that client PC; use
the reachable LAN edge URL (for example `http://192.168.31.10:5000`) or start
the local edge API. Camera definitions are stored centrally in the database,
but RTSP decoding and capture are performed by the selected edge API, not by
the Flutter executable itself.

## Local/LAN and cloud build commands

### API

Cloud App Service deployment:

```powershell
.\tools\deploy-azure-api.ps1 `
  -ResourceGroup AuthenticFoodFarm `
  -AppName authenticfoodfarmApi
```

Local/LAN IIS package (keep the server's protected `web.config` and environment
variables when updating an existing installation):

```powershell
.\tools\build-api-publish.ps1 `
  -OutputDirectory .\artifacts\API_LAN_20261022
```

Fresh Camera Edge IIS site (includes the generated ASP.NET Core Module
`web.config`; secrets are still not included):

```powershell
.\tools\build-camera-edge-iis.ps1 `
  -OutputDirectory .\artifacts\CAMERA_EDGE_IIS_20261022
```

If `/api/health` returns an IIS `404.0` handled by `StaticFile`, the site is
missing this generated `web.config` (or its physical path points at the wrong
folder). Install the .NET 8 ASP.NET Core Hosting Bundle, set the application
pool to **No Managed Code**, point the IIS site at the folder containing both
`CaneFactory.API.dll` and `web.config`, then restart IIS.

On the Camera Edge PC, save the Azure SQL/JWT/encryption/Blob secrets as
machine-level environment variables without putting them in source files or
PowerShell history:

```powershell
# Run PowerShell as Administrator; values are requested with hidden input.
.\tools\configure-camera-edge-machine.ps1
iisreset
```

`CANE_CAMERA_CAPTURE_MODE=EDGE_PC` belongs on the Azure App Service. The
factory Camera Edge API must use `CANE_CAMERA_CAPTURE_MODE=SERVER`, because it
is the process that opens the RTSP streams and captures the images.

Direct/background test on the same Windows PC:

```powershell
.\tools\start-camera-edge.ps1 `
  -ApiDirectory C:\Balvant\API\CAMERA_EDGE_API_LAN `
  -Port 5001 `
  -ListenAddress 127.0.0.1
```

When trusted Windows clients on other factory PCs must use one shared Camera
Edge host, use `-ListenAddress 0.0.0.0`, allow inbound TCP 5001 in Windows
Firewall, and configure each client with `http://<static-edge-PC-IP>:5001`.
Never point an Internet client at this private HTTP endpoint or port-forward it
from the router; use a VPN plus HTTPS if remote edge access is ever required.

### Windows application

Cloud business API with camera capture on the same factory PC:

```powershell
.\tools\build-windows-client.ps1 `
  -ApiBaseUrl https://authenticfoodfarmapi-gwduddakdjgka6aq.centralindia-01.azurewebsites.net `
  -CameraEdgeApiBaseUrl http://127.0.0.1:5001 `
  -OutputDirectory .\artifacts\WINDOWS_CLOUD_20261022
```

Local/LAN server:

```powershell
.\tools\build-windows-client.ps1 `
  -ApiBaseUrl http://192.168.31.10:5000 `
  -OutputDirectory .\artifacts\WINDOWS_LAN_20261022
```

### Android application

Run these commands from `frontend\cane_factory_app`. The LAN build explicitly
supports the factory's HTTP IIS endpoint; use HTTPS whenever it is available.

```powershell
# Cloud
flutter build apk --release --obfuscate `
  --split-debug-info=build\symbols-cloud `
  --dart-define=API_BASE_URL=https://authenticfoodfarmapi-gwduddakdjgka6aq.centralindia-01.azurewebsites.net

# Local/LAN
flutter build apk --release --obfuscate `
  --split-debug-info=build\symbols-lan `
  --dart-define=API_BASE_URL=http://192.168.31.10:5000
```

The mobile client uses the selected business API. Camera live view and capture
remain a Windows/factory-edge responsibility and are not streamed through the
mobile client or Azure App Service.

### Scale Bridge

The same Scale Bridge code is used in both modes. Only `ScaleBridge.ApiBaseUrl`
changes; COM port, parser profile, device ID and local endpoint remain the same.

```powershell
# Cloud
.\tools\build-scale-bridge.ps1 `
  -ApiBaseUrl https://authenticfoodfarmapi-gwduddakdjgka6aq.centralindia-01.azurewebsites.net `
  -OutputDirectory .\artifacts\SCALE_BRIDGE_CLOUD_20261022

# Local/LAN
.\tools\build-scale-bridge.ps1 `
  -ApiBaseUrl http://192.168.31.10:5000 `
  -OutputDirectory .\artifacts\SCALE_BRIDGE_LAN_20261022
```

Set one strong key on both sides. In App Service set
`CANE_SCALE_AGENT_KEY=<same-key>`; on the Bridge set
`ScaleBridge:AgentKey=<same-key>` in protected configuration. When **Live weight
only on Digitizer PC** is enabled, the Bridge stops continuous cloud posting,
the factory Windows client reads `http://127.0.0.1:5158`, and only the weight
proof/save request is sent to the business API.

## Company logo in cloud mode

Do not use a Windows drive path for a cloud logo. Sign in as Developer, open
**Configuration > Company**, choose the logo file and upload it once. The logo
bytes are stored in the database and are then available to Azure report PDF,
Excel, A4, dot-matrix and invoice rendering. Local/LAN mode can use either this
portable upload or a server-local path.

## Azure SQL backup behavior

Azure SQL Database does not support the application's local
`BACKUP DATABASE ... TO DISK` job. Azure automatically manages full,
differential and transaction-log backups. The Configuration > Backup page shows
`Azure SQL Managed Backup` in cloud mode instead of reporting a failed local
folder backup.

For the current Basic database, inspect or set the supported 1-7 day point-in-time
retention policy with:

```powershell
az sql db str-policy show `
  -g AuthenticFoodFarm `
  -s authenticfoodfarmserver `
  -n AuthenticFoodFarmDatabase

az sql db str-policy set `
  -g AuthenticFoodFarm `
  -s authenticfoodfarmserver `
  -n AuthenticFoodFarmDatabase `
  --retention-days 7 `
  --diffbackup-hours 24
```

Local/LAN SQL Server continues to use the existing Backup folder, `.bak`, SQL
script and Task Scheduler workflow. Restoring an Azure backup is performed from
Azure SQL Database **Restore** / Point-in-time restore, not from the application.

