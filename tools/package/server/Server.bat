@echo off
rem Starts the Pb dedicated server in this window (docs/hosting.md), after bringing it up to date with the latest
rem test build (update-server.ps1). Its settings are in server.jsonc beside this file. Close the window to stop it.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-server.ps1"
"%~dp0Pb.console.exe" --headless -- --server %*
pause
