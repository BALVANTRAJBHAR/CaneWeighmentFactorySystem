# PRD — CaneFactorySystem (Factory Cane Weighment + Payment + Loan + Farmer System)

## Original Problem Statement (summary)
Production-ready factory management system: cane weighment (RS232 digitizer), payments, loans,
farmer management, cameras, SMS, RazorpayX, reporting. **Mandatory stack (user-confirmed):
Flutter (Windows/Android/iOS/Web) + ASP.NET Core Web API (Clean Architecture) + SQL Server 2019
Express.** Do NOT replace with React/FastAPI/MongoDB. Deliverable = downloadable source package +
full setup documentation. 14 phases; user approved delivering **Phases 1–5 first**.

## User Choices (Q&A, June 2026)
1. Delivery mode: **Full source-code package generation** (no live preview; build/run locally).
2. Scope: **Phases 1–5** (architecture+auth+RBAC, masters+validation, grower/vehicle/variety/rate/purchase, RS232 parser engine + String Type 15 preset + test screen, unified Gross/Tare form).
3. SMS: **generic configurable DLT-compatible HTTP provider** (not hard-coded), encrypted secrets.
4. Razorpay: **config-driven RazorpayX**, keys entered later in Developer Dashboard, encrypted.
5. Language: **English default, Hindi switchable** (TTS default Hindi).
Plus 26 additional hard requirements (unified single weighment form, sequence numbers from 1 without IDENTITY, no signup, forced first-login password change, offline factory LAN operation, camera vendor abstraction, etc.).

