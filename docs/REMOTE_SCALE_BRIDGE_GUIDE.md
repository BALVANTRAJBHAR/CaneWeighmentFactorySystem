# Remote USB/COM Scale Bridge

Use this mode when the CaneFactory API/SQL Server and the weighing indicator's USB-to-serial converter are on different LAN PCs. A Windows COM port belongs only to the PC to which the converter is physically attached; it cannot be opened by the API process on another PC.

## 1. Server PC

Deploy `backend/publish-remote-scale-bridge-final` over the API deployment, then restart the API process or IIS application pool.

Create a strong secret once in an elevated PowerShell window. Copy the displayed value privately; it is required in the bridge configuration.

```powershell
$key = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 40 | ForEach-Object { [char]$_ })
[Environment]::SetEnvironmentVariable('CANE_SCALE_AGENT_KEY', $key, 'Machine')
$key
```

Restart the API/IIS application pool after setting the variable. Ensure the bridge PC can reach the API URL (for example `http://192.168.31.10:5000/api/ping`).

## 2. Weighing Device configuration

In **Weighing Device**, edit the active indicator configuration and set connection type to **Remote USB/COM PC (Scale Bridge)**. Save/activate it, then press **Connect**. This only sets the desired state; the server will deliberately not try to open `COMx`.

The first/default weighing device normally has ID `1`. If there are multiple device configurations, use the active device ID returned by `GET /api/devices` while signed in, or ask the administrator to identify it before starting the bridge.

## 3. PC with the USB-to-serial converter

Copy the complete `artifacts/scale-bridge-final` folder to this PC. In Device Manager, confirm the converter's assigned `COMx` port. Edit its `appsettings.json`:

```json
"ApiBaseUrl": "http://192.168.31.10:5000",
"AgentKey": "the exact CANE_SCALE_AGENT_KEY value from the server",
"DeviceId": 1,
"ComPort": "COM3"
```

Retain or match the actual baud rate, parity, data bits, stop bits and string profile parser positions. The supplied default is String Type 15.

For production, install it as the automatic background **CaneFactory Scale Bridge** Windows Service. Keep the complete bridge publish folder in a permanent location such as `C:\WARRIOR SOFTECH\ScaleBridge`, then open PowerShell **as Administrator** in that folder and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-service.ps1 -StartNow
```

It opens only this PC's serial port, posts readings to the server, reconnects after serial/LAN failures, starts with Windows, and has no visible window. Do not run the EXE manually while the service is running, because only one process can own the same `COMx` port.

## 4. Verify

- Bridge console: no “server rejected” message and reports a local COM connection.
- Weighment screen on any LAN client: indicator shows `Connected`, `READING`, and changing live weight.
- Unplug/replug USB converter: bridge retries; it does not cause the API server to attempt a COM connection.

Do not share `appsettings.json` with the secret or commit a real key into source control.
