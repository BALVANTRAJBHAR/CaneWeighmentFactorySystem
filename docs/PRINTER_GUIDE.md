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

Every Gross/Tare save that has `AutoPrint=true` and copies > 0 returns an `autoPrint` instruction
(printer, copies, language, ready-made `documentUrl`) - the Flutter client fetches that URL and
sends the bytes to the named printer via `printing_ffi` (raw ESC/P for DotMatrix, PDF for A4).

## Print/report layout standard (applies to ALL reports)
Logo (top-left, or a placeholder box if no logo uploaded) • QR code (top-right, safe identifier
only — PurchaseId/AdviceNumber/LoanId; never secrets/Aadhaar) • Company name (top-center, blue) •
Address • Report header (blue) • Generated-by + Print date/time • Season • two-column blue-label/
black-value rows • Times New Roman on A4 (bundled Tinos substitute) / Noto Sans Devanagari for
Hindi (bundled, default print language) • identical standard across Purchase/Payment/Loan/
Recovery/Hourly/Grower/Master/SalePurchase reports.

## How printing works (Phase 7)
- **Centralized backend Print Engine** (`Infrastructure/Printing/`): every module builds a
  `PrintDocument` (title, company/address/season/QR/generated-by, then a list of label/value rows)
  and hands it to ONE of two renderers - `A4PdfRenderer` (QuestPDF) or `DotMatrixEscPRenderer`
  (SkiaSharp + HarfBuzz). No module ever writes its own print/PDF/ESC-P code.
- **Fonts are bundled, not system-dependent**: Noto Sans Devanagari (OFL-1.1, Hindi) and Tinos
  (Apache-2.0, a metric-compatible Times New Roman substitute for English) ship in
  `API/Assets/Fonts/` and are loaded via `SKTypeface.FromFile` / registered into QuestPDF -
  printing never depends on the printer's or the OS's own fonts. Text shaping uses
  `SkiaSharp.HarfBuzz` (`SKShaper`) so Devanagari conjuncts/matras render correctly - simple
  codepoint-to-glyph mapping is NOT used.
- **A4**: the generated PDF (`GET /api/print/purchase/{id}?target=A4&format=final`) IS the print
  job and IS the preview (`format=preview` returns the identical PDF - already WYSIWYG).
- **Dot Matrix (TVS MSP 270 Classic Plus, 9-pin ESC/P2-compatible)**: the identical information is
  first rasterized to a monochrome 960px-wide bitmap (necessarily single-color - 9-pin hardware
  has no color), then converted 1:1 to Epson `ESC *` bit-image graphics banded 8 dots at a time
  with line-spacing locked via `ESC 3 20` so bands tile with no gaps/overlap. `format=preview`
  returns that exact source bitmap as PNG - print and preview are pixel-identical. Long values are
  truncated with `…` rather than overlapping the next column.
- **QR code**: `QRCoder` generates a PNG containing ONLY the Purchase ID - never Aadhaar/bank/
  password/API-key data.
- **Auto Print / local factory printing**: the Flutter client sends bytes to `printerName` via
  `printing_ffi` (native `winspool`, no internet required) - `printPdf()` for A4,
  `rawDataToPrinter()` (raw ESC/P) for DotMatrix. A failed/offline printer never rolls back the
  already-completed weighment - it only shows a clear error + a manual **Reprint** button
  (Weighment screen).
- **Reprint & audit**: every `format=final` call increments `Purchase.GrossPrintCount` /
  `TarePrintCount` and writes an audit entry - `Print` the first time, `Reprint` afterwards.
  `format=preview` never touches counters or the audit log. Reprinting only re-renders existing
  data; it can never create a duplicate Purchase/Payment/transaction.
- **Print Test** (Developer Dashboard → Configuration → Print): `GET /api/print/test?target=&
  language=&format=` renders sample data (long name, zero-decimal, blank field) with no purchase/
  audit side effects - use **Preview** to see the exact DotMatrix bitmap or **Print Test Page** to
  send a real job, before trusting Auto Print in production.
- **Endpoints**: `GET /api/print/purchase/{id}?stage=GROSS|TARE&target=A4|DotMatrix&format=
  final|preview` (needs `Weighment.Print`), `GET /api/print/test?...` (needs `Print.Configure`).
- **Extending to Payment/Loan/LoanRecovery/SalePurchase/Reports**: add a
  `BuildXxxSlipAsync(...)` to `PrintEngineService` returning a `PrintDocument` with that module's
  rows - the renderers, fonts, QR helper and Auto Print/Reprint/audit plumbing are already generic
  and require zero changes.

## Troubleshooting
- Printer offline → check cable/power, reprint from the transaction (data is already saved).
- Wrong characters on dot matrix → this build never relies on the printer's built-in font (Hindi
  is always rasterized), so a garbled printout means the wrong `printerName` is configured, or the
  printer's own ESC/P dialect differs - contact support with the exact printer/driver model.
- Auto print not firing → verify Auto Print = ON and copies > 0 for that document type.
- Windows raw printing needs `printing_ffi` (winspool) - no separate driver "Generic/Text Only"
  mode is required since we send fully-formed ESC/P bytes directly.
