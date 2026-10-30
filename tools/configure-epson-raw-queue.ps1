# Run in an elevated Windows PowerShell on the PC to which the LX-310 is attached.
# Creates a separate RAW-compatible queue on the SAME physical USB/LAN port.
# The existing Epson V4 printer and its Windows test-page setup are untouched.
param(
    [string]$SourcePrinterName = 'EPSON LX-310',
    [string]$RawPrinterName = '',
    [switch]$InspectOnly
)

$ErrorActionPreference = 'Stop'
Import-Module PrintManagement -ErrorAction Stop

$source = Get-Printer -Name $SourcePrinterName -ErrorAction Stop
if (-not $RawPrinterName) { $RawPrinterName = "$SourcePrinterName RAW" }
$sourcePort = $source.PortName
Write-Host "Source: $($source.Name)"
Write-Host "Driver: $($source.DriverName)"
Write-Host "Port:   $sourcePort"
Write-Host "RAW queue: $RawPrinterName"

$existing = Get-Printer -Name $RawPrinterName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.PortName -ne $sourcePort) {
        throw "Existing RAW queue uses port '$($existing.PortName)', but source uses '$sourcePort'. Correct the port manually; no printer was changed."
    }
    Write-Host "RAW queue already exists on $sourcePort with driver '$($existing.DriverName)'."
    if ($existing.DriverName -ne 'Generic / Text Only') {
        throw "RAW queue driver is '$($existing.DriverName)'. It must be 'Generic / Text Only'. No printer was changed."
    }
    Write-Host "Print processor: $($existing.PrintProcessor)"
    exit 0
}

if ($InspectOnly) { exit 0 }

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Open PowerShell as Administrator on the printer PC and run this script again.'
}

$driver = Get-PrinterDriver -Name 'Generic / Text Only' -ErrorAction SilentlyContinue
if (-not $driver) {
    try {
        Add-PrinterDriver -Name 'Generic / Text Only' -ErrorAction Stop
    } catch {
        throw "Windows could not install its Generic / Text Only driver: $($_.Exception.Message). Install that built-in driver in Windows printer settings, then rerun this script."
    }
}

Add-Printer -Name $RawPrinterName -DriverName 'Generic / Text Only' -PortName $sourcePort -ErrorAction Stop
$created = Get-Printer -Name $RawPrinterName -ErrorAction Stop
if ($created.PortName -ne $sourcePort -or $created.DriverName -ne 'Generic / Text Only') {
    throw 'The RAW queue was created but its port or driver does not match the requested configuration.'
}
if ($created.PrintProcessor -ne 'winprint') {
    Set-Printer -Name $RawPrinterName -PrintProcessor 'winprint' -ErrorAction Stop
}

Write-Host "Created '$RawPrinterName' on $sourcePort."
Write-Host 'In Cane Factory > Configuration > Print, select this RAW queue and save.'
Write-Host 'Send Print Test first and confirm paper output before production printing.'
