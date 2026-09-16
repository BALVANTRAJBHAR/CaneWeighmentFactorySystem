# SECURITY CHECKLIST — OWASP-aligned (Phase 1–5 status)

## Authentication & Sessions
- [x] Passwords hashed with ASP.NET Core Identity `PasswordHasher` (PBKDF2, per-user salt) — never plaintext
- [x] Short-lived access tokens (15 min JWT) + 7-day refresh tokens
- [x] Refresh token **rotation** with SHA-256 hashed storage (raw token never stored)
- [x] Refresh-token **reuse detection** → all sessions revoked automatically
- [x] Logout + logout-all-sessions + session/device listing
- [x] Login attempt limit (5) + account lockout (15 min) — configurable in SystemSettings
- [x] Rate limiting: global 300/min/IP + strict 10/min/IP on auth endpoints
- [x] Forced password change on first login (temporary passwords) — blocks all protected APIs until changed
- [x] Forgot password: Mobile → OTP (hashed, 5-min expiry, 5 attempts) → reset → all sessions revoked
- [x] No public registration/sign-up anywhere
- [x] Deactivated users: tokens revoked, API blocked ≤60s, history preserved

## Authorization (server-side boundary)
- [x] RBAC: Users / Roles / Permissions / UserRoles / RolePermissions (570 granular permissions)
- [x] Every protected endpoint enforces permissions server-side (`[HasPermission]` policy or imperative check)
- [x] Copied URL / manipulated object ID never bypasses authorization → 401 unauthenticated, 403 unauthorized
- [x] Account-active re-verified server-side per request (60s cache)
- [x] Farmer object-ownership scoping on purchases (404 for out-of-scope IDs — no record-existence leak)
- [x] Frontend hides unauthorized buttons/menus, but is explicitly NOT the security boundary

## Data Protection
- [x] AES-256-GCM encryption for stored secrets: SMS keys, camera passwords, Aadhaar
- [x] Aadhaar: encrypted at rest, unique via SHA-256 hash, only masked (XXXX-XXXX-1234) ever returned, never in reports
- [x] Bank account numbers masked in all list/lookup responses
- [x] Secrets from environment variables only; `.env.example` has placeholders; no secrets in Git
- [x] Config endpoints return `hasPassword/hasApiKey` flags — never the secret value

## Injection & Input
- [x] EF Core parameterized queries everywhere (no string-concatenated SQL); raw SQL uses parameters
- [x] Server-side validation: required/max-length/numeric/decimal(2)/mobile(10)/email/IFSC/Aadhaar/enum/FK-existence/duplicates/inactive-reference — on every form
- [x] Case-insensitive + trimmed duplicate checks
- [x] Model binding hardened (audit fields stripped server-side against mass assignment)

## Transport & Headers
- [x] HTTPS/TLS 1.2+ (Kestrel binding documented), HSTS outside Development
- [x] CORS allowlist (no wildcard with credentials)
- [x] X-Content-Type-Options: nosniff, Referrer-Policy: no-referrer, X-Frame-Options: DENY, CSP default-src 'self'

## Error Handling & Logging
- [x] Global exception middleware — clients get a generic message + trace reference; stack traces/connection strings never leave the server
- [x] Audit log for every critical action (login/failed login/password events/user/role/permission changes/master CRUD/rate changes/gross/tare/lock/cancel/config changes) with UserId, Role, Action, Module, Entity, EntityId, OldValue, NewValue, Timestamp, IP, Device, Success, FailureReason

## Concurrency & Integrity
- [x] Business serials via transactional NumberSequence table (row-locked) — two operators can never receive the same number
- [x] Idempotency keys on gross/tare; duplicate save/double-click blocked
- [x] Unique DB indexes: username, permission code, zone name, village-in-zone, grower code, village+sequence, aadhaar hash, IFSC-pair, item, mode code
- [x] Soft delete everywhere; transactional records use Cancel/Reversal (no hard delete)

## Client (Flutter)
- [x] Tokens in `flutter_secure_storage` (Windows Credential Manager / Android Keystore / iOS Keychain)
- [x] No API secrets in the Flutter app; API URL injected via `--dart-define`
- [x] Android release: R8/ProGuard minification + `--obfuscate --split-debug-info` (build guide)

## Pending (later phases)
- [ ] File-upload validation pipeline (MIME/magic-bytes/decode/size/traversal/malware hook) — Phase 6 with camera capture
- [ ] Dependency vulnerability scanning in CI (`dotnet list package --vulnerable`, `flutter pub outdated`) — Phase 13/14

## Phase 13 additions (Security, Backup & Health)
- [x] `SecurityAuditMiddleware` logs every 401/403 API response (unauthenticated + permission-denied) to the audit log
- [x] Public unauthenticated `/api/health` liveness probe, separate from the role-aware in-app `/api/dashboard/health`
- [x] `BackupConfig` (Frequency/TimeOfDay/RetentionDays/Folder) + generated FULL+DIFF+LOG `.sql` script and Windows
      Task Scheduler XML — SQL Server 2019 Express has no SQL Agent, so scheduling happens via `schtasks`/Task Scheduler
- [x] Online payment gateway permanently removed (Cash/Bank/Mobile UPI only) — no Razorpay code, config or docs remain
