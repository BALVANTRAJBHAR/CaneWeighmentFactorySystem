[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https?://[^/]+(?::\d+)?/?$')]
    [string]$ApiBaseUrl,

    # In cloud mode the business API is hosted in Azure, while camera live
    # view/capture stays on the factory PC. Point this at that PC's edge API.
    # In local/LAN mode omit it and the main API URL is reused.
    [ValidatePattern('^$|^https?://[^/]+(?::\d+)?/?$')]
    [string]$CameraEdgeApiBaseUrl = '',

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\windows-client')
)

$ErrorActionPreference = 'Stop'

# The URL is compiled into a Flutter desktop build via --dart-define.  This
# prevents every client PC from accidentally using its own localhost address.
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$flutterProject = Join-Path $projectRoot 'frontend\cane_factory_app'
$releaseFolder = Join-Path $flutterProject 'build\windows\x64\runner\Release'

Push-Location $flutterProject
try {
    flutter pub get
    if ($LASTEXITCODE -ne 0) { throw 'flutter pub get failed.' }

    $dartDefines = @("--dart-define=API_BASE_URL=$ApiBaseUrl")
    if (![string]::IsNullOrWhiteSpace($CameraEdgeApiBaseUrl)) {
        $dartDefines += "--dart-define=CAMERA_EDGE_API_BASE_URL=$CameraEdgeApiBaseUrl"
    }
    flutter build windows --release @dartDefines
    if ($LASTEXITCODE -ne 0) { throw 'Windows release build failed.' }
}
finally {
    Pop-Location
}

if ((Test-Path -LiteralPath $OutputDirectory) -and
    (Get-ChildItem -LiteralPath $OutputDirectory -Force | Select-Object -First 1)) {
    throw "Output directory is not empty: $OutputDirectory. Choose a new empty directory so an existing client package is not overwritten."
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $releaseFolder -Force |
    Copy-Item -Destination $OutputDirectory -Recurse -Force

Write-Host "Client package created: $OutputDirectory"
Write-Host "Configured API endpoint: $ApiBaseUrl"
if (![string]::IsNullOrWhiteSpace($CameraEdgeApiBaseUrl)) {
    Write-Host "Configured factory camera edge endpoint: $CameraEdgeApiBaseUrl"
}
else {
    Write-Host 'Camera edge endpoint: main API endpoint (local/LAN mode)'
}
Write-Host 'Copy the complete folder (not only the .exe) to every client PC.'
