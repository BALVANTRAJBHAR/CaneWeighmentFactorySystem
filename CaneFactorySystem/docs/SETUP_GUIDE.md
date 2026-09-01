# SETUP GUIDE — CaneFactorySystem (Phases 1–5)

Complete environment setup for development and production.

---
## 1. Prerequisites

### 1.1 .NET SDK 8.0 (backend)
- Download: https://dotnet.microsoft.com/download/dotnet/8.0 → **SDK x64 for Windows**
- Verify: `dotnet --version` → `8.0.x`

### 1.2 Flutter SDK (stable channel)
- Download: https://docs.flutter.dev/get-started/install/windows
- Extract to `C:\flutter` (no spaces in path), add `C:\flutter\bin` to PATH.
- Verify: `flutter --version` (Dart is bundled with Flutter — no separate Dart install needed)
- Run `flutter doctor` and resolve every item.

### 1.3 Android Studio + JDK (Android builds)
- Install Android Studio: https://developer.android.com/studio
- In SDK Manager install: Android SDK Platform 34+, Build-Tools, Platform-Tools, Command-line Tools.
- JDK 17 is bundled with recent Android Studio; otherwise install Temurin 17: https://adoptium.net
- Set `JAVA_HOME` to the JDK 17 path.
- Accept licenses: `flutter doctor --android-licenses`

### 1.4 Visual Studio 2022 (Windows desktop builds)
- Install **Visual Studio 2022 Community** with workload **"Desktop development with C++"**
  (required by `flutter build windows`). For backend development also add **"ASP.NET and web development"**.

### 1.5 Xcode (iOS builds — macOS only)
- Xcode 15+, CocoaPods (`sudo gem install cocoapods`). iOS builds can only be produced on macOS.

### 1.6 SQL Server 2019 Express
1. Download: https://www.microsoft.com/en-us/download/details.aspx?id=101064
2. Choose **Basic** install (instance name defaults to `SQLEXPRESS`).
3. Install **SSMS** (SQL Server Management Studio) to run scripts.
4. Enable TCP/IP if API runs on another machine: SQL Server Configuration Manager →
   SQL Server Network Configuration → Protocols for SQLEXPRESS → TCP/IP = Enabled → restart service.
5. Schema/indexes are Standard-edition compatible — future upgrade requires no schema change.

---
## 2. Database Creation
1. Open SSMS → connect to `localhost\SQLEXPRESS`.
2. Edit `database/scripts/01_create_database.sql` — replace `<STRONG_DB_PASSWORD>`.
3. Run `01_create_database.sql` (creates DB + least-privilege login `canefactory_app`).
4. Run `database/scripts/02_schema_migration.sql` (idempotent, generated from EF Core migrations)
   — OR skip it and let the API apply migrations automatically at first start.
5. Seed data (7 roles, 570 permissions, vehicle/variety/item/payment-mode masters, season 2026-27,
   String Type 15 device preset, weight rules, sound messages, developer account) is inserted
   automatically and idempotently by the API at startup.

---
## 3. Connection String + Environment Variables (Development Secrets)

**Never hard-code secrets. Never commit them to Git.** Copy `backend/.env.example` values into
real environment variables (System Properties → Environment Variables, or a launch profile):

| Variable | Example | Purpose |
|---|---|---|
| `CANE_DB_PROVIDER` | `SqlServer` | `SqlServer` (production) or `Sqlite` (dev containers only) |
| `CANE_CONNECTION_STRING` | `Server=localhost\SQLEXPRESS;Database=CaneFactoryDb;User Id=canefactory_app;Password=***;TrustServerCertificate=True;Encrypt=True` | EF Core connection |
| `CANE_JWT_SECRET` | 64+ random chars | JWT signing key |
| `CANE_ENCRYPTION_KEY` | 64+ random chars | AES-256-GCM key for stored secrets (SMS/Razorpay/camera credentials, Aadhaar) |
| `CANE_SEED_DEV_PASSWORD` | temporary password | Initial developer account (forced change at first login) |
| `ASPNETCORE_URLS` | `http://localhost:5000` | API listen address |

Generate random secrets (PowerShell):
```powershell
-join ((1..64) | ForEach-Object { '{0:x}' -f (Get-Random -Max 16) })
```

---
## 4. API Configuration & Run
```powershell
cd backend
dotnet restore
dotnet build            # must report 0 errors
cd src\CaneFactory.API
dotnet run --urls http://localhost:5000
```
- Swagger UI: http://localhost:5000/swagger
- Health ping: http://localhost:5000/api/ping
- SignalR live-weight hub: `/hubs/weight` (JWT via `access_token` query param)
- CORS allowlist: `appsettings.json → Cors:AllowedOrigins` (add your Flutter-web origin)

---
## 5. Flutter App — first run
The repo ships the `lib/` source + `pubspec.yaml`. Generate platform folders once:
```powershell
cd frontend\cane_factory_app
flutter create . --platforms=windows,android,ios,web --project-name cane_factory_app
flutter pub get
flutter analyze          # must be clean
flutter run -d windows --dart-define=API_BASE_URL=http://localhost:5000
```
`API_BASE_URL` is injected at build time — nothing is hard-coded.
For Android emulator use `http://10.0.2.2:5000`; for LAN devices use the factory server IP.

Windows-only note (`flutter_secure_storage`): tokens are stored via Windows Credential Manager.
On Web, secure storage uses WebCrypto — use HTTPS in production.

---
## 6. Production Deployment (factory server, offline-capable)
1. **API as Windows Service** on the factory LAN server:
   ```powershell
   dotnet publish backend/src/CaneFactory.API -c Release -o C:\CaneFactory\api
   sc.exe create CaneFactoryApi binPath= "C:\CaneFactory\api\CaneFactory.API.exe" start= auto
   ```
   Set environment variables machine-wide before starting the service.
2. **HTTPS/TLS 1.2+**: bind a certificate (self-signed for LAN or company CA):
   `ASPNETCORE_URLS=https://0.0.0.0:5001` + `ASPNETCORE_Kestrel__Certificates__Default__Path/Password`.
   HSTS is enabled automatically outside Development.
3. **Windows weighbridge app**: `flutter build windows --release --dart-define=API_BASE_URL=https://factory-server:5001`,
   copy `build\windows\x64\runner\Release\` to operator PCs.
4. **Offline-first**: Gross/Tare/live weight/printing/local DB all run on the factory LAN with no
   internet. Only SMS, Razorpay and remote web/mobile access require internet.
5. **Storage root**: default `D:\CanePaymentData\` (configurable in System Settings). Images are stored
   on disk with metadata in SQL — never as database BLOBs.
6. Configure backups per `docs/BACKUP_GUIDE.md` and review `docs/SECURITY_CHECKLIST.md` before go-live.

---
## 7. Hardware Setup Pointers
- **RS232 digitizer / String Type 15** → `docs/RS232_DIGITIZER_GUIDE.md`
- **Printer (TVS MSP 270 / A4)** → `docs/PRINTER_GUIDE.md`
- **IP cameras** → `docs/CAMERA_GUIDE.md`
- **SMS provider** → `docs/SMS_GUIDE.md`
- **RazorpayX** → `docs/RAZORPAY_GUIDE.md`
