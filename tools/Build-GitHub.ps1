[CmdletBinding()]
param(
    [switch]$NoPause,
    [string]$PublicClientId = '0df6579a-f033-4283-97ae-83b4ff0d6cee',
    [string]$InnoCompiler = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$projectParent = Split-Path -Parent $projectRoot
$exportName = (Split-Path -Leaf $projectRoot) + '-GitHub'
$destination = [IO.Path]::GetFullPath((Join-Path $projectParent $exportName))
$ownerFile = '.orbit-github-export'
$ownerText = 'Orbit Launcher public export v1'
$tempParent = $projectParent
$work = Join-Path $tempParent ('.orbit-gh-' + [Guid]::NewGuid().ToString('N').Substring(0,16))
$package = Join-Path $work 'package'
$source = Join-Path $package 'source'
$release = Join-Path $package 'release'
$published = $false
$movedPrevious = $false
$previous = Join-Path $work 'previous-export'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Write-Utf8([string]$Path, [string]$Text) {
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}
function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}
function Assert-NoLink([string]$Path) {
    if ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Links and junctions are not allowed in a public export: $Path"
    }
}
function Assert-ExportTarget {
    $resolved = [IO.Path]::GetFullPath($destination).TrimEnd('\')
    $expected = [IO.Path]::GetFullPath((Join-Path $projectParent $exportName)).TrimEnd('\')
    if ($resolved -ne $expected -or (Split-Path -Parent $resolved) -ne $projectParent -or $resolved -eq $projectRoot) {
        throw 'The export target is outside the expected sibling directory.'
    }
    Assert-NoLink $projectParent
    if (Test-Path -LiteralPath $destination) {
        Assert-NoLink $destination
        $marker = Join-Path $destination $ownerFile
        if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
            throw "Folder already exists and was not created by this script. Nothing was replaced: $destination"
        }
        Assert-NoLink $marker
        if ([IO.File]::ReadAllText($marker).Trim() -ne $ownerText) { throw 'Export ownership marker does not match.' }
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push($destination)
        while ($pending.Count -gt 0) {
            foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                Assert-NoLink $entry.FullName
                if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            }
        }
    }
}
function Assert-WorkChild([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($work + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to modify a path outside this build workspace.'
    }
}

try {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'OrbitLauncher.csproj'))) { throw 'Run this script from the Orbit Launcher project.' }
    $clientGuid = [Guid]::Empty
    if (-not [Guid]::TryParse($PublicClientId, [ref]$clientGuid) -or $clientGuid -eq [Guid]::Empty) { throw 'PublicClientId must be a non-empty Microsoft application GUID.' }
    Assert-NoLink $projectRoot
    Assert-ExportTarget
    $dotnet = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source }
    if (-not $InnoCompiler) {
        $innoCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($innoCommand) { $InnoCompiler = $innoCommand.Source }
        foreach ($programs in @([Environment]::GetFolderPath('ProgramFiles'),[Environment]::GetFolderPath('ProgramFilesX86'),(Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs'))) {
            foreach ($version in @('Inno Setup 7','Inno Setup 6')) {
                $candidate = Join-Path $programs ($version+'\ISCC.exe')
                if (-not $InnoCompiler -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { $InnoCompiler = $candidate }
            }
        }
    }
    if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) { throw 'Inno Setup compiler not found. Install Inno Setup 7 or pass -InnoCompiler <ISCC.exe>.' }
    $privateConfig = Join-Path $projectRoot 'Utilities\AppConfiguration.Local.cs'
    if (-not (Test-Path -LiteralPath $privateConfig -PathType Leaf)) { throw 'CurseForge requires Utilities\AppConfiguration.Local.cs for the release. This private file will not be exported.' }
    Assert-NoLink $privateConfig
    $null = New-Item -ItemType Directory -Path $source,$release -Force
    (Get-Item -LiteralPath $work -Force).Attributes = (Get-Item -LiteralPath $work -Force).Attributes -bor [IO.FileAttributes]::Hidden

    Write-Host '[1/7] Copying approved source files (no account data, caches or local keys)...'
    . (Join-Path $PSScriptRoot 'Export-PublicSource.ps1')
    $export = Export-OrbitPublicSource -ProjectRoot $projectRoot -Destination $source -PublicClientId $PublicClientId
    Write-Host '[2/7] Public source copied and audited.'

    Write-Host '[3/7] Building the release with local service credentials (not copied to source)...'
    $buildOutput = Join-Path $work 'build-output'
    $buildLog = Join-Path $work 'publish.log'
    $projectFile = Join-Path $source 'OrbitLauncher.csproj'
    $publicProject = [IO.File]::ReadAllText($projectFile)
    $include = [Security.SecurityElement]::Escape($privateConfig)
    $releaseProject = $publicProject.Replace('</Project>', ('  <ItemGroup><Compile Include="'+$include+'" Link="Utilities\AppConfiguration.Local.cs" /></ItemGroup>'+[Environment]::NewLine+'</Project>'))
    Write-Utf8 $projectFile $releaseProject
    Push-Location $source
    try {
        $oldPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            & $dotnet publish OrbitLauncher.csproj -c Release -r win-x64 --self-contained true --nologo `
                -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
                -p:EnableCompressionInSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
                -o $buildOutput *> $buildLog
            $buildExit = $LASTEXITCODE
        } finally { $ErrorActionPreference = $oldPreference }
    } finally { Pop-Location; Write-Utf8 $projectFile $publicProject }
    if ($buildExit -ne 0) { throw "Release build failed. Previous export was kept. Private build log: $buildLog" }

    Write-Host '[4/7] Checking the actual release payload and the live CurseForge API...'
    $probeRoot = Join-Path $work 'probe'
    $probeProject = Join-Path $probeRoot 'ReleaseProbe.csproj'
    Write-Utf8 $probeProject '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><Nullable>enable</Nullable></PropertyGroup></Project>'
    Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\Release-Probe.cs.txt') -Destination (Join-Path $probeRoot 'Program.cs')
    $assemblyPath = Join-Path $source 'bin\Release\net8.0-windows\win-x64\OrbitLauncher.dll'
    $payloadPath = Join-Path $buildOutput 'OrbitLauncher.exe'
    $probeResultPath = Join-Path $work 'probe-result.json'
    $probeLog = Join-Path $work 'probe.log'
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $dotnet run --project $probeProject -c Release -- $assemblyPath $payloadPath $probeResultPath $clientGuid.ToString() *> $probeLog
        $probeExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $oldPreference }
    if ($probeExit -ne 0) { Get-Content -LiteralPath $probeLog -Tail 5; throw 'Release credentials/API verification failed. Previous export was kept.' }
    $probeResult = [IO.File]::ReadAllText($probeResultPath) | ConvertFrom-Json
    if (-not $probeResult.apiVerified -or -not $probeResult.payloadVerified) { throw 'Release verification report is incomplete.' }

    Write-Host '[5/7] Compiling OrbitLauncherInstaler.exe...'
    $installerLog = Join-Path $work 'installer.log'
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $InnoCompiler ('--define=OrbitBuildDir='+$buildOutput) ('--output-dir='+$release) (Join-Path $source 'installer\OrbitLauncher.iss') *> $installerLog
        $installerExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $oldPreference }
    $installerPath = Join-Path $release 'OrbitLauncherInstaler.exe'
    if ($installerExit -ne 0 -or -not (Test-Path -LiteralPath $installerPath -PathType Leaf)) { Get-Content -LiteralPath $installerLog -Tail 12; throw 'Installer compilation failed. Previous export was kept.' }
    foreach ($dir in @('bin','obj')) {
        $generated = Join-Path $source $dir
        Assert-WorkChild $generated
        if (Test-Path -LiteralPath $generated) { Remove-Item -LiteralPath $generated -Recurse -Force }
    }

    Write-Host '[6/7] Verifying source isolation and writing installer checksums...'
    $sourceFiles = @(Get-ChildItem -LiteralPath $source -File -Recurse -Force)
    Assert-OrbitPublicSource -Source $source -ApprovedFiles $export.Files
    if ([IO.File]::ReadAllText($projectFile) -ne $publicProject) { throw 'Public project was not restored after the private release build.' }
    $installerHash = Get-Sha256 $installerPath
    $payloadHash = Get-Sha256 $payloadPath
    Write-Utf8 (Join-Path $release 'SHA256SUMS.txt') ($installerHash + '  OrbitLauncherInstaler.exe' + [Environment]::NewLine)
    $manifest = @($sourceFiles | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=$_.FullName.Substring($source.Length+1).Replace('\','/');sha256=(Get-Sha256 $_.FullName)}
    })
    $report = [ordered]@{schema=2;createdUtc=[DateTime]::UtcNow.ToString('o');sourceFiles=$sourceFiles.Count;publicMicrosoftClientId=$clientGuid.ToString();curseForgeKeyIncludedInSource=$false;curseForgeKeyIncludedInInstaller=$true;curseForgeApiVerified=$true;installerSha256=$installerHash;installedExeSha256=$payloadHash;files=$manifest}
    Write-Utf8 (Join-Path $package 'privacy-report.json') ($report | ConvertTo-Json -Depth 5)
    Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\GitHub-README.ru.txt') -Destination (Join-Path $package 'README.txt')
    Write-Utf8 (Join-Path $package $ownerFile) $ownerText

    Write-Host '[7/7] Replacing only the previous public export...'
    Assert-ExportTarget
    if (Test-Path -LiteralPath $destination) {
        Assert-WorkChild $previous
        Move-Item -LiteralPath $destination -Destination $previous
        $movedPrevious = $true
    }
    try {
        Assert-WorkChild $package
        Move-Item -LiteralPath $package -Destination $destination
        $published = $true
    } catch {
        if ($movedPrevious -and -not (Test-Path -LiteralPath $destination)) {
            Assert-ExportTarget
            Move-Item -LiteralPath $previous -Destination $destination
            $movedPrevious = $false
        }
        throw
    }
    Write-Host ''
    Write-Host ('Ready: ' + $destination) -ForegroundColor Green
    Write-Host 'Upload source/ to the repository; attach release/OrbitLauncherInstaler.exe to GitHub Releases.'
    Write-Host 'CurseForge API is verified and included in the installer; its key is excluded from source.'
    Write-Host 'Your project, accounts, Minecraft instances and personal launcher EXE were not cleaned.'
    # Only this GUID-named temporary workspace is removed, after successful publication.
    if ($published -and [IO.Path]::GetFullPath($work).StartsWith($tempParent+'\.orbit-gh-',[StringComparison]::OrdinalIgnoreCase)) {
        Assert-NoLink $work
        try { Remove-Item -LiteralPath $work -Recurse -Force }
        catch { Write-Warning ('Export is complete; temporary files could not be removed: ' + $work) }
    }
    exit 0
} catch {
    Write-Host ('ERROR: ' + $_.Exception.Message) -ForegroundColor Red
    if (Test-Path -LiteralPath $work) { Write-Host ('Temporary diagnostic files: ' + $work) }
    exit 1
}
