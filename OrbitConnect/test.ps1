param([string]$JavaHome = '', [string]$NettyVersion = '4.2.7.Final', [string]$RunJava = '')
$ErrorActionPreference='Stop'
if (-not $JavaHome) { $JavaHome=Join-Path $env:LOCALAPPDATA 'OrbitLauncher\minecraft\runtime\windows-x64\java-runtime-epsilon' }
$agent=Join-Path $PSScriptRoot '..\Assets\OrbitConnect\orbit-connect-agent.jar'
if (-not (Test-Path -LiteralPath $agent)) { throw 'Orbit Connect agent is missing' }
$nettyRoot=Join-Path $env:LOCALAPPDATA 'OrbitLauncher\minecraft\libraries\io\netty'
$versionPattern=[regex]::Escape($NettyVersion)
$jars=@(Get-ChildItem -LiteralPath $nettyRoot -Filter '*.jar' -Recurse | Where-Object { $_.FullName -match $versionPattern } | ForEach-Object FullName)
if (-not $jars.Count) { throw "Cached Netty $NettyVersion missing" }
$classes=Join-Path $PSScriptRoot 'build\test-classes'
New-Item -ItemType Directory -Force -Path $classes | Out-Null
$cp=$agent+';'+($jars -join ';')
$sources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.java' -Recurse | ForEach-Object FullName)
& (Join-Path $JavaHome 'bin\javac.exe') --release 8 -encoding UTF-8 -cp $cp -d $classes @sources
if ($LASTEXITCODE -ne 0) { throw 'Agent test compilation failed' }
if (-not $RunJava) { $RunJava=Join-Path $JavaHome 'bin\java.exe' }
& $RunJava "-javaagent:$agent" -cp ($classes+';'+$cp) AgentSmoke
if ($LASTEXITCODE -ne 0) { throw 'Agent smoke failed' }
