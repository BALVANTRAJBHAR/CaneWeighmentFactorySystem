# SMS GUIDE — Generic DLT-Compatible HTTP Provider

The SMS module is provider-agnostic (NOT hard-coded to MSG91/Twilio/Fast2SMS). Any Indian
DLT-compliant HTTP SMS gateway can be configured from the Developer Dashboard.

## Configuration (Developer Dashboard → Configuration → SMS)
| Field | Example |
|---|---|
| Provider Name | MSG91 / Fast2SMS / TextLocal / custom |
| API Base URL | `https://api.provider.com/v2/sms/send` |
| HTTP Method | POST / GET |
| API Key / API Secret | stored **AES-256-GCM encrypted**, never shown again, never sent to clients |
| Authorization Header | e.g. `authkey` / `Bearer` |
| Sender ID | 6-char DLT-approved header, e.g. `FCTORY` |
| DLT Entity ID | where the provider requires it |
| Templates | per event (TARE_COMPLETED, PAYMENT_COMPLETED) with DLT Template IDs |
| Enabled | master switch |

## Rules enforced by the system
- SMS is sent **only from the ASP.NET Core backend** — credentials never reach Flutter/Web.
- Success/failure is logged in the audit log **without** logging secrets or full message bodies.
- SMS events (Phase 10): Tare completion → grower; Payment completion → each farmer in the batch;
  SalePurchase completion → configured Party/Manager/Admin recipients (multiple numbers supported).
- If internet is down, factory weighment continues; SMS attempts fail gracefully and are logged.

## DLT prerequisites (India)
1. Register your entity + sender header + message templates on your telecom DLT portal.
2. Enter the approved Template IDs in the SMS template configuration.
3. Use the Test SMS function (Phase 10) against your own number before enabling globally.
