# RAZORPAY / RAZORPAYX GUIDE

The ONLINE payment mode uses **RazorpayX payouts** for actual farmer disbursement (not the
ordinary payment-gateway checkout). Phase 5 ships the complete configuration surface with
encrypted secret storage; live payout execution + webhook verification activate in Phase 10.

## Getting credentials
1. Create a Razorpay account → https://dashboard.razorpay.com
2. For payouts, enable **RazorpayX** and note your RazorpayX **Account Number**.
3. Settings → API Keys → Generate **Test** keys first (`rzp_test_...`), later Live keys.
4. Webhooks (Phase 10): configure endpoint `https://<server>/api/razorpay/webhook` with a webhook
   secret; the backend verifies the `X-Razorpay-Signature` HMAC before trusting any event.

## Application configuration (Developer Dashboard → Configuration → Razorpay)
| Field | Notes |
|---|---|
| Enabled | master switch |
| Mode | Test / Live |
| Key ID / Key Secret / Webhook Secret | stored **AES-256-GCM encrypted**; UI shows only set/not-set |
| Account Number | RazorpayX debit account for payouts |

## Security rules (enforced)
- All Razorpay calls are **server-side only**; secrets never reach Flutter/Web/Mobile.
- Payout operations use **idempotency keys** so a retry can never double-pay a farmer.
- The system maintains OrderId / PaymentId / PayoutId / Status / FailureReason / ReferenceId
  per transaction, and every Razorpay event is written to the audit log.
- Webhook payloads are verified server-side before any state change.

## Test → Live checklist (Phase 10 go-live)
1. Test mode payout to a test fund account succeeds and reconciles.
2. Webhook signature verification passes; failure events update payment status correctly.
3. Switch Mode to Live, paste Live keys (Test keys remain encrypted in history), re-run one
   small real payout before batch processing.
