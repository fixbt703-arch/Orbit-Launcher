[CmdletBinding()]
param([string]$JavaHome = '')
$ErrorActionPreference='Stop'
if (-not $JavaHome) { $JavaHome=Join-Path $env:LOCALAPPDATA 'OrbitLauncher\minecraft\runtime\windows-x64\java-runtime-epsilon' }
$deps=Join-Path $PSScriptRoot '..\SkinBridge\.deps'
$expected=@{'asm'='5fe3b469b8646e5bc53a897840952d63b712d4923d2a7a26aebc585f1fec7676';'asm-commons'='fcff37e6a0227a9f5ee81a7c07f51249307fa15889b56a359ae6f773c164c22b'}
foreach ($name in $expected.Keys) {
    $path=Join-Path $deps "$name-9.11.jar"
    if (-not (Test-Path -LiteralPath $path)) { New-Item -ItemType Directory -Force -Path $deps | Out-Null; Invoke-WebRequest "https://repo.maven.apache.org/maven2/org/ow2/asm/$name/9.11/$name-9.11.jar" -OutFile $path -UseBasicParsing }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected[$name]) { throw "ASM hash mismatch: $name" }
}
$cp=(Join-Path $deps 'asm-9.11.jar')+';'+(Join-Path $deps 'asm-commons-9.11.jar')
$classes=Join-Path $PSScriptRoot 'build\classes'; New-Item -ItemType Directory -Force -Path $classes | Out-Null
$sources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.java' -Recurse | ForEach-Object FullName)
& (Join-Path $JavaHome 'bin\javac.exe') --release 8 -encoding UTF-8 -cp $cp -d $classes @sources
if ($LASTEXITCODE -ne 0) { throw 'Connect agent compilation failed' }
& (Join-Path $JavaHome 'bin\java.exe') -cp $cp (Join-Path $PSScriptRoot 'tools\BuildAgent.java') $classes (Join-Path $PSScriptRoot '..\Assets\OrbitConnect\orbit-connect-agent.jar') (Join-Path $deps 'asm-9.11.jar') (Join-Path $PSScriptRoot '..\SkinBridge\LICENSE-ASM.txt') (Join-Path $PSScriptRoot '..\LICENSE')
if ($LASTEXITCODE -ne 0) { throw 'Connect agent packaging failed' }
