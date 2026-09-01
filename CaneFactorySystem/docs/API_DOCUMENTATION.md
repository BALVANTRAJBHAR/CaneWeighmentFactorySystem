# API DOCUMENTATION — CaneFactory API (Phases 1–5)

Base URL: `http(s)://<server>:<port>` — all business endpoints under `/api`. Interactive docs at `/swagger`.
Auth: `Authorization: Bearer <accessToken>`. 401 = unauthenticated, 403 = missing permission.
All list endpoints support `?search=&includeInactive=&sortBy=id|name&desc=&page=&pageSize=`.

## Auth (`/api/auth`) — anonymous unless noted, rate-limited 10/min/IP
| Method | Path | Body | Notes |
|---|---|---|---|
| POST | /login | `{username,password,deviceInfo?}` | Returns access+refresh tokens, user info incl. permissions, `mustChangePassword` |
| POST | /refresh | `{refreshToken}` | Rotation; reuse of a revoked token revokes all sessions |
| POST | /logout 🔒 | `{refreshToken}` | Revokes that session |
| POST | /logout-all 🔒 | — | Revokes all sessions |
| GET | /sessions 🔒 | — | Device/session list |
| GET | /me 🔒 | — | Current user + permissions |
| POST | /change-password 🔒 | `{currentPassword,newPassword,confirmPassword}` | Revokes other sessions; clears forced-change flag |
| POST | /forgot-password/start | `{mobile}` | Sends OTP (logged server-side until SMS phase) |
| POST | /forgot-password/verify | `{mobile,otp}` | |
| POST | /forgot-password/reset | `{mobile,otp,newPassword}` | Revokes all old sessions |

## Users & Roles
| Method | Path | Permission |
|---|---|---|
| GET/POST | /api/users | User.View / User.Create |
| PUT | /api/users/{id} | User.Edit (deactivation revokes sessions) |
| POST | /api/users/{id}/reset-password | User.Edit |
| DELETE | /api/users/{id} | User.Delete (soft) |
| GET | /api/roles | Role.View |
| GET | /api/roles/permissions | Permission.View |
| POST | /api/roles | Role.Create |
| PUT | /api/roles/{id}/permissions | Role.Edit |

## Masters (all follow the same CRUD contract: GET list, GET {id}, POST, PUT {id}, DELETE {id} soft)
| Endpoint | Module perm | Special |
|---|---|---|
| /api/zones | Zone.* | Business ID starts at 1 (sequence) |
| /api/villages | Village.* | Business ID starts at 101; unique per zone |
| /api/banks | Bank.* | IFSC + BankName+Branch uniqueness |
| /api/vehicle-types | Vehicle.* | |
| /api/variety-types | VarietyType.* | |
| /api/varieties | Variety.* | `GET /api/varieties/by-type/{varietyTypeId}` cascade |
| /api/items | Item.* | |
| /api/parties | Party.* | `GET /duplicate-check?name=&mobile=`; Name+Mobile pair blocked |
| /api/seasons | Season.* | Activating a season deactivates others |
| /api/payment-modes | PaymentMode.* | |

## Growers (`/api/growers`) — Grower.*
| Method | Path | Notes |
|---|---|---|
| GET | / | `?search=&searchBy=name|father|village|code|mobile` |
| GET | /by-code?code=101/1 | Weighment lookup (read-only grower panel) |
| GET/POST/PUT/DELETE | standard | POST: Aadhaar unique (blocked), mobile & name+father duplicates return 422 `requiresConfirmation` until `acceptDuplicateWarning=true`; code auto-generated `VillageId/Seq` |

## Rates (`/api/rates`) — Rate.*
| Method | Path | Notes |
|---|---|---|
| GET | / | Versioned list |
| GET | /current/{varietyTypeId} | Effective rate now |
| POST | / | Overlap-blocked new period |
| POST | /{id}/close | Close period (no in-place history edits) |
| GET | /recalculate/preview?varietyTypeId=&newRate= | Rate.Approve — unpaid records only |
| POST | /recalculate/apply | Rate.Approve — audited per record |

## Weighment (`/api/weighment`)
| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | /pending-tare | Weighment.View | Pending gross grid |
| GET | /purchase/{id}/for-tare | Weighment.View | Validates exists/gross-done/tare-pending/not-cancelled/not-locked |
| POST | /gross | Weighment.Create | Full server validation, min-weight rule, rate snapshot, sequence PurchaseId, idempotencyKey; returns success message + `autoPrint` + `soundEvent` |
| POST | /tare | Weighment.Edit | Server-side Net/Cutting/Tax/Final/Amount (Quintal, 2dp); marks Payment PENDING |
| GET | /rules | Weighment.View | Min-weight + sound config + messages for the client |

## Purchases (`/api/purchases`) — Purchase.*
GET list/detail (Farmer sees own only), POST /{id}/lock, /{id}/unlock, /{id}/cancel (reason required, paid blocked).

## Devices & Profiles (Developer)
| Path | Permission |
|---|---|
| GET/POST/PUT /api/devices, /{id}/activate, /{id}/deactivate, /{id}/history | Device.View / Device.Configure |
| GET /api/devices/ports, POST /{id}/connect, /disconnect, /start-reading, /stop-reading | Device.Configure |
| GET /api/devices/live-weight | Weighment.View |
| POST /api/devices/test-parser `{stringProfileId,rawHex|rawText}` | Device.Configure |
| POST /api/devices/simulator/start|set-weight|stop | Device.Configure |
| /api/string-profiles CRUD + /{id}/duplicate | Device.View / Device.Configure |

## Configuration (`/api/config`) — Developer
weight-rules (GET/PUT), sound (GET/PUT) + sound/messages/{id} (PUT), cameras (GET/POST) +
cameras/{id}/test (POST), print (GET/PUT), sms (GET/PUT), razorpay (GET/PUT), company (GET/PUT),
settings (GET) + settings/{key} (PUT). Secrets always stored encrypted, never returned.

## Dashboard, Audit, Guide
| Path | Permission | Notes |
|---|---|---|
| GET /api/dashboard/header 🔒 | any authenticated | Company, season, server time, user/roles |
| GET /api/dashboard/summary | Dashboard.View | Today's vehicles/weights/pending counts |
| GET /api/dashboard/health | Health.View | **Role-aware** health items (Green/Yellow/Red) |
| GET /api/audit | Audit.View | Filters: module/action/username/date, paged |
| GET /api/user-guide | UserGuide.View | Only the caller's role guides |
| GET /api/ping | anonymous | Liveness |

## Real-time
SignalR hub `/hubs/weight` (JWT via `access_token` query param) — event `liveWeight`:
`{weightKg, weightQuintal, weightUnit, stable, deviceConnected, lastReceivedAt, deviceName, error}`.
