@echo off
setlocal EnableExtensions DisableDelayedExpansion
title Orbit Launcher - Personal Data Cleanup
cd /d "%~dp0"

set "PROJECT_ROOT=%~dp0"
set "APP_DATA=%LOCALAPPDATA%\OrbitLauncher"
set "MSAL_CACHE=%LOCALAPPDATA%\cmllib_msal_cache.txt"
set "IMPORT_TEMP=%TEMP%\OrbitLauncher"

echo.
echo  ========================================
echo       ORBIT LAUNCHER - CLEANING
echo  ========================================
echo.

if not exist "%PROJECT_ROOT%OrbitLauncher.csproj" goto :wrong_directory
if not exist "%PROJECT_ROOT%Utilities\AppConfiguration.cs" goto :wrong_directory
if not defined LOCALAPPDATA goto :unsafe_path
if not defined TEMP goto :unsafe_path

echo This will remove:
echo   - Microsoft account data and the CmlLib token cache;
echo   - settings.json and the Orbit Launcher cache;
echo   - locally saved skin library;
echo   - legacy plugin files and locally collected analytics, if present;
echo   - every local Orbit Launcher Minecraft instance;
echo   - bin, obj, dist, installer-staging, .tools and .vs;
echo   - temporary Orbit Launcher import files.
echo.
echo This will NOT remove:
echo   - source code;
echo   - Utilities\AppConfiguration.cs;
echo   - Utilities\AppConfiguration.Local.cs;
echo   - Client ID, API keys or API addresses stored in source files.
echo.

if /i "%~1"=="--yes" goto :clean
set "CLEAN_CONFIRM="
echo Press Y to continue. Any other key cancels without making changes.
for /F "delims=" %%K in ('powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$key = [Console]::ReadKey($true); if ($key.Key -eq [ConsoleKey]::Y -or [int]$key.KeyChar -in 89, 121, 1053, 1085) { 'Y' } else { 'N' }"') do set "CLEAN_CONFIRM=%%K"
if /i not "%CLEAN_CONFIRM%"=="Y" goto :cancelled

:clean
echo.
echo [1/4] Closing Orbit Launcher...
if /i "%ORBIT_CLEANING_TEST%"=="1" goto :launcher_closed
taskkill /IM OrbitLauncher.exe /T /F >nul 2>nul
:launcher_closed

echo [2/4] Removing account data, settings and local instances...
if exist "%APP_DATA%\accounts.json" del /F /Q "%APP_DATA%\accounts.json"
if exist "%APP_DATA%\settings.json" del /F /Q "%APP_DATA%\settings.json"
if exist "%MSAL_CACHE%" del /F /Q "%MSAL_CACHE%"
if exist "%APP_DATA%\cache" rmdir /S /Q "%APP_DATA%\cache"
if exist "%APP_DATA%\skins" rmdir /S /Q "%APP_DATA%\skins"
if exist "%APP_DATA%\plugins" rmdir /S /Q "%APP_DATA%\plugins"
if exist "%APP_DATA%\plugin-data" rmdir /S /Q "%APP_DATA%\plugin-data"
if exist "%APP_DATA%\instances" rmdir /S /Q "%APP_DATA%\instances"
if exist "%APP_DATA%\minecraft" rmdir /S /Q "%APP_DATA%\minecraft"
if exist "%IMPORT_TEMP%" rmdir /S /Q "%IMPORT_TEMP%"

echo [3/4] Removing build output...
if exist "%PROJECT_ROOT%bin" rmdir /S /Q "%PROJECT_ROOT%bin"
if exist "%PROJECT_ROOT%obj" rmdir /S /Q "%PROJECT_ROOT%obj"
if exist "%PROJECT_ROOT%dist" rmdir /S /Q "%PROJECT_ROOT%dist"
if exist "%PROJECT_ROOT%installer-staging" rmdir /S /Q "%PROJECT_ROOT%installer-staging"
if exist "%PROJECT_ROOT%.tools" rmdir /S /Q "%PROJECT_ROOT%.tools"
if exist "%PROJECT_ROOT%.vs" rmdir /S /Q "%PROJECT_ROOT%.vs"

echo [4/4] Verifying preserved build configuration...
if not exist "%PROJECT_ROOT%Utilities\AppConfiguration.cs" goto :configuration_missing
if exist "%PROJECT_ROOT%Utilities\AppConfiguration.Local.cs" (
    echo Local Client ID and API keys were preserved.
) else (
    echo No local key file existed before cleanup; the example file was preserved.
)

echo.
echo Cleanup completed. The project is ready for a clean build or publication.
echo.
if /i not "%~1"=="--yes" pause
exit /b 0

:cancelled
echo.
echo Cleanup cancelled. No files were changed.
exit /b 0

:wrong_directory
echo [ERROR] cleaning.bat must be located next to OrbitLauncher.csproj.
pause
exit /b 1

:unsafe_path
echo [ERROR] Windows did not provide safe LOCALAPPDATA or TEMP paths.
pause
exit /b 1

:configuration_missing
echo [ERROR] Utilities\AppConfiguration.cs is missing after cleanup.
pause
exit /b 1
