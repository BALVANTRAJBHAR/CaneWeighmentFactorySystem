[CmdletBinding()]
param(
    [string]$BlobServiceUri = 'https://authenticfoodfarmstorage.blob.core.windows.net'
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run PowerShell as Administrator so the protected settings can be stored at Machine scope.'
}

function Read-ProtectedValue([string]$prompt) {
    $secure = Read-Host $prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

$sql = Read-ProtectedValue 'Paste the Azure SQL connection string'
$jwt = Read-ProtectedValue 'Paste the exact CANE_JWT_SECRET used by the Azure API'
$encryption = Read-ProtectedValue 'Paste the exact CANE_ENCRYPTION_KEY used by the Azure API'
$blob = Read-ProtectedValue 'Paste Storage Account > Access keys > Connection string'

$protected = @{
    CANE_CONNECTION_STRING = $sql
    CANE_JWT_SECRET = $jwt
    CANE_ENCRYPTION_KEY = $encryption
    CANE_AZURE_BLOB_CONNECTION_STRING = $blob
}
foreach ($entry in $protected.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace($entry.Value)) {
        throw "$($entry.Key) cannot be empty. No settings were written."
    }
}

$settings = $protected + @{
    CANE_DB_PROVIDER = 'SqlServer'
    CANE_IMAGE_STORAGE_MODE = 'AZURE_BLOB'
    CANE_AZURE_BLOB_SERVICE_URI = $BlobServiceUri.TrimEnd('/')
    CANE_CAMERA_CAPTURE_MODE = 'SERVER'
    CANE_CAMERA_SIMULATOR = 'false'
    ASPNETCORE_ENVIRONMENT = 'Production'
}

foreach ($entry in $settings.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable(
        $entry.Key,
        $entry.Value,
        [EnvironmentVariableTarget]::Machine)
}

$sql = $jwt = $encryption = $blob = $null
Write-Host 'Camera Edge machine settings saved successfully.'
Write-Host 'Important: EDGE_PC belongs only on the Azure API. This Camera Edge API is now SERVER mode.'
Write-Host 'Restart IIS/WAS (iisreset) or restart the PC before testing /api/health.'
