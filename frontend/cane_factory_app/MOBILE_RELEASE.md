# Mobile release builds

## Android

For direct installation, build architecture-specific APKs. Do not distribute
the universal APK because it bundles native libraries for every CPU type.

```powershell
flutter clean
flutter pub get
flutter build apk --release --split-per-abi
```

Use `app-arm64-v8a-release.apk` for nearly all current physical Android phones.
Use `app-armeabi-v7a-release.apk` only for an older 32-bit ARM phone. The
`app-x86_64-release.apk` is intended for x64 Android devices/emulators.

For Google Play, upload an Android App Bundle. Play generates and delivers the
smallest compatible split automatically:

```powershell
flutter build appbundle --release
```

Release builds enable R8 code shrinking, resource shrinking, Dart tree shaking,
and Material icon tree shaking. Keep the generated mapping/symbol files with the
release when obfuscation is enabled in a future build.

## iOS / Apple mobile

iOS release builds require macOS, Xcode, an Apple Developer signing identity,
and an App Store provisioning profile. Validate compilation without signing:

```bash
flutter clean
flutter pub get
flutter build ios --release --no-codesign
```

Create the signed App Store/TestFlight IPA on the Mac:

```bash
flutter build ipa --release --export-method app-store
```

For direct registered-device distribution, use the appropriate provisioning
profile and build:

```bash
flutter build ipa --release --export-method ad-hoc
```
