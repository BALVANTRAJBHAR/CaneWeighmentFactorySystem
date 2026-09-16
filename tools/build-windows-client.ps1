[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https?://[^/]+(?::\d+)?/?$')]
    [string]$ApiBaseUrl,

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

    flutter build windows --release "--dart-define=API_BASE_URL=$ApiBaseUrl"
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
Write-Host 'Copy the complete folder (not only the .exe) to every client PC.'
