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
  (57 C# files). JWT + refresh rotation, permission policies (570 perms), AES-256-GCM SecretProtector,
  transactional NumberSequence generator, configurable parser engine (FixedPosition/Delimiter/Regex/
  KeyValue/Json), System.IO.Ports serial reader + simulator, SignalR /hubs/weight, audit log,
  vendor-abstracted camera capture (ISAPI/ONVIF/RTSP/SIMULATOR providers, Infrastructure/Camera/).
- `/app/CaneFactorySystem/frontend/cane_factory_app` — Flutter app (22 Dart files): login/forced
  change/forgot-OTP, role-aware shell, dashboard+role-aware health, generic master CRUD engine,
  grower master+search, unified GROSS/TARE weighment screen with live weight + TTS sound state
  machine + camera panel + captured-evidence thumbnails, developer device/parser/simulator screens,
  config hub (weight rules/sound/cameras+snapshot preview/print/SMS/Razorpay/company), users/roles,
  audit, role-specific user guide, light/dark + 6 theme colors persisted.
- `/app/CaneFactorySystem/database/scripts` — 01 create DB + least-privilege login, 02 idempotent
  EF-generated SQL Server schema (1,310 lines).
- `/app/CaneFactorySystem/docs` — 14 guides (setup, flutter builds, API, RS232, camera, printer,
  SMS, Razorpay, backup, security checklist, role matrix, sequences, ER diagram, test report).
- Zip: `/app/CaneFactorySystem_Phase1-6.zip`.

## Dev/Test environment notes (container)
- .NET 8 SDK at `/opt/dotnet` (reinstall if pod restarts: dotnet-install.sh --channel 8.0).
- API tested in-container with documented `Sqlite` dev provider on localhost:8001
  (start command in /app/memory/test_credentials.md). Production = SqlServer provider.
- Flutter cannot build here (no Linux ARM64 SDK) — user builds locally per docs.
- Platform supervisor FastAPI backend intentionally stopped (project doesn't use it).

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

## Prioritized backlog (next phases per spec)
- P0 Phase 7: printing engine (dot matrix ESC/P + A4, Hindi slip layout, QR codes, report standard).
- P0 Phase 8–9: Loan + Recovery, Payment + Advice (sequence from 1) + batch payments + cash evidence
  + cancellation/reversal.
- P1 Phase 10: SMS send engine + RazorpayX payouts + webhook verification.
- P1 Phase 11: Reports (hourly buckets, exports Excel/PDF).
- P1 Phase 12: Farmer mobile/web portal views. SalePurchase weighment module + SalePurchase role screens.
- P2 Phase 13–14: hardening, dependency scanning, backups automation, tests, production packaging.
- Advisory (from Phase 1-5 test review): standardize 400/409/422 usage across masters; PATCH-style
  weight-rules update; audit sanitizer for token-like strings; document name+father duplicate
  warning trigger.
- Advisory (from Phase 6 test review, non-blocking): PATCH /api/config/cameras/{id} for partial
  field updates (avoid re-sending password on a mere toggle); typed CaptureRequest DTO instead of
  Dictionary<string,string>; explicit `captureDisabled` boolean in the kill-switch response;
  Cache-Control header on GET /api/images/{id}/file (images are immutable once captured).

## Test credentials
See /app/memory/test_credentials.md (developer / DevSecure@2026 on current dev DB; fresh DB =
Dev@2026Temp with forced change; opuser2 / OpSecure@2026 for Operator-role RBAC testing).