## Architecture
- `/app/CaneFactorySystem/backend` — .NET 8 solution: Domain / Application / Infrastructure / API
  (63 C# files). JWT + refresh rotation, permission policies (570 perms), AES-256-GCM SecretProtector,
  transactional NumberSequence generator, configurable parser engine (FixedPosition/Delimiter/Regex/
  KeyValue/Json), System.IO.Ports serial reader + simulator, SignalR /hubs/weight, audit log,
  vendor-abstracted camera capture (ISAPI/ONVIF/RTSP/SIMULATOR providers, Infrastructure/Camera/),
  centralized Print Engine (A4 QuestPDF + DotMatrix SkiaSharp/HarfBuzz ESC-P, Infrastructure/Printing/).
- `/app/CaneFactorySystem/frontend/cane_factory_app` — Flutter app (24 Dart files): login/forced
  change/forgot-OTP, role-aware shell, dashboard+role-aware health, generic master CRUD engine,
  grower master+search, unified GROSS/TARE weighment screen with live weight + TTS sound state
  machine + camera panel + captured-evidence thumbnails + Auto Print/manual Reprint, developer
  device/parser/simulator screens, config hub (weight rules/sound/cameras+snapshot preview/
  print+test-page preview/SMS/Razorpay/company), users/roles, audit, role-specific user guide,
  light/dark + 6 theme colors persisted.
- `/app/CaneFactorySystem/database/scripts` — 01 create DB + least-privilege login, 02 idempotent
  EF-generated SQL Server schema.
- `/app/CaneFactorySystem/docs` — 14 guides (setup, flutter builds, API, RS232, camera, printer,
  SMS, Razorpay, backup, security checklist, role matrix, sequences, ER diagram, test report).
- Zip: `/app/CaneFactorySystem_Phase1-10.zip`.

## Dev/Test environment notes (container)
- .NET 8 SDK at `/root/.dotnet` (reinstall if pod restarts: download the arm64/x64 tarball
  matching `uname -m` from `https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.404/...` and
  extract to `/root/.dotnet` — the official `dotnet-install.sh` script silently no-ops in this
  container, direct tarball download works). `dotnet tool install --global dotnet-ef --version
  8.0.11` for migrations (installs to `/root/.dotnet/tools`).
- API tested in-container with documented `Sqlite` dev provider on a FREE port (8001 is occupied
  by an unrelated supervisor-managed placeholder backend — use 8099 or similar) (start command in
  /app/memory/test_credentials.md). Production = SqlServer provider.
- Flutter cannot build here (no Flutter SDK) — user builds locally per docs.
- Platform supervisor FastAPI backend intentionally stopped (project doesn't use it).
- KNOWN CONTAINER-ONLY GOTCHA: EF Core's SQLite provider throws `System.NotSupportedException`
  ("cannot apply aggregate operator Sum") if `SumAsync(x => x.SomeDecimalProperty)` is called
  directly on an `IQueryable` — this is a SQLite-provider translation limitation, NOT a data bug,
  and does NOT happen on SQL Server. Fix used everywhere in this codebase: `(await q.Select(x =>
  x.Prop).ToListAsync()).Sum()` (fetch then sum client-side). Apply this pattern to any NEW
  decimal-aggregate query.

## What's been implemented (2026-06 / session 1)
- Phases 1–5 complete. Backend built with 0 errors/0 warnings; EF migration generated.
- Testing agent iteration_1: **53/53 backend tests passed** (auth, forced password change gate,
  lockout, refresh rotation+reuse detection, RBAC 401/403 matrix, masters duplicates, grower
  codes 101/1, Aadhaar block+masking, rate overlap, parser frames, min-weight, gross/tare exact
  2dp calculations, lock/cancel, audit, encrypted config secrets never returned).

## What's been implemented (2026-09 / session 2 — Phase 6: Camera capture)
- Vendor-abstracted `ICameraCaptureProvider` with 4 implementations (Infrastructure/Camera/):
  ISAPI (HTTP Basic/Digest snapshot), ONVIF (WS-Security PasswordDigest `GetSnapshotUri` SOAP +
  HTTP GET, per-camera `OnvifSettings` JSON override for mediaServiceUrl/profileToken), RTSP
  (ffmpeg single-frame extraction, requires ffmpeg on PATH), SIMULATOR (fixed placeholder JPEG,
  `CANE_CAMERA_SIMULATOR=true` — used for this container/demo without physical cameras, NEVER in
  production).
- `CameraCaptureService` orchestrates: respects `CameraSystemEnabled`/`ImageCaptureEnabled`
  system settings + per-camera `CaptureEnabled`; best-effort per camera (one failure never blocks
  others or the weighment); saves JPEG to `{ImageStorageRoot}\<Season>\Images\YYYY\MM\DD\PUR-<id>\
  <STAGE>-CAM<NN>-<seq>.jpg`, computes SHA256, inserts `PurchaseImage` row (never a DB BLOB).
- `WeighmentController` fires capture as a fire-and-forget background task (own DI scope via
  `IServiceScopeFactory`) after Gross/Tare save — response includes `captureQueued: true` and is
  never slowed down by camera network calls.
- New endpoints: `GET /api/purchases/{id}/images` (list, needs `Image.View`), `POST
  /api/purchases/{id}/images/capture` (manual re-capture, needs `Image.Create`), `GET
  /api/images/{id}/file` (serves JPEG bytes, needs `Image.View`), `GET
  /api/config/cameras/{id}/snapshot` (live preview, needs `Camera.Configure`).
- Flutter: Weighment screen shows captured evidence thumbnails per purchase (polls list 2s after
  save, `Image.memory` via authenticated byte fetch); Developer camera config screen has a
  **Snapshot** button for live preview.
- Testing agent iteration_2: **16/16 Phase 6 tests passed** — capture-per-camera on Gross/Tare,
  disk layout + SHA256, file serving 200/404, manual re-capture + sequencing, both kill switches
  (system + per-camera), RBAC separation (Image.Create vs Image.View), snapshot preview 200/409,
  and Phase 1-5 regression spot-check. No bugs found.
- Repackaged deliverable: `/app/CaneFactorySystem_Phase1-6.zip`.

## What's been implemented (2026-09 / session 3 — Phase 7: Printing Engine)
- ONE centralized `IPrintEngineService` + `IPrintRenderer` (Infrastructure/Printing/) - every
  module builds a generic `PrintDocument` (title/company/address/season/QR/generated-by + label-
  value rows); Payment/Loan/LoanRecovery/SalePurchase/Reports will add their own
  `BuildXxxSlipAsync()` later and reuse the exact same renderers, no redesign needed.
- `A4PdfRenderer` (QuestPDF, Community license): logo/company/QR header, blue title+season, blue-
  label/black-value 2-column table, footer - the generated PDF IS the print job AND the preview
  (already WYSIWYG).
- `DotMatrixEscPRenderer` (SkiaSharp + SkiaSharp.HarfBuzz for correct Devanagari shaping - verified
  working on linux-arm64 via `SkiaSharp.NativeAssets.Linux` + `HarfBuzzSharp.NativeAssets.Linux`):
  renders the identical layout to a monochrome 960px bitmap, converts 1:1 to Epson `ESC *` bit-
  image raster (`ESC @` init, `ESC 3 20` line spacing for gap-free 8-dot banding) for the TVS MSP
  270 Classic Plus. `format=preview` returns that exact source bitmap as PNG (pixel-identical to
  print). Long values are truncated with `…` to prevent column overlap (found + fixed during dev).
- Fonts bundled (never OS/printer-dependent): Noto Sans Devanagari (OFL-1.1) + Tinos (Apache-2.0,
  Times New Roman metric substitute) in `API/Assets/Fonts/`, copied to output via csproj.
- QR via `QRCoder` - Purchase ID only, never Aadhaar/bank/secrets.
- `GET /api/print/purchase/{id}?stage=GROSS|TARE&target=A4|DotMatrix&format=final|preview`
  (`Weighment.Print`) and `GET /api/print/test?target=&language=&format=` (`Print.Configure`,
  Developer-only, sample data, zero side effects). `format=final` increments new
  `Purchase.GrossPrintCount`/`TarePrintCount` (migration `AddPurchasePrintCounts`) and audits
  `Print` (first) / `Reprint` (subsequent); `format=preview` never touches either.
- `WeighmentController.AutoPrintAsync` now returns `documentUrl` alongside printer/copies/
  language whenever `PrintConfig.AutoPrint=true` - Flutter fetches it and sends bytes to the
  named printer via `printing_ffi` (`printPdf()` for A4, `rawDataToPrinter()` raw ESC/P for
  DotMatrix) - fully offline, no internet required; failures never roll back the save.
- Flutter: Weighment screen fires Auto Print after Gross/Tare and shows a manual **Reprint**
  button next to captured evidence; Developer Print config tab has **Preview** + **Print Test
  Page** (target/language override, sample long-name/zero-decimal/blank-field data).
- Testing agent iteration_3: **21/21 Phase 7 tests passed** - real ESC/P bytes (ESC @ + ESC *),
  real PDF (%PDF magic), preview WYSIWYG, Print→Reprint audit + counters, TARE-before-GROSS 409,
  invalid stage/target/format 400, purchase-not-found 404, RBAC (Operator 200 on purchase slip /
  403 on print-test; Developer 200 on both), print-test has zero side effects, Auto Print
  documentUrl wiring, and Phase 1-6 regression spot-check. 2 minor issues found + fixed same
  session: `GrossPrintCount`/`TarePrintCount` now exposed on `GET /api/purchases` and `/{id}`;
  `GET /api/audit` now honors `entity`/`entityId` query filters (previously ignored).
- Repackaged deliverable: `/app/CaneFactorySystem_Phase1-7.zip`.

## What's been implemented (2026-09 / session 4 — Phase 8: Loan & Recovery)
- User-confirmed requirements: configurable `LoanTypeMaster`, loans INTEREST-FREE, manual recovery
  only (auto-deduction is Phase 9), NO max loan amount, separate `Loan`/`LoanRecovery` tables,
  continuous `LoanId`/`LRId` from 1 via `SequenceGenerator` (never EF auto-increment), reuse Phase 7
  Print Engine for receipts.
- New entities (`Domain/Entities/Loan.cs`): `LoanTypeMaster`, `Loan` (GrowerId/GrowerCode snapshot,
  LoanAmount/RecoveredAmount/OutstandingAmount decimal(14,2), LoanStatus ACTIVE|CLOSED|CANCELLED,
  PrintCount), `LoanRecovery` (LoanId FK, RecoveryAmount, RecoveryStatus ACTIVE|REVERSED, PrintCount).
- `LoanType` added to `Permissions.Modules.Masters` + `Modules.All` - permission seeding (570→+15
  codes) and RoleMatrix (Admin full master CRUD via Modules.Masters loop, Accountant `LoanType.View`)
  needed ZERO other DbSeeder changes since Loan/LoanRecovery module codes were already reserved in
  Modules.All from Phase 1 planning. 4 default LoanTypeMaster rows seeded (Fertilizer/Seed/Equipment/
  Emergency Loan).
- `LoanTypesController` (`/api/loan-types`) - standard `MasterControllerBase<LoanTypeMaster>` CRUD.
- `LoanController` (`/api/loans`): `POST` issue (validates grower/loanType/amount>0/active season,
  idempotencyKey), `GET` list/get (Farmer object-ownership scoped via `Grower.Mobile`), `GET
  /api/loans/outstanding?growerCode=` (deliberately a QUERY param, not a path segment - GrowerCode
  values like "101/1" contain a literal '/' that ASP.NET Core never decodes from a path segment;
  this was caught and fixed during manual testing), `POST /{id}/cancel` (blocks if any recovery
  already recorded - "reverse the recoveries first").
- `LoanRecoveryController` (`/api/loan-recoveries`): `POST` record (RecoveryAmount validated to
  NEVER exceed `Loan.OutstandingAmount` - 409 if it would; auto-transitions `LoanStatus`→CLOSED when
  outstanding hits exactly 0), `GET` list/get (Farmer-scoped, added after testing agent flagged the
  asymmetry vs `LoanController`), `POST /{id}/reverse` (restores outstanding, REOPENS a CLOSED loan
  back to ACTIVE).
- Print integration: `PrintEngineService.BuildLoanSlipAsync`/`BuildLoanRecoverySlipAsync` + new
  `PrintController` actions `GET /api/print/loan/{id}` and `GET /api/print/loan-recovery/{id}`
  (same final/preview + Print/Reprint audit-counter semantics as the Phase 7 purchase slip); reuses
  `PrintConfig.LoanCopies` (already present in the entity from Phase 7 planning) for both documents.
- EF Core migration `AddLoanRecoveryModule` generated (SQL Server target; container SQLite dev DB
  uses `EnsureCreated()` so the migration file itself isn't exercised in-container).
- Flutter (untestable in this container - no Flutter SDK): `screens/loans/loan_screens.dart`
  (`LoanTypesScreen` via generic master CRUD, `LoanScreen` issue+register+cancel,
  `LoanRecoveryScreen` record+register+reverse), wired into `app_shell.dart` nav (`Loans`/`Loan
  Recovery`) and the Masters hub (`Loan Types` tab).
- Testing agent iteration_4: **44/44 backend tests passed** (1 skipped, unrelated) - LoanType CRUD +
  duplicate 409, issuance validation, continuous LoanId/LRId, idempotency, partial-recovery 2dp
  rounding (33.33+33.33+33.34=100.00 exact), over-recovery 409, cancel-with-recovery 409, full
  recovery auto-CLOSE, reverse auto-REOPEN, print PNG/PDF + audit counters, RBAC (Accountant full
  access, Operator/SalePurchase 403, Farmer read-only + ownership-scoped), regression spot-check.
  0 critical issues; 1 code-review gap (LoanRecoveryController missing Farmer scoping) fixed
  same session and rebuilt (0 errors/warnings) - not yet re-run through the full automated suite,
  but mirrors the already-tested `LoanController.ScopedQueryAsync` pattern exactly.
- Repackaged deliverable: `/app/CaneFactorySystem_Phase1-8.zip`.

## What's been implemented (2026-09 / session 5 — Phase 9: Payment & Advice)
- User-confirmed requirements: 3 Advice-batch selection modes (SINGLE purchase / DATE_RANGE /
  FARMER-wise all-pending), FULLY AUTOMATIC loan auto-deduction (FIFO oldest-loan-first, capped at
  payable amount, no user override), payment modes ONLY Cash/Bank/Mobile UPI (Razorpay/Online
  explicitly excluded - removed from seed, no gateway code anywhere), Cash Evidence photo capture
  for CASH mode only (reuses Phase 6 camera pipeline), Cancel = full reversal (never hard-delete,
  restores Purchases to payable + reverses LoanRecovery + reopens CLOSED loans), re-payment after
  cancel always gets a brand-new Advice Number.
- New entities (`Domain/Entities/Payment.cs`): `Payment` (AdviceNumber + PaymentId both continuous
  business serials from `SequenceGenerator`, TotalPurchaseAmount/LoanDeductedAmount/NetPayableAmount
  decimal(14,2), PaymentStatus COMPLETED|CANCELLED), `PaymentPurchase` (join row, snapshots amount
  at payment time), `PaymentImage` (Cash Evidence, mirrors PurchaseImage). Added `LoanRecovery.
  PaymentId` (nullable FK) so Payment.Cancel can find and reverse exactly the recoveries it created.
- `PaymentController` (`/api/payments`): `GET /eligible-purchases` (read-only preview before
  committing - shows exactly what a POST with the same criteria would produce), `POST` issue (FIFO
  loan-deduction loop, idempotencyKey, autoPrint via `PrintConfig.PaymentCopies`, captureQueued only
  for CASH), `GET` list/get (Farmer object-ownership scoped via `Grower.Mobile`, purchaseIds[] on
  Get), `POST /{id}/cancel` (full reversal), `GET/POST /{id}/images` + `/capture` (Cash Evidence,
  gated by the pre-existing `CashEvidence.*` permissions).
- Payment modes seed reduced to exactly CASH/BANK/MOBILE_UPI (ONLINE/Razorpay row removed from
  `DbSeeder`) - PaymentModeMaster stays a fully generic/configurable master so a real gateway could
  be added later as a normal mode row without touching controller code.
- Camera (`CameraCaptureService.CaptureForPaymentAsync`/`SavePaymentImageAsync`) and Print
  (`PrintEngineService.BuildPaymentSlipAsync` + `PrintController.PaymentSlip`) both extended using
  the exact same patterns as Phase 6/7, just targeting Payment instead of Purchase.
- Zero RBAC/DbSeeder changes needed beyond the PaymentModes seed fix - Payment/CashEvidence module
  permissions were already fully reserved and assigned to Accountant/Admin/Farmer roles during
  Phase 8 planning.
- EF migration `AddPaymentModule` generated (SQL Server target; SQLite dev DB uses `EnsureCreated`).
- Flutter (untestable in this container): `screens/payments/payment_screens.dart` (`PaymentScreen`
  with SINGLE/DATE_RANGE/FARMER segmented selector, live eligible-purchases + loan-deduction
  preview, payment mode dropdown, Payment Register with Cancel), wired into `app_shell.dart` nav.
- Testing agent iteration_5: **38/38 backend pytest tests passed**, 0 bugs found. Verified: all 3
  selection modes, loan-deduction math (no-loan / capped-at-payable / multi-loan FIFO / exact-close),
  cancel-then-repay gets a new Advice Number with fresh deduction, idempotency 409, captureQueued
  true only for CASH, print PNG/PDF + print/reprint counter, RBAC (Accountant Create OK, Admin
  403 on Create/Cancel, Operator/SalePurchase 403 on everything, Farmer ownership-scoped 404 not
  403), PaymentModes seed has no ONLINE/Razorpay row. Non-blocking code-review notes only
  (controller size ~408 LOC, typed DTO preferred over `Dictionary<string,string>` for Cancel body,
  in-memory idempotency cache needs Redis at scale - consistent with the Phase 8 pattern already
  in place, deferred).
- Repackaged deliverable: `/app/CaneFactorySystem_Phase1-9.zip`.

## What's been implemented (2026-09 / session 6 — Phase 10: SMS Notifications)
- User-confirmed requirements: generic/configurable HTTP SMS provider (NO hard-coded MSG91/Twilio/
  Fast2SMS), only 2 events (Tare/Final Weighment Completed + Payment Completed - explicitly NOT
  Loan), Hindi default/English switchable, encrypted credentials never exposed to any client, fully
  async/non-blocking (queue-only insert on the request thread, real HTTP delivery via a separate
  background poller), controlled retry (exponential backoff, max 5 attempts), idempotent on
  (EventCode, ReferenceId), full SMS log/queue with masked mobile numbers, all config changes/sends
  audited. Razorpay/Online explicitly stayed OUT of scope (dormant `RazorpayConfig` entity untouched).
- Extended pre-scaffolded `SmsConfig`/`SmsTemplate` entities (`Configs.cs`) with `Language`,
  `RequestContentType`, `RequestBodyTemplate`, `ResponseSuccessPath`, `ResponseSuccessValue` (config)
  and `Language` (per-template hi/en). Added new `SmsLog` entity (Status QUEUED|PROCESSING|SENT|
  FAILED|RETRY_PENDING, AttemptCount, NextAttemptAt, unique index on EventCode+ReferenceId).
- `GenericHttpSmsProvider` (`Infrastructure/Sms/`): builds the HTTP request purely from SmsConfig's
  URL/method/header/body templates with `{Mobile} {Message} {ApiKey} {ApiSecret} {SenderId}
  {EntityId}` placeholder substitution, parses the response via a configurable dotted JSON field
  path - works with ANY vendor's HTTP API without code changes.
- `SmsService.QueueAsync` - a single fast idempotent DB insert, zero network calls, wrapped in
  try/catch at every call site so SMS can never roll back a Weighment/Payment.
- `SmsQueueProcessor` (`BackgroundService`, registered in `Program.cs`, polls every 15s) - the ONLY
  place that ever makes a real network call for SMS; exponential backoff retry (3^attempt minutes),
  FAILED permanently after 5 attempts.
- `ConfigController` extended: `GetSms`/`UpdateSms` (booleans only for secrets, never plaintext),
  `SaveSmsTemplate` (upsert by EventCode+Language, validates against the 2 allowed events),
  `TestSmsConnection` (TCP reachability probe, no message sent), `TestSendSms` (one real send,
  bypasses the queue, never echoes the provider's raw response).
- New `SmsLogController` (`/api/sms-logs` list + `/retry`) - mobile numbers masked via `SmsMask`
  helper at the projection layer.
- `WeighmentController.Tare` and `PaymentController.Issue` now call `_sms.QueueAsync(...)` with
  real placeholder values, replacing the `smsQueued:false` Phase-9 placeholder.
- Required a mid-session environment fix: pod restart wiped `/opt/dotnet` and the `dotnet-ef` tool
  (reinstalled both) and the `CaneFactory.Infrastructure` project needed the
  `Microsoft.Extensions.Hosting.Abstractions` NuGet package added for `BackgroundService`.
- EF migration `AddSmsModule` generated. Flutter: extended `_SmsTab` (Developer Dashboard) with
  Language/RequestBodyTemplate/ResponseSuccessPath fields, Test Connection/Test Send buttons,
  template editor; added a new `_SmsLogsTab` (8th tab) with status filter + manual retry.
- Testing agent iteration_6: **13/13 executed backend tests passed**, 0 bugs. Verified end-to-end
  against a real local mock HTTP server: config save/encryption round-trip, template validation,
  test-connection/test-send, queue→SENT for both Tare and Payment with correct placeholder
  substitution and mobile masking, Enabled=false skip, failure→RETRY_PENDING→manual-retry→SENT,
  non-blocking guarantee (200 OK even with an unreachable SMS provider), Loan module confirmed to
  have zero SMS coupling. 2 tests skipped (pre-existing, unrelated environment quirks: mobile is
  mandatory at Grower creation so the "no mobile" path can't be hit via API; non-Developer test-user
  login-after-password-change quirk noted in earlier iterations too) - not Phase 10 bugs.
- Repackaged deliverable: `/app/CaneFactorySystem_Phase1-10.zip`.

## What's been implemented (2026-09 / session 7 — Phases 11-14: Reports, Farmer Portal, Security/Backup/Health, Final Packaging)
User explicitly said "do not ask further clarification questions for these phases — make
sensible production-grade decisions" and mandated Razorpay/Online payment be **permanently
removed everywhere** (payment modes only Cash/Bank/Mobile UPI).

- **Phase 11 (Reports)**: `ReportsController` (`/api/reports/purchases|payments|loans|
  daily-collection`) — 4 flexible, filterable, farmer-scoped endpoints cover every named report
  (Daily Weighment, Gross/Tare/Net, Village-wise, Grower-wise, Date-range, Rate-wise, Variety-wise,
  Vehicle-wise, Pending Payment, Lock, Payment, Cancel, Loan, Daily Collection) instead of 14
  hard-coded ones. Every endpoint supports `format=json|pdf|excel`, date-range + key filters +
  totals; `Report.View`/`Report.Print`/`Report.Export` permission split. Pre-existing `/api/audit`
  satisfies the Audit report. New shared `IReportExportService`/`ReportExportService`
  (QuestPDF landscape-A4 for PDF, ClosedXML for Excel) used by Reports AND the Farmer Statement.
  Flutter `screens/reports/reports_screen.dart` (untestable in-container).
- **Phase 12 (Farmer Portal)**: `FarmerController` (`/api/farmer/dashboard`, `/api/farmer/
  statement`) — resolves the caller's OWN Grower strictly via `User.Mobile == Grower.Mobile`
  (never a client-supplied id), 404 with a clear message if no Grower is linked. Dashboard =
  profile + purchase/payment/loan summary + last 5 of each. Statement = combined Purchase+
  Payment+Loan "passbook" ledger, `format=json|pdf|excel`, no extra permission needed (the
  endpoint itself is the scoping boundary). Flutter `screens/farmer/farmer_dashboard_screen.dart` +
  `My Dashboard` nav item gated by `roleOnly: 'Farmer'` in `app_shell.dart` (new `_NavItem.roleOnly`
  field, existing permission gating untouched).
- **Phase 13 (Security/Backup/Health)**: `SecurityAuditMiddleware` (registered right after
  `ExceptionMiddleware`) logs every 401/403 API response to the audit log automatically (verified:
  both Unauthenticated and PermissionDenied entries appear). New public `GET /api/health`
  (zero auth, for ops/Task Scheduler monitoring) separate from the existing role-aware `/api/
  dashboard/health`. New `BackupConfig` entity (Frequency/TimeOfDay/RetentionDays/Folder/
  Differential/TransactionLog flags, single row) + `BackupController` (`GET/PUT /api/backup/
  config` validated via `BackupConfigDto`, `GET /api/backup/script` generates the FULL+DIFF+LOG
  `.sql` from the policy, `GET /api/backup/task-scheduler-xml` generates a Task Scheduler XML —
  SQL Server 2019 Express has no SQL Agent). New `Backup` tab in Developer Dashboard
  (`settings_screens.dart`, replaced the old Razorpay tab) with Save/Download-script/
  Download-XML buttons using a new shared `lib/core/file_download.dart` helper
  (`path_provider` added to `pubspec.yaml`).
- **Razorpay fully removed**: `RazorpayConfig` entity deleted (EF migration
  `AddReportsFarmerBackupPhase` drops `RazorpayConfigs`, creates `BackupConfigs`), `ConfigController`
  Razorpay endpoints deleted, `Permissions.Modules.All` no longer has "Razorpay", `DashboardController.
  Health()` item swapped for "Backup Schedule", Flutter Razorpay tab replaced, all doc/guide text
  updated (`RAZORPAY_GUIDE.md` deleted, `SECURITY_CHECKLIST.md`/`ROLE_PERMISSION_MATRIX.md`/
  `SETUP_GUIDE.md`/`API_DOCUMENTATION.md`/`ER_DIAGRAM.md`/`UserGuideController.cs`/`README.md`
  updated). `database/scripts/02_schema_migration.sql` regenerated via `dotnet ef migrations
  script --idempotent` so it matches the current model exactly (historical migration replay still
  shows `CREATE TABLE RazorpayConfigs` then `DROP TABLE` — this is correct/expected EF migration
  history, not a leftover bug).
- **Phase 14 (Final Packaging)**: `README.md` Phase Status table rewritten (all 14 phases marked
  complete, was stuck at "Phases 1-5" since session 1), `API_DOCUMENTATION.md` gained full Reports/
  Farmer/Backup/Health sections, `BACKUP_GUIDE.md` gained a "generate from the app" section
  pointing at the new endpoints. Final deliverable: **`/app/CaneFactorySystem_Final_Complete.zip`**
  (183 files, source-only — bin/obj/.git excluded) supersedes `CaneFactorySystem_Phase1-10.zip`
  (deleted).
- Found + fixed a PRE-EXISTING bug while testing (not part of this session's new code but
  blocking it): `DashboardController.Summary()` and any `IQueryable<T>.SumAsync(x =>
  (decimal?)x.Prop)` throws on the SQLite dev provider (see gotcha note above) — fixed with the
  fetch-then-client-sum pattern everywhere it appeared.
- Testing agent iteration_7: **22/22 backend tests passed**, 0 critical issues. 2 minor code-review
  notes (BackupController DTO strictness, one CS8601 nullable warning) fixed same session
  (added `BackupConfigDto` with `[Required]` fields, rebuilt with 0 warnings/0 errors).
  Frontend (Flutter) skipped per established precedent — cannot build/run in this container.

## Prioritized backlog (next phases per spec)
All 14 phases from the original spec are now complete. Optional future enhancements (none
requested by the user yet):
- Physical validation on real Windows hardware: RS232 digitizer, dot-matrix printer, IP cameras,
  SQL Server 2019 Express install, and a real Flutter build/run (all mocked/simulated in this
  container — see "Project Health Check" pattern from earlier sessions).
- Dependency vulnerability scanning in CI (`dotnet list package --vulnerable`, `flutter pub
  outdated`) — flagged as pending in `SECURITY_CHECKLIST.md`.
- Advisory (Phase 8/9 test review, non-blocking, still open): typed DTO instead of
  `Dictionary<string,string>` for a couple of older Cancel bodies; in-memory idempotency cache
  needs Redis at real multi-instance scale.

## Test credentials
See /app/memory/test_credentials.md (developer / DevSecure@2026 on current dev DB; fresh DB =
Dev@2026Temp with forced change; opuser2 / OpSecure@2026 for Operator-role RBAC testing).

## Final deliverable
`/app/CaneFactorySystem_Final_Complete.zip` — complete source (backend + frontend + database
scripts + docs), Razorpay fully removed, all 14 phases included, 0 build errors/warnings.
