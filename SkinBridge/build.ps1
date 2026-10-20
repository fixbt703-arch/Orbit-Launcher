[CmdletBinding()]
param([string]$JavaHome = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $JavaHome) { $JavaHome = Join-Path $env:LOCALAPPDATA 'OrbitLauncher\minecraft\runtime\windows-x64\java-runtime-epsilon' }
$java = Join-Path $JavaHome 'bin\java.exe'
$javac = Join-Path $JavaHome 'bin\javac.exe'
if (-not (Test-Path -LiteralPath $javac)) { throw 'A JDK 25+ is required to rebuild the skin bridge.' }
$deps = Join-Path $root '.deps'
$classes = Join-Path $root 'build\classes'
New-Item -ItemType Directory -Force -Path $deps,$classes | Out-Null
$dependencies = @{
    'asm' = '5fe3b469b8646e5bc53a897840952d63b712d4923d2a7a26aebc585f1fec7676'
    'asm-commons' = 'fcff37e6a0227a9f5ee81a7c07f51249307fa15889b56a359ae6f773c164c22b'
}
foreach ($name in $dependencies.Keys) {
    $path = Join-Path $deps "$name-9.11.jar"
    if (-not (Test-Path -LiteralPath $path)) { Invoke-WebRequest "https://repo.maven.apache.org/maven2/org/ow2/asm/$name/9.11/$name-9.11.jar" -OutFile $path -UseBasicParsing }
    $stream=[IO.File]::OpenRead($path); $sha=[Security.Cryptography.SHA256]::Create()
    try { $hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose();$stream.Dispose() }
    if ($hash -ne $dependencies[$name]) { throw "ASM checksum mismatch: $name" }
}
$classpath = (Join-Path $deps 'asm-9.11.jar')+';'+(Join-Path $deps 'asm-commons-9.11.jar')
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.java' -Recurse | ForEach-Object FullName)
& $javac --release 8 -encoding UTF-8 -cp $classpath -d $classes @sources
if ($LASTEXITCODE -ne 0) { throw 'Skin bridge compilation failed.' }
$output=Join-Path $root '..\Assets\SkinBridge\orbit-skin-bridge.jar'
& $java -cp $classpath (Join-Path $root 'tools\BuildAgent.java') $classes $output (Join-Path $deps 'asm-9.11.jar') (Join-Path $root 'LICENSE-ASM.txt') (Join-Path $root '..\LICENSE')
if ($LASTEXITCODE -ne 0) { throw 'Skin bridge packaging failed.' }
