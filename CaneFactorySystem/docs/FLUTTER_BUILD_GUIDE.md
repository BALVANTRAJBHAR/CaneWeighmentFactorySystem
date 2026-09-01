# FLUTTER BUILD GUIDE — Windows / Android / iOS / Web

All builds inject the API URL at compile time — no hard-coded endpoints or secrets:
```
--dart-define=API_BASE_URL=https://factory-server:5001
```

Before first build (generates platform folders):
```
cd frontend/cane_factory_app
flutter create . --platforms=windows,android,ios,web --project-name cane_factory_app
flutter pub get
```

---
## Windows Desktop (weighbridge operator PCs)
Requires Visual Studio 2022 with "Desktop development with C++".
```
flutter build windows --release --dart-define=API_BASE_URL=https://factory-server:5001
```
Output: `build\windows\x64\runner\Release\` — copy the whole folder to target PCs.
TTS uses the local Windows voices (install the Hindi voice: Settings → Time & Language →
Speech → Manage voices → add "Hindi (India)"). Works fully offline.

## Android (APK + AAB with R8 obfuscation)
1. Create a signing key (do NOT commit it):
   ```
   keytool -genkey -v -keystore %USERPROFILE%\cane-release.jks -keyalg RSA -keysize 2048 -validity 10000 -alias cane
   ```
2. Create `android/key.properties` (git-ignored):
   ```
   storePassword=<password>
   keyPassword=<password>
   keyAlias=cane
   storeFile=C:/Users/<you>/cane-release.jks
   ```
3. In `android/app/build.gradle` enable release signing + R8 minification:
   ```gradle
   buildTypes {
     release {
       signingConfig signingConfigs.release
       minifyEnabled true
       shrinkResources true
       proguardFiles getDefaultProguardFile('proguard-android-optimize.txt'), 'proguard-rules.pro'
     }
   }
   ```
4. Build with Dart obfuscation:
   ```
   flutter build apk --release --obfuscate --split-debug-info=build/symbols --dart-define=API_BASE_URL=https://factory-server:5001
   flutter build appbundle --release --obfuscate --split-debug-info=build/symbols --dart-define=API_BASE_URL=https://factory-server:5001
   ```
Outputs: `build/app/outputs/flutter-apk/app-release.apk`, `build/app/outputs/bundle/release/app-release.aab`.
Keep `build/symbols` privately for crash de-obfuscation.

## iOS (macOS required)
```
cd ios && pod install && cd ..
flutter build ipa --release --obfuscate --split-debug-info=build/symbols --dart-define=API_BASE_URL=https://factory-server:5001
```
Configure the signing team in Xcode (`ios/Runner.xcworkspace` → Signing & Capabilities).

## Web
```
flutter build web --release --dart-define=API_BASE_URL=https://factory-server:5001
```
Output: `build/web/` — host behind HTTPS (IIS/nginx). Add the web origin to the API's
`Cors:AllowedOrigins`. Web/mobile access requires network reachability to the API and is
read/report oriented for remote roles (Farmer etc.).

## Common Issues
| Problem | Fix |
|---|---|
| `flutter doctor` complains about VS | Install "Desktop development with C++" workload |
| Android licence errors | `flutter doctor --android-licenses` |
| API unreachable from emulator | Use `10.0.2.2` instead of `localhost` |
| CORS error on web | Add the exact web origin to `Cors:AllowedOrigins`, restart API |
| No Hindi TTS voice | Install the Hindi speech voice in Windows/Android settings |
