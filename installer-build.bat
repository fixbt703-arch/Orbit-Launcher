@echo off
setlocal EnableExtensions DisableDelayedExpansion
title Orbit Launcher - Installer Build
cd /d "%~dp0"

set "PROJECT=%~dp0OrbitLauncher.csproj"
set "SCRIPT=%~dp0installer\OrbitLauncher.iss"
set "STAGING=%~dp0installer-staging"
set "DIST=%~dp0dist"
set "INSTALLER=%DIST%\OrbitLauncher-installer.exe"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"

echo.
echo  ========================================
echo       ORBIT LAUNCHER - INSTALLER
echo  ========================================
echo.

set "DOTNET_EXE="
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET_EXE for /f "delims=" %%D in ('where dotnet.exe 2^>nul') do if not defined DOTNET_EXE set "DOTNET_EXE=%%D"
if not defined DOTNET_EXE goto :sdk_missing

set "ISCC_EXE="
if exist "%~dp0.tools\inno\ISCC.exe" set "ISCC_EXE=%~dp0.tools\inno\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles%\Inno Setup 7\ISCC.exe" set "ISCC_EXE=%ProgramFiles%\Inno Setup 7\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" set "ISCC_EXE=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
if not defined ISCC_EXE if exist "%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe" set "ISCC_EXE=%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do if not defined ISCC_EXE set "ISCC_EXE=%%I"
if not defined ISCC_EXE goto :inno_missing

if not exist "%PROJECT%" goto :files_missing
if not exist "%SCRIPT%" goto :files_missing
if not exist "%~dp0Assets\OrbitLauncher.ico" goto :files_missing

set "SDK_LIST=%TEMP%\orbit-launcher-installer-sdks-%RANDOM%.txt"
"%DOTNET_EXE%" --list-sdks > "%SDK_LIST%" 2>nul
findstr /R /C:"^[0-9][0-9]*\." "%SDK_LIST%" >nul 2>nul
if errorlevel 1 (
    del /q "%SDK_LIST%" >nul 2>nul
    goto :sdk_missing
)
del /q "%SDK_LIST%" >nul 2>nul

echo [1/7] Cleaning installer workspace...
if exist "%STAGING%" rmdir /s /q "%STAGING%"
if not exist "%DIST%" mkdir "%DIST%"
if exist "%INSTALLER%" del /q "%INSTALLER%"

echo [2/7] Restoring NuGet packages...
"%DOTNET_EXE%" restore "%PROJECT%" --nologo
if errorlevel 1 goto :failed

echo [3/7] Publishing self-contained application files...
"%DOTNET_EXE%" publish "%PROJECT%" -c Release -r win-x64 --self-contained true --no-restore --nologo -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -p:Deterministic=true -p:ContinuousIntegrationBuild=true -o "%STAGING%"
if errorlevel 1 goto :failed
if not exist "%STAGING%\OrbitLauncher.exe" goto :failed
for /r "%STAGING%" %%F in (*.pdb) do del /q "%%F" >nul 2>nul

echo [4/7] Checking application signing configuration...
call "%~dp0sign-file.bat" "%STAGING%\OrbitLauncher.exe"
if errorlevel 1 goto :failed

echo [5/7] Compiling Inno Setup installer...
"%ISCC_EXE%" /Qp "%SCRIPT%"
if errorlevel 1 goto :failed
if not exist "%INSTALLER%" goto :failed

echo [6/7] Checking installer signing configuration...
call "%~dp0sign-file.bat" "%INSTALLER%"
if errorlevel 1 goto :failed

echo [7/7] Removing temporary application files...
rmdir /s /q "%STAGING%"

echo.
echo Installer created successfully:
echo %INSTALLER%
echo.
for %%F in ("%INSTALLER%") do echo Size: %%~zF bytes
echo.
pause
exit /b 0

:inno_missing
echo [ERROR] Inno Setup 7 or 6 was not found.
echo.
echo Install the official x64 compiler and run this file again:
echo   winget install --id JRSoftware.InnoSetup.7 -e -s winget -i
echo.
echo Official download page:
echo   https://jrsoftware.org/isdl.php
start "" "https://jrsoftware.org/isdl.php"
pause
exit /b 1

:sdk_missing
echo [ERROR] .NET 8 SDK was not found.
echo Install the Windows x64 SDK from:
echo   https://dotnet.microsoft.com/download/dotnet/8.0
pause
exit /b 1

:files_missing
echo [ERROR] Installer source files are missing.
echo Check OrbitLauncher.csproj, installer\OrbitLauncher.iss and Assets\OrbitLauncher.ico.
pause
exit /b 1

:failed
echo.
echo [ERROR] Installer build failed. Review the messages above.
echo Temporary files were kept in installer-staging for diagnostics.
echo.
pause
exit /b 1
