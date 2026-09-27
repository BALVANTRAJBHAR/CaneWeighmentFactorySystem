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

## Android SIM Gateway

`Android SIM` is an additional provider; it does not replace or change existing HTTP providers.
The API writes the normal durable `SmsLogs` queue and a paired Android phone claims each row before
sending it with `SmsManager`.

### Pair and start a phone

1. Deploy the API behind HTTPS and apply migration `20260927120000_AddAndroidSimSmsGateway`.
2. In **Configuration → SMS**, choose **Android SIM**. Enter Configuration Name, a unique Device ID,
   a random API key of at least 12 characters, SIM slot and poll interval.
3. Click **Register / Rotate Device Key**, then **Save SMS Configuration** with Active enabled.
   The API stores only a salted PBKDF2 hash of the device key and never returns the key.
4. Install the Android build on the SIM phone and open **SIM Gateway**. Enter the same HTTPS server
   URL, Device ID and API key, then tap **Validate & Save**.
5. Tap **Start Gateway** and grant SMS, phone/SIM and notification permissions. Keep the persistent
   foreground notification enabled. The gateway sends a heartbeat every 25 seconds.
6. Reboot the phone once to verify auto-start. `GatewayBootReceiver` starts the foreground service
   after `BOOT_COMPLETED` only when the user left Gateway ON. Using **Stop Gateway** also disables
   boot auto-start.

### Test and operations

- **Test Connection** reports Connected, Offline, Invalid Credentials, Not Paired or Disabled.
- **Send Test SMS** uses the same durable queue and phone claim flow as business SMS.
- Turning Active OFF immediately prevents pending rows from being returned and cancels undelivered
  Android queue rows. A claimed phone reports the final send result before the row is considered sent.
- Stale Processing rows return to retry after two minutes, with a maximum of three claims.
- Default SIM, SIM 1 and SIM 2 are supported; long messages use multipart SMS and are marked sent
  only after all Android sent callbacks succeed.

### Required Android permissions

- `INTERNET`
- `SEND_SMS`
- `READ_PHONE_STATE` (SIM 1 / SIM 2 subscription selection)
- `RECEIVE_BOOT_COMPLETED`
- `FOREGROUND_SERVICE` and `FOREGROUND_SERVICE_REMOTE_MESSAGING`
- `POST_NOTIFICATIONS` on Android 13+
