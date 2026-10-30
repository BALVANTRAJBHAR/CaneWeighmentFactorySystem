[CmdletBinding()]
param(
    [string]$ApiDirectory = (Join-Path $PSScriptRoot '..\artifacts\CAMERA_EDGE_API_20261022'),
    [ValidateRange(1024, 65535)]
    [int]$Port = 5001,
    [ValidateSet('127.0.0.1', '0.0.0.0')]
    [string]$ListenAddress = '127.0.0.1'
)

$ErrorActionPreference = 'Stop'
$apiRoot = (Resolve-Path -LiteralPath $ApiDirectory).Path
$apiDll = Join-Path $apiRoot 'CaneFactory.API.dll'
if (-not (Test-Path -LiteralPath $apiDll)) {
    throw "Camera Edge API was not found: $apiDll"
}

function Import-ServerSetting([string]$name, [switch]$Required) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = [Environment]::GetEnvironmentVariable($name, [EnvironmentVariableTarget]::Machine)
    }
    if ($Required -and [string]::IsNullOrWhiteSpace($value)) {
        throw "Missing protected machine setting $name. Configure the Camera Edge PC before starting it."
    }
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        [Environment]::SetEnvironmentVariable($name, $value, [EnvironmentVariableTarget]::Process)
    }
}

Import-ServerSetting 'CANE_CONNECTION_STRING' -Required
Import-ServerSetting 'CANE_JWT_SECRET' -Required
Import-ServerSetting 'CANE_ENCRYPTION_KEY' -Required
Import-ServerSetting 'CANE_SEED_DEV_PASSWORD'
Import-ServerSetting 'CANE_AZURE_BLOB_CONNECTION_STRING'
Import-ServerSetting 'CANE_AZURE_BLOB_SERVICE_URI'
Import-ServerSetting 'CANE_EVIDENCE_UPLOAD_QUEUE_PATH'
Import-ServerSetting 'CANE_IMAGE_STORAGE_MODE'

if ([string]::IsNullOrWhiteSpace($env:CANE_AZURE_BLOB_CONNECTION_STRING) -and
    [string]::IsNullOrWhiteSpace($env:CANE_AZURE_BLOB_SERVICE_URI)) {
    throw 'Set CANE_AZURE_BLOB_CONNECTION_STRING on the Camera Edge PC (recommended), or configure a supported Azure identity plus CANE_AZURE_BLOB_SERVICE_URI.'
}

$healthUrl = "http://127.0.0.1:$Port/api/health"
try {
    $existing = Invoke-WebRequest -UseBasicParsing -Uri $healthUrl -TimeoutSec 3
    if ($existing.StatusCode -eq 200) {
        Write-Host "Camera Edge API is already healthy at http://127.0.0.1:$Port"
        exit 0
    }
} catch { }

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = "http://${ListenAddress}:$Port"
$env:CANE_DB_PROVIDER = 'SqlServer'
$env:CANE_CAMERA_CAPTURE_MODE = 'SERVER'
$env:CANE_CAMERA_SIMULATOR = 'false'
$env:CANE_IMAGE_STORAGE_MODE = 'AZURE_BLOB'

$stdout = Join-Path $apiRoot 'camera-edge.stdout.log'
$stderr = Join-Path $apiRoot 'camera-edge.stderr.log'
$process = Start-Process -FilePath 'dotnet' -ArgumentList 'CaneFactory.API.dll' `
    -WorkingDirectory $apiRoot -WindowStyle Hidden `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru

for ($attempt = 1; $attempt -le 15; $attempt++) {
    Start-Sleep -Seconds 2
    if ($process.HasExited) {
        Write-Error "Camera Edge API exited with code $($process.ExitCode). See $stderr"
        exit $process.ExitCode
    }
    try {
        $health = Invoke-WebRequest -UseBasicParsing -Uri $healthUrl -TimeoutSec 4
        if ($health.StatusCode -eq 200) {
            Write-Host "Camera Edge API started successfully on http://${ListenAddress}:$Port (PID $($process.Id))."
            Write-Host "Logs: $stdout and $stderr"
            exit 0
        }
    } catch { }
}

throw "Camera Edge API did not become healthy. See $stdout and $stderr"
