[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\CAMERA_EDGE_IIS'),
    [string]$ZipPath
)

$ErrorActionPreference = 'Stop'
$builder = Join-Path $PSScriptRoot 'build-api-publish.ps1'

& $builder -OutputDirectory $OutputDirectory -FreshIis
if ($LASTEXITCODE -ne 0) {
    throw 'Camera Edge IIS publish failed.'
}

if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    $ZipPath = "$OutputDirectory.zip"
}
if (Test-Path -LiteralPath $ZipPath) {
    throw "ZIP already exists: $ZipPath. Choose a new path."
}

$zipParent = Split-Path -Parent $ZipPath
if (![string]::IsNullOrWhiteSpace($zipParent)) {
    New-Item -ItemType Directory -Path $zipParent -Force | Out-Null
}
Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $ZipPath -CompressionLevel Optimal

Write-Host "Camera Edge IIS folder: $OutputDirectory"
Write-Host "Camera Edge IIS ZIP: $ZipPath"
Write-Host 'Use this package for a NEW IIS site. It includes web.config and FFmpeg.'
