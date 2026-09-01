# CAMERA GUIDE — Vendor-Abstracted IP Camera Configuration

The camera architecture is **vendor-abstracted** (Hikvision, CP Plus, Dahua, Uniview/UNV,
Generic ONVIF, Generic RTSP). Configuration is fully database-driven — adding a compatible model
never requires code changes. Phase 5 ships configuration, encrypted credential storage, live-view
panel placement and reachability testing; actual snapshot capture pipelines (ONVIF/ISAPI/RTSP
providers) activate in Phase 6.

## Recommended Hikvision 4MP PoE models
- DS-2CD2046G2-IU, DS-2CD2146G2-ISU, DS-2CD1063G2-LIU (any compatible model is configurable)

## Configure (Developer Dashboard → Configuration → Cameras)
Per camera (1–6): Vendor, Model, Protocol (RTSP/ONVIF/ISAPI), IP, Port (554 RTSP / 80 ONVIF/ISAPI),
Username, Password (**AES-256-GCM encrypted; never returned to clients**), Channel, Stream type,
optional explicit RTSP URL, Capture Enabled, Live View Enabled, Retention days.

Typical RTSP URLs:
| Vendor | Main stream |
|---|---|
| Hikvision | `rtsp://user:pass@IP:554/Streaming/Channels/101` |
| CP Plus / Dahua | `rtsp://user:pass@IP:554/cam/realmonitor?channel=1&subtype=0` |
| Uniview | `rtsp://user:pass@IP:554/media/video1` |

## Global switches (System Settings)
- `CameraSystemEnabled` — master ON/OFF for the whole camera system
- `ImageCaptureEnabled` — global transaction-capture switch (OFF = no images captured or recorded
  for Gross/Tare/Payment evidence, while live preview may stay available)
Per-camera `CaptureEnabled` further limits capture to individually enabled cameras.

## Image storage (Phase 6 layout, already defined)
`D:\CanePaymentData\<Season>\Images\YYYY\MM\DD\PUR-<id>\GROSS-CAM01-01.jpg` etc.
Metadata (ImageId, PurchaseId, CameraId, CaptureStage, FileHash, CapturedAt/By) in SQL —
**images are never stored as database BLOBs**.

## Setup steps
1. Connect cameras to the factory LAN/PoE switch; assign static IPs (e.g. 192.168.1.61–66).
2. Set strong camera passwords in the camera's own admin UI first.
3. Enter details in Camera Configuration → **Test Connection** (TCP reachability) → Save.
4. Only cameras with Live View Enabled appear on the weighment form; Farmer role can never
   access factory-wide live cameras.
