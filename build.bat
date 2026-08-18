@echo off
setlocal EnableExtensions
title Orbit Launcher - Build
cd /d "%~dp0"

set "PROJECT=%~dp0OrbitLauncher.csproj"
set "OUTPUT=%~dp0dist"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"

echo.
echo  ========================================
echo         ORBIT LAUNCHER - BUILD
echo  ========================================
echo.

set "DOTNET_EXE="
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"

if not defined DOTNET_EXE (
    for /f "delims=" %%D in ('where dotnet 2^>nul') do if not defined DOTNET_EXE set "DOTNET_EXE=%%D"
)

if not defined DOTNET_EXE goto :sdk_missing

if not exist "%PROJECT%" (
    echo [ERROR] OrbitLauncher.csproj was not found next to build.bat.
    goto :failed
)

set "SDK_LIST=%TEMP%\orbit-launcher-sdks-%RANDOM%.txt"
"%DOTNET_EXE%" --list-sdks > "%SDK_LIST%" 2>nul
findstr /R /C:"^[0-9][0-9]*\." "%SDK_LIST%" >nul 2>nul
if errorlevel 1 (
    del /q "%SDK_LIST%" >nul 2>nul
    goto :sdk_missing
)

echo Detected SDK:
type "%SDK_LIST%"
del /q "%SDK_LIST%" >nul 2>nul
echo.

echo [1/5] Cleaning old build files...
if exist "%~dp0bin" rmdir /s /q "%~dp0bin"
if exist "%~dp0obj" rmdir /s /q "%~dp0obj"
if not exist "%OUTPUT%" mkdir "%OUTPUT%"
if exist "%OUTPUT%\OrbitLauncher.exe" del /q "%OUTPUT%\OrbitLauncher.exe"

echo [2/5] Restoring NuGet packages...
"%DOTNET_EXE%" restore "%PROJECT%" --nologo
if errorlevel 1 goto :failed
if not exist "%~dp0obj\project.assets.json" goto :failed

echo [3/5] Building an uncompressed single Windows EXE...
"%DOTNET_EXE%" publish "%PROJECT%" -c Release -r win-x64 --self-contained true --no-restore --nologo -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=None -p:DebugSymbols=false -p:Deterministic=true -p:ContinuousIntegrationBuild=true -o "%OUTPUT%"
if errorlevel 1 goto :failed
if not exist "%OUTPUT%\OrbitLauncher.exe" goto :failed

for /r "%OUTPUT%" %%F in (*.pdb) do del /q "%%F" >nul 2>nul

echo [4/5] Checking Authenticode signing configuration...
call "%~dp0sign-file.bat" "%OUTPUT%\OrbitLauncher.exe"
if errorlevel 1 goto :failed

echo [5/5] Build completed successfully.
echo.
echo EXE: %OUTPUT%\OrbitLauncher.exe
echo.
pause
exit /b 0

:sdk_missing
echo [ERROR] .NET SDK was not found.
echo.
echo The Runtime alone is not enough to build Orbit Launcher.
echo Install the Windows x64 SDK, then restart Windows:
echo https://dotnet.microsoft.com/en-us/download/dotnet/8.0
echo.
echo After restarting, this command must show an 8.0.xxx entry:
echo dotnet --list-sdks
echo.
pause
exit /b 1

:failed
echo.
echo [ERROR] Build failed. Review the messages above.
echo.
pause
exit /b 1
