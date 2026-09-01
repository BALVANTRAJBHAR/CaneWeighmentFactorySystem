# CAMERA GUIDE — Vendor-Abstracted IP Camera Configuration

The camera architecture is **vendor-abstracted** (Hikvision, CP Plus, Dahua, Uniview/UNV,
Generic ONVIF, Generic RTSP). Configuration is fully database-driven — adding a compatible model
never requires code changes. Phase 6 activates real capture pipelines (ONVIF/ISAPI/RTSP) that
automatically photograph every Gross and Tare transaction.

## Recommended Hikvision 4MP PoE models
- DS-2CD2046G2-IU, DS-2CD2146G2-ISU, DS-2CD1063G2-LIU (any compatible model is configurable)

## Configure (Developer Dashboard → Configuration → Cameras)
Per camera (1–6): Vendor, Model, Protocol (RTSP/ONVIF/ISAPI), IP, Port (554 RTSP / 80 ONVIF/ISAPI),
Username, Password (**AES-256-GCM encrypted; never returned to clients**), Channel, Stream type,
optional explicit RTSP URL, Capture Enabled, Live View Enabled, Retention days. Use **Test**
(TCP reachability) and the new **Snapshot** button (one real live frame, not saved as evidence)
to verify a camera before relying on it for automatic capture.

Typical RTSP URLs:
| Vendor | Main stream |
|---|---|
| Hikvision | `rtsp://user:pass@IP:554/Streaming/Channels/101` |
| CP Plus / Dahua | `rtsp://user:pass@IP:554/cam/realmonitor?channel=1&subtype=0` |
| Uniview | `rtsp://user:pass@IP:554/media/video1` |

RTSP capture requires the **ffmpeg** binary installed and on PATH on the API server (Windows
factory server); ISAPI and ONVIF capture use plain HTTP/SOAP and need no extra binary.

## How capture works (Phase 6)
1. Saving Gross or Tare on the Weighment screen immediately returns to the operator
   (`captureQueued: true`); capture across all enabled cameras happens in the background so the
   scale/queue is never blocked by a slow or unreachable camera.
2. Each camera is captured independently — one camera failing/timing out never blocks the others
   or the weighment itself. Failures are recorded in the Audit Log (`ImageCaptureFailed`).
3. ISAPI: `GET http://IP:PORT/ISAPI/Streaming/channels/{channel}01/picture` (HTTP Basic/Digest).
   ONVIF: WS-Security `GetSnapshotUri` SOAP call to `/onvif/device_service` (override with a
   camera's `OnvifSettings` JSON: `{"mediaServiceUrl":"...","profileToken":"..."}` for
   non-standard models) followed by an HTTP GET of the returned URI.
   RTSP: `ffmpeg -rtsp_transport tcp -i <url> -frames:v 1 output.jpg`.
4. `POST /api/purchases/{id}/images/capture {"stage":"GROSS"|"TARE"}` lets an Operator/SalePurchase
   user manually (re-)trigger capture, e.g. after an auto-capture failure.
5. `GET /api/purchases/{id}/images` lists captured evidence metadata (requires `Image.View` —
   Admin/Developer by default per the role matrix); `GET /api/images/{id}/file` streams the JPEG.

## Global switches (System Settings)
- `CameraSystemEnabled` — master ON/OFF for the whole camera system
- `ImageCaptureEnabled` — global transaction-capture switch (OFF = no images captured or recorded
  for Gross/Tare/Payment evidence, while live preview may stay available)
Per-camera `CaptureEnabled` further limits capture to individually enabled cameras.

## Image storage (already defined)
`{ImageStorageRoot}\<Season>\Images\YYYY\MM\DD\PUR-<id>\GROSS-CAM01-01.jpg` etc. `ImageStorageRoot`
defaults to `D:\CanePaymentData` (Developer Dashboard → System Settings) and can be overridden
per environment with `CANE_IMAGE_STORAGE_ROOT` (useful for dev/test containers).
Metadata (ImageId, PurchaseId, CameraId, CaptureStage, FileHash, CapturedAt/By) in SQL —
**images are never stored as database BLOBs**.

## Simulator mode (no physical camera hardware)
Set `CANE_CAMERA_SIMULATOR=true` to make every capture (auto or manual) return a fixed placeholder
JPEG instead of contacting real hardware — exercises the full save/hash/DB/serve pipeline for
demos and CI without cameras. **Never set this to true in production.**

## Setup steps
1. Connect cameras to the factory LAN/PoE switch; assign static IPs (e.g. 192.168.1.61–66).
2. Set strong camera passwords in the camera's own admin UI first.
3. Enter details in Camera Configuration → **Test Connection** (TCP reachability) → **Snapshot**
   (real frame preview) → Save.
4. Only cameras with Live View Enabled appear on the weighment form; Farmer role can never
   access factory-wide live cameras.

