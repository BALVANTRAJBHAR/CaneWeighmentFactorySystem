# Public inquiries, SMTP and attachment scanning

The public web page opens without login. Its Home, Services, Request Quotation,
Contact and About Us links are available to visitors. Successful login still
uses the existing role-aware dashboard and server-side permission checks.

## Company mail settings

Log in as a user with **Company.Configure**, open **Configuration > Company**,
then save:

- **Contact Email / Gmail**: recipient for factory and general requests.
- **Developer Email / Gmail**: recipient for Developer / Software inquiries.
- **SMTP Host**: your mail provider's SMTP host (Gmail: `smtp.gmail.com`).
- **SMTP Port**: `587` for STARTTLS.
- **SMTP Username / Sender Email**: the account that sends the message.
- **SMTP Password / App Password**: an SMTP or Gmail app password; enter it
  only when changing it. It is encrypted on the server and never returned by
  the configuration API.

For Gmail, enable two-step verification on the sending account and generate an
app password. A normal Google account password is not accepted for SMTP. The
server must be able to reach the SMTP host on port 587. Send a request without
an attachment first to verify delivery. No email is sent when configuration is
missing; the form shows an error.

## Attachment scanner

### Current deployment and setup

The API contains an optional ClamAV client, but **no ClamAV daemon is installed
or configured**. Scanning is currently off by default. `QuotationScanner__Host`
and `QuotationScanner__Port` are absent from the current cloud App Service
settings. The local Windows machine also has no ClamAV daemon listening on
port 3310. Setting the two variables alone does not install or start it.
Uploads proceed without ClamAV while scanning is off; size, type, byte-signature,
image-decoding/re-encoding and basic PDF checks still apply. These checks do not
establish that a document is malware-free. SMTP and a recipient are still
required to actually send a quotation email.

For an API and scanner on the **same Windows/LAN machine**, install and run a
private `clamd` service or container with current `freshclam` signatures and
bind its TCP port to `127.0.0.1:3310`. If Docker is available, the official
ClamAV container can be started with a loopback-only port mapping:

```powershell
docker run -d --name cane-clamav --restart unless-stopped -p 127.0.0.1:3310:3310 clamav/clamav:stable
docker ps --filter name=cane-clamav
Test-NetConnection 127.0.0.1 -Port 3310
```

Allow time and memory for the signature database to download and the daemon
to become healthy. On the API host, set machine or IIS process environment
variables `QuotationScanner__Host=127.0.0.1` and
`QuotationScanner__Port=3310`, then restart the API. If the scanner is on
another LAN host, use its private address and allow port 3310 only from the
API host. Never publish port 3310 to the internet.

The current Azure API is on **F1 Free** and has no VNet integration. It cannot
reach a private Azure scanner simply by adding these settings. To use a
private Azure VM/container scanner, first move the App Service plan to Basic
or higher, integrate the API with a VNet subnet in the same region, run the
scanner on a private address accessible from that subnet, and restrict TCP
3310 to the API integration subnet. This changes Azure charges. Then open
Azure Portal > App Services > `authenticfoodfarmApi` > Settings > Environment
variables > App settings and add:

```text
QuotationScanner__Enabled = true
QuotationScanner__Host = <scanner-private-IP-or-DNS-name>
QuotationScanner__Port = 3310
```

Apply the settings, wait for the App Service restart, test connectivity from
the API environment, then test a clean PDF/JPG and a harmless antivirus test
signature in staging. The host value must be a host/IP, without `http://` or
`https://`. This protocol is raw TCP without built-in authentication or TLS.

Anonymous requests accept one PDF or JPG, at most 1,000,000 bytes. The server
checks the extension and file signature, rejects active PDF features, decodes
and re-encodes JPG to strip metadata and compress it, and scans the resulting
bytes with ClamAV **when enabled**. With scanning off, the basic checks still
run and attachments can be sent. Once scanning is enabled, an unavailable
scanner fails closed until service returns. Requests without attachments do
not need the scanner.

Run `clamd` on a private host/network, then set the API server configuration:

```json
"QuotationScanner": {
  "Enabled": true,
  "Host": "private-clamav-host",
  "Port": 3310
}
```

The `clamd` INSTREAM port must not be exposed publicly. Keep ClamAV signatures
updated and restrict access to the API host. The API's example production
settings include these keys. A scanner reduces risk but cannot guarantee that
every malicious file is detected; mail recipients should still treat outside
attachments as untrusted.

## Other outbound email and MFA

Company SMTP sends quotation requests and may send password-reset codes when
the configured SMS provider fails. Staff login MFA is a separate optional
feature and remains off (`Security__StaffMfaEnabled=false`) by the owner's
decision. A normal staff login does not send an OTP. A Forgot Password flow
may send a reset code; it does not enable login MFA. The cloud Company SMTP
and quotation recipient fields were empty at the last configuration check.

## Windows icon packaging

Use `tools/build-windows-client.ps1` for cloud Windows packages. It now builds
with `--no-tree-shake-icons` so the complete Material icon font is included.
Copy the **entire** generated Windows package, including `data/flutter_assets`;
copying only the EXE will leave fonts and other assets unavailable.
