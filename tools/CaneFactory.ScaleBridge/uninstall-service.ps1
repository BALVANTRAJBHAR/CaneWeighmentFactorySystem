[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'CaneFactoryScaleBridge'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run PowerShell as Administrator, then run .\uninstall-service.ps1.'
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$serviceName' is not installed."
    return
}
if ($existing.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force }
& sc.exe delete $serviceName | Out-Null
Write-Host "Service '$serviceName' removed. The Scale Bridge folder and appsettings.json were kept."
