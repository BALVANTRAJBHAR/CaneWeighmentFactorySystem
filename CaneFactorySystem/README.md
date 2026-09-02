# CaneFactorySystem — Factory Cane Weighment + Payment + Loan + Farmer Management

Production factory management system. **All 14 phases delivered** (Architecture, Auth/RBAC,
Masters, Purchase/Grower/Vehicle/Variety/Rate, RS232 weighing, Gross/Tare weighment, camera
capture, printing engine, loans & recovery, payments & advice, SMS notifications, reporting
engine, farmer self-service portal, security/backup/health, final packaging).

## Technology Stack
| Layer | Technology |
|---|---|
| Frontend | Flutter (stable) — Windows Desktop, Android, iOS, Web |
| Backend | ASP.NET Core 8 Web API — Clean Architecture, REST + SignalR |
| Database | Microsoft SQL Server 2019 Express (Standard-upgrade compatible) |
| Security | JWT + refresh rotation, RBAC (570 granular permissions), AES-256-GCM secret encryption, audit log |

## Repository Layout
```
CaneFactorySystem/
├── backend/
│   ├── CaneFactory.sln
│   └── src/
│       ├── CaneFactory.Domain/          # Entities (no dependencies)
│       ├── CaneFactory.Application/     # DTOs, interfaces, business rules
│       ├── CaneFactory.Infrastructure/  # EF Core, SQL Server, parser engine, serial port, services
│       └── CaneFactory.API/             # Controllers, auth policies, SignalR hub, middleware
├── frontend/cane_factory_app/           # Flutter app (all platforms)
├── database/scripts/                    # SQL Server scripts (01 create DB+login, 02 schema)
└── docs/                                # All setup / build / integration guides
```

## Quick Start (local development, Windows)
1. Install prerequisites — see `docs/SETUP_GUIDE.md`.
2. Create the database: run `database/scripts/01_create_database.sql` then `02_schema_migration.sql` in SSMS
   (or let the API auto-migrate on first start).
3. Set environment variables (see `backend/.env.example`) — **never commit secrets**:
   - `CANE_CONNECTION_STRING`, `CANE_JWT_SECRET`, `CANE_ENCRYPTION_KEY`, `CANE_SEED_DEV_PASSWORD`
4. Run the API:
   ```
   cd backend/src/CaneFactory.API
   dotnet run --urls http://localhost:5000
   ```
   On first start the API migrates the schema and seeds roles, 570 permissions, masters,
   the String Type 15 indicator preset and the initial Developer account.
5. Run the Flutter app (after `flutter create . --platforms=windows,android,ios,web --project-name cane_factory_app` once):
   ```
   cd frontend/cane_factory_app
   flutter pub get
   flutter run -d windows --dart-define=API_BASE_URL=http://localhost:5000
   ```

## Initial Developer Account (no public registration exists)
| Username | Temporary password | Behaviour |
|---|---|---|
| `developer` | value of `CANE_SEED_DEV_PASSWORD` (default `ChangeMe@2026`) | **Forced password change on first login.** All permission-protected APIs are blocked until changed. |

After first login the Developer creates the first Admin, then other users. Deactivating a user
instantly revokes refresh tokens and blocks API access while preserving all historical records.

## Documentation Index
| Doc | Content |
|---|---|
| docs/SETUP_GUIDE.md | Flutter/Dart/Android Studio/JDK/Visual Studio/SQL Server 2019 setup, DB creation, connection string, env vars, secrets, production deployment |
| docs/FLUTTER_BUILD_GUIDE.md | Windows / Android (APK+AAB with R8 obfuscation) / iOS / Web builds |
| docs/API_DOCUMENTATION.md | Every endpoint with permissions |
| docs/RS232_DIGITIZER_GUIDE.md | RS232/USB wiring, COM setup, String Type 15, parser profiles, testing |
| docs/CAMERA_GUIDE.md | Hikvision/CP Plus/Dahua/Uniview/ONVIF/RTSP configuration |
| docs/PRINTER_GUIDE.md | TVS MSP 270 dot matrix + A4 setup, auto print |
| docs/SMS_GUIDE.md | Generic DLT-compatible HTTP provider configuration |
| docs/BACKUP_GUIDE.md | Full/differential/log backups, verification, restore test, Task Scheduler XML |
| docs/SECURITY_CHECKLIST.md | OWASP-aligned checklist with implementation status |
| docs/ROLE_PERMISSION_MATRIX.md | Role → permission matrix |
| docs/SEQUENCE_ARCHITECTURE.md | Continuous business-serial number design (starts at 1, gap-free) |
| docs/ER_DIAGRAM.md | Entity-relationship diagram (text) |
| docs/TEST_REPORT.md | Phase-by-phase test evidence + known issues |

## Phase Status
| Phase | Scope | Status |
|---|---|---|
| 1 | Architecture + DB + Auth + RBAC | ✅ Complete, tested |
| 2 | Masters + validations + duplicate checks | ✅ Complete, tested |
| 3 | Grower + Vehicle + Variety + Rate + Purchase | ✅ Complete, tested |
| 4 | RS232/USB weighing + configurable parser engine + String Type 15 + test screen | ✅ Complete, tested (simulator + parser verified; physical RS232 requires Windows hardware) |
| 5 | Unified Gross/Tare form + live weight + min-weight + cutting/tax + sound/TTS + camera/print hooks | ✅ Complete, tested |
| 6 | Camera capture (Hikvision/CP Plus/Dahua/Uniview/ONVIF/RTSP), stored evidence images | ✅ Complete, tested |
| 7 | Centralized printing engine (QuestPDF A4 + SkiaSharp/HarfBuzz Hindi ESC/P dot-matrix) | ✅ Complete, tested |
| 8 | Loans & Recovery — interest-free, manual recovery, cancel/reverse | ✅ Complete, tested |
| 9 | Payments & Advice — Cash/Bank/Mobile UPI, cash evidence photo, auto loan deduction (no online gateway) | ✅ Complete, tested |
| 10 | SMS Notifications — generic HTTP provider, async queue, Hindi default templates, delivery log | ✅ Complete, tested |
| 11 | Reports — Purchase/Payment/Loan/Daily Collection, all filterable + totals + PDF/Excel export | ✅ Complete, tested |
| 12 | Farmer self-service portal — dashboard + statement, strictly own-data scoped | ✅ Complete, tested |
| 13 | Security hardening (security audit log), public health probe, SQL Server backup policy/scripts | ✅ Complete, tested |
| 14 | Final packaging — full source `.zip`, DB scripts, all guides | ✅ Complete |
