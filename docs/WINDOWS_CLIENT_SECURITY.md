# Windows client storage and trust

The portable Windows client does not write configuration, authentication data,
printer state, or cache files beside `cane_factory_app.exe`.

| Data | Windows location |
| --- | --- |
| Shared preferences (theme, camera-edge URL, printer TOF state) | `%APPDATA%\com.affllp.canefactory\cane_factory_app` |
| Access and refresh tokens | Windows Credential Manager via `flutter_secure_storage` |
| Temporary PDF used for printing | `%TEMP%` and deleted after printing |
| Operator-requested PDF/XLSX exports | Documents or Downloads |

Every Windows artifact contains `run.bat`. On first run it removes the
Mark-of-the-Web from files in that artifact and launches the application. It
does not request elevation, disable Microsoft Defender, add antivirus
exclusions, or grant write access to the installation directory.

## Production trust

AppData storage and `Unblock-File` do not give an unsigned executable publisher
reputation. Production releases should be Authenticode-signed with a trusted
organization code-signing certificate and timestamped. Sign the final EXE (and
preferably installer/MSIX). When the certificate is installed in the Windows
certificate store, the build script can sign automatically:

```powershell
.\tools\build-windows-client.ps1 `
  -ApiBaseUrl https://api.example.com `
  -CodeSigningCertificateThumbprint YOUR_CERTIFICATE_THUMBPRINT `
  -OutputDirectory artifacts\WINDOWS_SIGNED
```

Then verify:

```powershell
Get-AuthenticodeSignature .\cane_factory_app.exe | Format-List Status,SignerCertificate
```

Do not solve reputation warnings with `Add-MpPreference -ExclusionPath` or
`icacls ... /grant Users:F`. Those changes weaken endpoint protection and do
not reliably override Smart App Control or WDAC policy.
