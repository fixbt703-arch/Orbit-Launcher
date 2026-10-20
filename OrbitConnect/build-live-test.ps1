param([string]$JavaHome = '')
$ErrorActionPreference='Stop'
if (-not $JavaHome) { $JavaHome=Join-Path $env:LOCALAPPDATA 'OrbitLauncher\minecraft\runtime\windows-x64\java-runtime-epsilon' }
$minecraft=Join-Path $env:LOCALAPPDATA 'OrbitLauncher\minecraft'
$classes=Join-Path $PSScriptRoot 'build\live-classes'; New-Item -ItemType Directory -Force -Path $classes | Out-Null
$libraries=@(Get-ChildItem -LiteralPath (Join-Path $minecraft 'libraries') -Filter '*.jar' -Recurse | ForEach-Object FullName)
$cp=(Join-Path $minecraft 'versions\26.2\26.2.jar')+';'+($libraries -join ';')
$arguments='-encoding UTF-8 -classpath "'+$cp+'" -d "'+$classes+'" "'+(Join-Path $PSScriptRoot 'live-test\ConnectWorldSmoke.java')+'"'
$argsFile=Join-Path $PSScriptRoot 'build\live-javac.args'; [IO.File]::WriteAllText($argsFile,$arguments.Replace('\','/'))
& (Join-Path $JavaHome 'bin\javac.exe') "@$argsFile"
if ($LASTEXITCODE -ne 0) { throw 'Live fixture compilation failed' }
$metadata='{"schemaVersion":1,"id":"orbit-connect-live-test","version":"1.0.0","environment":"client","entrypoints":{"client":["ConnectWorldSmoke"]},"depends":{"fabricloader":">=0.19.5","minecraft":"26.2"}}'
[IO.File]::WriteAllText((Join-Path $classes 'fabric.mod.json'),$metadata)
& (Join-Path $JavaHome 'bin\jar.exe') cf (Join-Path $PSScriptRoot 'build\connect-live-test.jar') -C $classes .
if ($LASTEXITCODE -ne 0) { throw 'Live fixture packaging failed' }
