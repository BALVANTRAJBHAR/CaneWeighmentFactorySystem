# CaneFactory Scale Bridge

Run this program only on the Windows PC where the USB-to-serial converter and weighbridge indicator are physically connected. It reads that PC's COM port and sends only parsed weight/state to the central CaneFactory API over the LAN.

## Server setup

On the API/SQL Server PC, set a strong system environment variable named `CANE_SCALE_AGENT_KEY`, then restart the CaneFactory API/IIS application pool. Do not put this secret in a client application.

## Device setup

In CaneFactory **Weighing Device** configuration:

1. Edit the active device and select **Remote USB/COM PC (Scale Bridge)**.
2. Keep the same COM/baud/profile details for reference, activate it, then select Connect. The server will wait for the bridge instead of opening its own COM port.
3. Note the device ID shown in configuration.

## Bridge PC setup

Edit `appsettings.json`: API URL, same AgentKey, DeviceId, the local `COMx` port shown in Device Manager, and serial/parser settings.

## Recommended: install as a background Windows Service

This is the recommended production setup. Keep the complete published bridge folder in a permanent path such as `C:\WARRIOR SOFTECH\ScaleBridge`; do not use Downloads or Desktop. After `appsettings.json` is correct, open **PowerShell as Administrator** in that folder and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-service.ps1 -StartNow
```

The `CaneFactory Scale Bridge` service starts automatically when that USB/COM PC starts, continues even when the Flutter client is closed or nobody is logged in, and restarts after an unexpected serial/LAN failure. It has no visible window. Check it with `Get-Service CaneFactoryScaleBridge`; errors are recorded in **Event Viewer → Windows Logs → Application** under source `CaneFactory Scale Bridge`.

To stop/remove only the service (without deleting settings or the bridge folder), run PowerShell as Administrator and use `./uninstall-service.ps1`.

For a quick temporary diagnostic only, run `CaneFactory.ScaleBridge.exe` manually from this folder. Do not run the EXE manually while the Windows Service is running: only one process may open the same `COMx` port.

The supplied parser defaults match String Type 15 (STX `02`, sign position 2, weight at position 3 for 6 characters, LF `0A`).
