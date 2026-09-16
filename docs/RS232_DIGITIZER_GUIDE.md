# RS232 / DIGITIZER GUIDE — Weighing Indicator Communication

## 1. Physical Setup
1. Connect the weighing indicator's RS232 output to the PC:
   - Direct DB9 serial port, or a **USB-to-Serial (FTDI/Prolific)** adapter.
2. Install the adapter driver; note the COM port in Windows Device Manager → Ports (COM & LPT),
   e.g. `COM3`.
3. Standard wiring: indicator TX → PC RX (pin 2), GND → GND (pin 5). Most indicators transmit
   continuously without handshaking (Flow Control = None).

## 2. Supplied Indicator Preset — "String Type 15" (pre-seeded)
| Setting | Value |
|---|---|
| Baud Rate | 2400 |
| Parity | None |
| Data Bits | 8 |
| Stop Bits | 1 |
| Flow Control | None |
| Frame | `STX(0x02) SIGN(0x20/0x2D) W1..W6 ETX(0x03) CR(0x0D) LF(0x0A)` |
| Weight field | 6 ASCII characters |

Example frame: `02 20 30 30 31 35 30 30 03 0D 0A` → SIGN `+`, WEIGHT `001500`, NUMERIC `1500 KG`, FRAME VALID.

**Nothing is hard-coded** — baud/parity/positions/bytes are all editable in
Developer Dashboard → Weighing Device / String Profiles.

## 3. Configuration Workflow (Developer Dashboard → Weighing Device)
1. **Device**: set COM port, baud, parity, data/stop bits, flow control, timeouts, auto-reconnect.
2. **String Profile**: select the parser type and byte positions:
   - Parser types: Fixed Position, Delimiter, Regex, Key-Value, JSON.
   - Fixed Position fields: StartByte, SignByte/SignPosition, WeightStartPosition, WeightLength,
     character order (Normal/Reversed), decimal places, EndByte, CR/LF, stable-weight duration.
3. **Test Parser**: paste a RAW HEX or text frame → verify SIGN / WEIGHT / NUMERIC / FRAME VALID.
4. **Connect → Start Reading**: watch live diagnostic (connection status, raw data, parsed weight,
   stable/unstable, last received time, errors).
5. **Save Configuration → Activate**. Only one device configuration is ACTIVE at a time; every
   change is stored in configuration history and the audit log, and can be reviewed/restored.

## 4. Adding a Different Indicator Later (no code changes)
1. Duplicate an existing String Profile (or create new).
2. Adjust communication settings + parser fields per the new indicator's manual.
3. Test with sample frames → Activate. The application is fully device-independent.

## 5. Behaviour Guarantees
- Malformed / noisy / incomplete frames are **safely ignored** — never crash the app.
- Live weight processing runs on a background thread and never freezes the UI.
- Stable = same value for the configured `StableWeightDurationMs` (default 1500 ms).
- Weight is converted to **Quintal (2 decimals)** for all business logic; raw KG is preserved
  in `ScaleReadingGrossKg` / `ScaleReadingTareKg`.
- Auto-reconnect retries per configuration when the serial link drops.

## 6. Simulator (development / demo)
Developer Dashboard → Weighing Device → Simulator: generates genuine String Type 15 frames through
the same parser pipeline (ramp to target weight, jitter, stability). Use it to test Gross/Tare,
minimum-weight blocking and sound announcements without hardware.

## 7. Troubleshooting
| Symptom | Check |
|---|---|
| No data | COM port number, cable TX/RX swap, baud mismatch |
| Garbage characters | Baud/parity/data-bits mismatch |
| Weight always invalid | Wrong WeightStartPosition/WeightLength — use Test Parser with a captured frame |
| Frequent disconnects | USB adapter power saving (disable in Device Manager), cable quality |
| App shows Disconnected | Use Connect button; check indicator power; review audit/config history |
