# PRINTER GUIDE — Dot Matrix + A4

## Supported strategies
1. **Dot Matrix** — TVS Electronics MSP 270 Classic Plus (ESC/P text-mode strategy; A4 layout is never forced onto dot matrix)
2. **A4** — any Windows laser/inkjet via installed driver

## Windows setup (TVS MSP 270 Classic Plus)
1. Connect via USB/parallel; install the TVS Windows driver (or "Generic / Text Only" for raw ESC/P).
2. Note the exact Windows printer name (Control Panel → Devices and Printers).
3. Load continuous stationery; set paper size in driver preferences.

## Application configuration (Developer Dashboard → Configuration → Print)
| Setting | Meaning |
|---|---|
| Printer Type | DotMatrix / A4 |
| Printer Name | exact Windows printer name |
| Paper Type | Continuous / A4 |
| Auto Print | ON = print automatically after every successful save |
| Copies | separate counts for Gross / Tare / Payment / Loan / SalePurchase (0–5) |
| Language | Hindi (default) / English |

Phase 5 returns the `autoPrint` instruction (printer, copies, language) with every successful
Gross/Tare save; the full print rendering engine (Hindi किसान पर्ची layout, logo, QR code with
PurchaseId/AdviceNumber, blue headers/black data, Times New Roman for A4 reports) is implemented
in Phase 7 using this configuration.

## Print/report layout standard (applies to ALL reports from Phase 7)
Logo (top-left) • QR code (top-right, safe identifier only — PurchaseId/AdviceNumber/LoanId; never
secrets/Aadhaar) • Company name (top-center) • Address • Report header • Generated-by + Print
date/time • Season • Times New Roman • identical layout across Purchase/Payment/Loan/Recovery/
Hourly/Grower/Master/SalePurchase reports.

## Troubleshooting
- Printer offline → check cable/power, reprint from the transaction (data is already saved).
- Wrong characters on dot matrix → use driver "Generic/Text Only" and verify code page.
- Auto print not firing → verify Auto Print = ON and copies > 0 for that document type.
