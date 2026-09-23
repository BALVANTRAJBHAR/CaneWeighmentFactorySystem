[CmdletBinding()]
param(
    [switch]$StartNow
)

$ErrorActionPreference = 'Stop'
$serviceName = 'CaneFactoryScaleBridge'
$displayName = 'CaneFactory Scale Bridge'
$exePath = Join-Path $PSScriptRoot 'CaneFactory.ScaleBridge.exe'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run PowerShell as Administrator, then run .\install-service.ps1 -StartNow.'
}
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "CaneFactory.ScaleBridge.exe was not found in $PSScriptRoot. Keep this script beside the complete Scale Bridge publish folder."
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(20))
    }
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $serviceName -DisplayName $displayName -BinaryPathName ('"{0}"' -f $exePath) `
    -Description 'Reads the locally attached weighbridge COM port and securely sends live readings to CaneFactory API.' `
    -StartupType Automatic

# Restart the bridge if USB, LAN, or serial-driver failures terminate the process.
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
& sc.exe failureflag $serviceName 1 | Out-Null

if ($StartNow) { Start-Service -Name $serviceName }
Write-Host "Installed '$displayName' as an automatic background Windows Service."
Write-Host "Check status: Get-Service $serviceName"
Write-Host "Event Viewer: Windows Logs > Application, Source '$displayName'"
