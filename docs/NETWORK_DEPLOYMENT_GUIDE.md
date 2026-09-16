# Factory LAN and Internet Deployment

The API runs **once** on the factory server.  Every Windows, Android, iOS, and
web client must point to that API.  `localhost` is only valid when the API and
the client are running on the *same* PC.

```
LAN desktop / mobile / web browser
              |
              v
      Factory server API + SQL Server
              |
              v
     serial indicator, cameras, evidence storage, printer
```

## 1. Factory LAN (Windows clients now)

Give the server a fixed LAN address, preferably a DHCP reservation.  The
examples below use `192.168.31.10`; replace it if the server's current LAN IP
is different.

On the server, start the published API so Kestrel listens on every LAN adapter:

```powershell
$env:ASPNETCORE_URLS = 'http://0.0.0.0:5000'
cd C:\API\AFFLLPCaneFactoryAPI202627
.\CaneFactory.API.exe
```

For a persistent deployment, set `ASPNETCORE_URLS` as a system environment
variable or configure the same value in the IIS/Kestrel deployment.  Do not
bind only to `localhost` or `127.0.0.1`.

Confirm on the server:

```powershell
netstat -ano | Select-String ':5000'
Invoke-WebRequest http://127.0.0.1:5000/api/health
```

`netstat` must show `0.0.0.0:5000` (or the server LAN IP), not only
`127.0.0.1:5000`.

Open the API port on the server's **Private** Windows Firewall profile from an
elevated PowerShell window:

```powershell
New-NetFirewallRule -DisplayName 'CaneFactory API LAN (TCP 5000)' `
  -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow -Profile Private
```

From another LAN PC, test before opening the Flutter app:

```powershell
Test-NetConnection 192.168.31.10 -Port 5000
Invoke-WebRequest http://192.168.31.10:5000/api/health
```

Both must succeed.  If they do not, fix the server binding, firewall, VLAN, or
IP address before rebuilding the client.

Build one Windows client package using the server LAN URL:

```powershell
cd C:\Balvant\Flutter\CaneFactorySystem\CaneFactorySystem
.\tools\build-windows-client.ps1 -ApiBaseUrl http://192.168.31.10:5000
```

Copy the **complete** `artifacts\windows-client` folder to each client PC. Do
not copy only `cane_factory_app.exe`. The build uses the server URL, so it no
longer tries to contact each client PC's `localhost`.

## 2. Internet access for Web and Mobile

Do not expose port 5000, SQL Server port 1433, camera RTSP ports, or the
weighbridge serial device directly to the Internet. Use a domain and HTTPS
reverse proxy on the factory server:

```
Internet client -> https://api.factory-example.com (443)
                         -> IIS reverse proxy / ASP.NET Core hosting
                         -> API on 127.0.0.1:5000
```

Required server/network work:

1. Obtain a stable public IP or DDNS and create `api.your-domain.com` DNS.
2. Install the ASP.NET Core 8 Hosting Bundle and IIS URL Rewrite/ARR, or host
   the API directly in IIS.
3. Bind a valid TLS certificate to the API site on port 443 (for example,
   Let's Encrypt). Mobile apps require HTTPS in production.
4. On the router, forward **only TCP 443** to the IIS reverse-proxy server.
   Do not forward 5000, 1433, 554, or camera/NVR ports.
5. Keep the SQL database, camera RTSP streams, evidence folder, printers, and
   serial digitizer on the factory LAN; the API accesses them server-side.
6. Add the web application origin to the API `Cors:AllowedOrigins`, for example:

```json
"Cors": {
  "AllowedOrigins": [
    "https://app.your-domain.com"
  ]
}
```

For a web release:

```powershell
cd frontend\cane_factory_app
flutter build web --release `
  --dart-define=API_BASE_URL=https://api.your-domain.com
```

For Android/iOS releases, use the same HTTPS `API_BASE_URL`. The Android main
manifest includes the required Internet permission. Do not ship a public app
that uses a raw `http://` public IP.

## 3. One URL for both LAN and Internet (recommended later)

Use `https://api.your-domain.com` in every production build. Configure
split-horizon DNS so, inside the factory LAN, that name resolves to the
server's private address, while public DNS resolves to the public address.
This avoids separate LAN and Internet app packages and keeps HTTPS enabled.

Until that DNS/TLS setup is complete, use the LAN Windows build in section 1.

## Security checklist

- Reserve the server LAN IP so it never changes.
- Use a strong, unique JWT secret and database password.
- Back up the evidence-image drive and SQL Server database.
- Restrict firewall rule TCP 5000 to `Private` only; remove it once IIS/HTTPS
  is the only entry point.
- Use HTTPS and a valid certificate for all Internet/mobile/web traffic.
