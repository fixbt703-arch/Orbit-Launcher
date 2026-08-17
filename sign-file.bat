@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "TARGET_FILE=%~1"
if not defined TARGET_FILE (
    echo [ERROR] No file was provided for signing.
    exit /b 1
)
if not exist "%TARGET_FILE%" (
    echo [ERROR] File to sign was not found: %TARGET_FILE%
    exit /b 1
)

if not defined ORBIT_SIGN_PFX if not defined ORBIT_SIGN_SHA1 (
    echo Signing skipped: set ORBIT_SIGN_PFX or ORBIT_SIGN_SHA1 to sign release files.
    exit /b 0
)

set "SIGNTOOL_EXE="
for /f "delims=" %%S in ('where signtool.exe 2^>nul') do if not defined SIGNTOOL_EXE set "SIGNTOOL_EXE=%%S"
if not defined SIGNTOOL_EXE (
    echo [ERROR] signtool.exe was not found. Install the Windows SDK and add SignTool to PATH.
    exit /b 1
)

if not defined ORBIT_TIMESTAMP_URL set "ORBIT_TIMESTAMP_URL=http://timestamp.digicert.com"
if defined ORBIT_SIGN_PFX (
    if not exist "%ORBIT_SIGN_PFX%" (
        echo [ERROR] Signing certificate was not found: %ORBIT_SIGN_PFX%
        exit /b 1
    )
    if defined ORBIT_SIGN_PASSWORD (
        "%SIGNTOOL_EXE%" sign /fd SHA256 /td SHA256 /tr "%ORBIT_TIMESTAMP_URL%" /f "%ORBIT_SIGN_PFX%" /p "%ORBIT_SIGN_PASSWORD%" "%TARGET_FILE%"
    ) else (
        "%SIGNTOOL_EXE%" sign /fd SHA256 /td SHA256 /tr "%ORBIT_TIMESTAMP_URL%" /f "%ORBIT_SIGN_PFX%" "%TARGET_FILE%"
    )
) else (
    "%SIGNTOOL_EXE%" sign /fd SHA256 /td SHA256 /tr "%ORBIT_TIMESTAMP_URL%" /sha1 "%ORBIT_SIGN_SHA1%" "%TARGET_FILE%"
)
if errorlevel 1 exit /b 1

"%SIGNTOOL_EXE%" verify /pa "%TARGET_FILE%"
exit /b %ERRORLEVEL%
