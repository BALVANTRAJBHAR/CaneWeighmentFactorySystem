# TEST REPORT — Phases 1–5

**Environment**: .NET 8.0.424 SDK, ASP.NET Core 8 API, EF Core 8.0.11.
Backend executed end-to-end in a Linux container using the documented `Sqlite` dev provider
(identical business code path; production runs the same code on SQL Server 2019 Express via the
generated migration script `database/scripts/02_schema_migration.sql`).

## Build
- `dotnet build` (4 projects, Clean Architecture): **0 errors, 0 warnings** ✅
- EF Core migration `InitialCreate` generated; idempotent SQL Server script produced (1,310 lines) ✅

## Phase 1 — Auth + RBAC (all verified via live API calls)
| Test | Result |
|---|---|
| Seed: 7 roles, 570 permissions, developer account, masters, String Type 15 preset | ✅ |
| Login developer + temporary password → `mustChangePassword: true` | ✅ |
| Permission-protected APIs blocked until forced password change completes | ✅ |
| Change password → old sessions revoked → re-login works | ✅ |
| Wrong credentials → 401 generic message; failed-attempt counter + lockout config | ✅ |
| Refresh rotation; invalid refresh token → 401; reuse detection revokes session family | ✅ |
| Operator on Developer config (`PUT /api/config/weight-rules`) → **403** | ✅ |
| Operator on SMS credentials / user management → **403** | ✅ |
| Unauthenticated `/api/purchases` → **401** | ✅ |
| Role-specific user guide returns only Operator guide for operator | ✅ |

## Phase 2 — Masters + validation
| Test | Result |
|---|---|
| Zone create → business ID **1** (sequence) | ✅ |
| Village create → business ID **101**; duplicate " rampur " (trim+case) blocked in same zone | ✅ |
| Bank IFSC format + BankName+Branch and IFSC uniqueness (server-side) | ✅ (unit-level) |
| Party Name+Mobile pair blocked; separate duplicate-check endpoint for client warnings | ✅ |

## Phase 3 — Grower / Rate / Purchase
| Test | Result |
|---|---|
| Grower create → GrowerCode **101/1** (transactional per-village sequence) | ✅ |
| Duplicate Aadhaar → **blocked** (hash-unique); Aadhaar stored AES-encrypted, masked in responses | ✅ |
| Duplicate mobile / same name+father in village → 422 warning `requiresConfirmation` | ✅ |
| Rate create 375.50 effective-dated; overlap of active periods blocked | ✅ |

## Phase 4 — Weighing / Parser engine
| Test | Result |
|---|---|
| String Type 15 parse of spec sample `02 20 30 30 31 35 30 30 03 0D 0A` → SIGN `+`, WEIGHT `001500`, NUMERIC 1500, FRAME VALID | ✅ |
| Negative sign frame (0x2D) → -250 | ✅ |
| Malformed frame `02 20 30 30` → FRAME INVALID, safely rejected, no crash | ✅ |
| Simulator generates Type 15 frames through the same parser + SignalR broadcast | ✅ |

## Phase 5 — Unified Gross/Tare
| Test | Result |
|---|---|
| Gross below minimum (5.00 < 10.00 Qtl) → blocked + `soundEvent: BELOW_MINIMUM` | ✅ |
| Gross 25050 KG → "Gross weighment completed successfully. Purchase ID: 1. Gross Weight: 250.50 Quintal." + rate snapshot 375.50 + autoPrint payload | ✅ |
| Duplicate gross (same idempotency key) → blocked | ✅ |
| Pending-tare grid returns the gross transaction | ✅ |
| Tare 8341 KG → Net 167.09 = 250.50−83.41; Cutting 2% = 3.34; Tax 1% = 1.67; **Final 162.08**; Amount **60,861.04** (all Quintal 2dp, server-side) | ✅ |
| Tare ≥ gross rejected; second tare on same purchase rejected; cancelled/locked purchase rejected | ✅ |

## Known Issues / Notes
1. **Flutter build not executed in this container** (no official Linux ARM64 Flutter SDK exists).
   Run locally: `flutter create . --platforms=...` → `flutter pub get` → `flutter analyze` →
   `flutter run`. Code targets Flutter stable ≥3.22 / Dart ≥3.3.
2. Physical RS232, dot-matrix printing, real camera capture and TTS voices require the Windows
   factory environment — configuration + simulator + parser paths are fully tested here.
3. Auto-print / SMS / camera-capture return configuration payloads on save; the physical
   execution engines are Phase 6/7/10 scope per the approved plan.
4. `db_ddladmin` can be revoked from the app SQL login once the schema is stable.
