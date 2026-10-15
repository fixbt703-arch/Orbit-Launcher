@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0OrbitClient\build.ps1"
if errorlevel 1 exit /b 1
echo Orbit Client is ready. Run build.bat to package the launcher.
pause
