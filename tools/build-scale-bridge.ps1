[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\scale-bridge')
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $projectRoot 'tools\CaneFactory.ScaleBridge\CaneFactory.ScaleBridge.csproj'
# PowerShell 5.1 runs on .NET Framework, where Path.GetFullPath has no
# two-argument overload. Resolve relative output from the caller's folder,
# while still accepting an absolute output path.
$outputCandidate = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path (Get-Location).Path $OutputDirectory
}
$output = [System.IO.Path]::GetFullPath($outputCandidate)

if ((Test-Path -LiteralPath $output) -and
    (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
    throw "Output directory is not empty: $output. Choose a new empty directory so a working Bridge package is not overwritten."
}

dotnet publish $project -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Scale Bridge publish failed.' }

Write-Host "Scale Bridge package created: $output"
Write-Host 'Copy the COMPLETE folder to the USB/COM (weighbridge) PC.'
Write-Host 'Edit appsettings.json there before running or installing the service.'
