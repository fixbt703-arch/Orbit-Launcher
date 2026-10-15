@echo off
setlocal
pushd "%~dp0"
set "ORBIT_DOTNET=C:\Program Files\dotnet\dotnet.exe"
if not exist "%ORBIT_DOTNET%" set "ORBIT_DOTNET=dotnet"
"%ORBIT_DOTNET%" publish OrbitLauncher.csproj -c Release -r win-x64 --self-contained true --nologo -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o dist
set "ORBIT_BUILD_RESULT=%errorlevel%"
popd
exit /b %ORBIT_BUILD_RESULT%
