@echo off
setlocal
pushd "%~dp0"

rem Remove only the Internet download marker from files in this trusted client
rem package. This does not disable Microsoft Defender or weaken folder ACLs.
powershell.exe -NoLogo -NoProfile -NonInteractive -Command ^
  "Get-ChildItem -LiteralPath '%~dp0' -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue"

if not exist "%~dp0cane_factory_app.exe" (
  echo cane_factory_app.exe was not found in this folder.
  pause
  popd
  exit /b 1
)

start "" "%~dp0cane_factory_app.exe"
popd
exit /b 0
