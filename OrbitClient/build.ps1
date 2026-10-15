$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $candidates = @($env:JAVA_HOME, (Join-Path $env:LOCALAPPDATA 'OrbitLauncher\minecraft\runtime\windows-x64\java-runtime-epsilon'))
    $javaExe = $null
    foreach ($candidate in $candidates) {
        if (-not $candidate) { continue }
        $candidateJava = Join-Path $candidate 'bin\java.exe'
        if (-not (Test-Path -LiteralPath $candidateJava)) { continue }
        $previousErrorAction = $ErrorActionPreference
        try {
            # java -version writes its normal output to stderr, which Windows PowerShell
            # otherwise promotes to a terminating NativeCommandError under Stop.
            $ErrorActionPreference = 'Continue'
            $versionText = (& $candidateJava -version 2>&1 | Out-String)
        } finally {
            $ErrorActionPreference = $previousErrorAction
        }
        if ($versionText -match 'version "(\d+)' -and [int]$Matches[1] -ge 25) {
            $javaExe = $candidateJava
            break
        }
    }
    if (-not $javaExe) { throw 'Set JAVA_HOME to a JDK 25+ installation, or launch Minecraft 26.2 once through Orbit Launcher.' }
    & $javaExe -classpath 'gradle\wrapper\gradle-wrapper.jar' org.gradle.wrapper.GradleWrapperMain --no-daemon build
    if ($LASTEXITCODE -ne 0) { throw 'Orbit Client build failed.' }
    $destination = Join-Path $PSScriptRoot '..\Assets\OrbitClient'
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath 'build\libs\orbit-client-0.18.4.jar' -Destination (Join-Path $destination 'orbit-client-26.2.jar') -Force
    Write-Host 'Orbit Client built and copied into launcher assets.'
} finally { Pop-Location }
