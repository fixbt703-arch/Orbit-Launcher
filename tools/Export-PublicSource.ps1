Set-StrictMode -Version Latest

function Assert-OrbitNoLink([string]$Path) {
    if ((Get-Item -LiteralPath $Path -Force -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Links and junctions are not allowed in public source: $Path"
    }
}

function Assert-OrbitPublicSource([string]$Source, [string[]]$ApprovedFiles) {
    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\')
    Assert-OrbitNoLink $sourceRoot
    $actual = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -Force -File)
    foreach ($file in $actual) {
        Assert-OrbitNoLink $file.FullName
        $relative = $file.FullName.Substring($sourceRoot.Length + 1).Replace('\','/')
        if ($relative -notin $ApprovedFiles) { throw "Unexpected public file: $relative" }
        if ($relative -match '(?i)(^|/)(AutoSourceGitHub\.bat|build_github\.bat|AppConfiguration\.Local\.cs|accounts\.json|settings\.json|cmllib_msal_cache\.txt|\.env(?:\..*)?)$|(^|/)(run|bin|obj|dist|\.git|\.gradle|screenshots|logs|saves|crash-reports|Design)/|\.(exe|pfx|p12|pem|key|log|dmp|pdb|user|suo)$') {
            throw "Private or generated file in public source: $relative"
        }
        if ($file.Length -ge 95MB) { throw "File exceeds the source size limit: $relative" }
        if ($file.Extension -in @('.cs','.xaml','.json','.ps1','.bat','.md','.txt','.properties','.gradle','.example','.yml','.yaml')) {
            $text = [IO.File]::ReadAllText($file.FullName)
            if ($text -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\$2[aby]\$\d\d\$[A-Za-z0-9./]{53}|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}|(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{30,})') {
                throw "Credential-like content found; publication blocked: $relative"
            }
        }
    }
    if ($actual.Count -ne $ApprovedFiles.Count) { throw 'Public source is incomplete.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($file in $actual | Where-Object Extension -eq '.jar') {
        $jar = [IO.Compression.ZipFile]::OpenRead($file.FullName)
        try {
            foreach ($entry in $jar.Entries) {
                if ($entry.FullName -match '(?i)(^|/)(accounts\.json|settings\.json|cmllib_msal_cache\.txt|\.env|AppConfiguration\.Local\.cs)$|(^|/)(saves|crash-reports|screenshots)/') {
                    throw "Personal data entry found in public JAR: $($file.Name)"
                }
            }
        } finally { $jar.Dispose() }
    }
}

function Export-OrbitPublicSource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$ProjectRoot,
        [Parameter(Mandatory=$true)][string]$Destination,
        [string]$PublicClientId = '0df6579a-f033-4283-97ae-83b4ff0d6cee'
    )
    $projectRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
    $source = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
    $utf8 = New-Object Text.UTF8Encoding($false)
    $clientGuid = [Guid]::Empty
    if (-not [Guid]::TryParse($PublicClientId, [ref]$clientGuid) -or $clientGuid -eq [Guid]::Empty) { throw 'PublicClientId must be a non-empty GUID.' }
    if ($source -eq $projectRoot -or $source.StartsWith($projectRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Export must be outside the working project.' }
    Assert-OrbitNoLink $projectRoot
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'OrbitLauncher.csproj') -PathType Leaf)) { throw 'OrbitLauncher.csproj is missing.' }
    if (Test-Path -LiteralPath $source) {
        Assert-OrbitNoLink $source
        if (@(Get-ChildItem -LiteralPath $source -Force).Count) { throw 'Public export destination must be empty.' }
    } else { $null = New-Item -ItemType Directory -Path $source }
    $copied = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $blockedDirs = @('bin','obj','build','dist','run','.git','.gradle','.vs','.idea','node_modules','logs','screenshots','saves','crash-reports','downloads','cache','installer-staging','plugin-data')

    function Write-PublicUtf8([string]$Relative, [string]$Text) {
        $path = Join-Path $source $Relative
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force
        [IO.File]::WriteAllText($path,$Text,$utf8)
        $null = $copied.Add($Relative.Replace('\','/'))
    }
    function Copy-ApprovedFile([string]$Relative) {
        if ($Relative -match '(?i)(^|[\\/])(?:AutoSourceGitHub\.bat|build_github\.bat|AppConfiguration\.Local\.cs|accounts\.json|settings\.json|cmllib_msal_cache\.txt)$|(^|[\\/])\.env(?:\.|$)|\.(?:pfx|p12|pem|key|log|dmp|pdb|user|suo)$') { return }
        $inputFile = [IO.Path]::GetFullPath((Join-Path $projectRoot $Relative))
        if (-not $inputFile.StartsWith($projectRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid source path.' }
        $ancestor = $inputFile
        while ($ancestor.Length -ge $projectRoot.Length) { Assert-OrbitNoLink $ancestor; if ($ancestor -eq $projectRoot) { break }; $ancestor = Split-Path -Parent $ancestor }
        if ((Get-Item -LiteralPath $inputFile).Length -ge 95MB) { throw "Source file too large: $Relative" }
        $outputFile = Join-Path $source $Relative
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $outputFile) -Force
        Copy-Item -LiteralPath $inputFile -Destination $outputFile
        $null = $copied.Add($Relative.Replace('\','/'))
    }
    function Copy-ApprovedTree([string]$Relative, [string[]]$Extensions) {
        $root = Join-Path $projectRoot $Relative
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Required source folder missing: $Relative" }
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push($root)
        while ($pending.Count -gt 0) {
            $dir = $pending.Pop(); Assert-OrbitNoLink $dir
            foreach ($entry in Get-ChildItem -LiteralPath $dir -Force) {
                if ($entry.PSIsContainer) {
                    if ($blockedDirs -notcontains $entry.Name) { $pending.Push($entry.FullName) }
                } elseif ($Extensions -contains $entry.Extension -or $entry.Name -like 'LICENSE*') {
                    Copy-ApprovedFile $entry.FullName.Substring($projectRoot.Length + 1)
                }
            }
        }
    }
    $files = @('.gitignore','LICENSE','README.md','RELEASE_AUDIT.md','OrbitLauncher.csproj','app.ico','app.manifest','app.xaml',
        'App.xaml.cs','GlobalUsings.cs','mainwindow.xaml','MainWindow.xaml.cs','MainWindow.Updates.cs','MainWindow.Ely.cs','MainWindow.Connect.cs','ELY_SKINS.md','UPDATING.md','NETWORK_REFILL_FIXES.md','build.bat',
        'cleaning.bat','client-build.bat','sign-file.bat','Utilities\AppConfiguration.Local.cs.example',
        'installer\OrbitLauncher.iss','tools\Build-GitHub.ps1','tools\Export-PublicSource.ps1','tools\GitHub-README.ru.txt','tools\Release-Probe.cs.txt',
        'OrbitClient\build.gradle','OrbitClient\settings.gradle','OrbitClient\gradle.properties','OrbitClient\gradlew',
        'OrbitClient\gradlew.bat','OrbitClient\build.ps1','OrbitClient\libs\SKIN-LAYERS-LICENSE.txt',
        'OrbitClient\third-party\holdmyitems\build.gradle','OrbitClient\third-party\holdmyitems\LICENSE',
        'OrbitClient\third-party\holdmyitems\README.md','OrbitClient\third-party\holdmyitems\ORBIT-PORT.md',
        'OrbitClient\third-party\holdmyitems\libs\luaj-jse-3.0.1.jar')
    foreach ($file in $files) { Copy-ApprovedFile $file }
    foreach ($dir in @('Controls','Dialogs','Models','Services','Utilities','CompilerSupport')) { Copy-ApprovedTree $dir @('.cs','.xaml') }
    Copy-ApprovedTree 'themes' @('.xaml')
    foreach ($asset in @('cave.png','fight.png','forest.png','ghast.png','nether.png','orbitlauncher.png','space.png','village.png','water.png',
        'OrbitClient\components.json','OrbitClient\orbit-client-26.2.jar','SkinBridge\orbit-skin-bridge.jar','Icons\TABLER-LICENSE.txt')) { Copy-ApprovedFile ('Assets\'+$asset) }
    Copy-ApprovedTree 'SkinBridge\src' @('.java')
    Copy-ApprovedTree 'SkinBridge\tools' @('.java')
    Copy-ApprovedTree 'SkinBridge\tests' @('.java')
    Copy-ApprovedFile 'SkinBridge\build.ps1'
    Copy-ApprovedFile 'SkinBridge\LICENSE-ASM.txt'
    Copy-ApprovedFile 'SkinBridge\README.md'
    Copy-ApprovedTree 'Assets\OrbitConnect' @('.json','.txt','.jar')
    Copy-ApprovedTree 'OrbitConnect\src' @('.java')
    Copy-ApprovedTree 'OrbitConnect\tools' @('.java')
    Copy-ApprovedTree 'OrbitConnect\tests' @('.java')
    Copy-ApprovedTree 'OrbitConnect\live-test' @('.java')
    foreach ($file in @('README.md','compatibility.json','build.ps1','test.ps1','build-live-test.ps1')) { Copy-ApprovedFile ('OrbitConnect\'+$file) }
    Copy-ApprovedFile 'docs\ORBIT_CONNECT.md'
    Copy-ApprovedFile 'docs\ORBIT_CONNECT_ZAPRET.md'
    Copy-ApprovedFile 'docs\FINAL_AUDIT_2026-10-20.md'
    Copy-ApprovedTree 'Tests' @('.cs','.csproj')
    Copy-ApprovedTree 'OrbitClient\src' @('.java','.json','.png','.mcmeta')
    Copy-ApprovedTree 'OrbitClient\tools' @('.java','.ps1')
    Copy-ApprovedTree 'OrbitClient\gradle' @('.jar','.properties')
    Copy-ApprovedTree 'OrbitClient\third-party\holdmyitems\src' @('.java','.json','.json5','.lua','.rpo','.png','.mcmeta','.ogg','.properties','.jar')

    $configPath = Join-Path $source 'Utilities\AppConfiguration.cs'
    $config = [IO.File]::ReadAllText($configPath)
    $pattern = 'public string MicrosoftClientId \{ get; set; \} = (?:string\.Empty|"[0-9a-fA-F-]{36}");'
    if ([regex]::Matches($config,$pattern).Count -ne 1) { throw 'Public configuration format changed; review required.' }
    $config = [regex]::Replace($config,$pattern,('public string MicrosoftClientId { get; set; } = "'+$clientGuid.ToString()+'";'))
    if ($config -notmatch 'public string CurseForgeApiKey \{ get; set; \} = string\.Empty;') { throw 'CurseForge public default must be empty.' }
    Write-PublicUtf8 'Utilities\AppConfiguration.cs' $config
    Write-PublicUtf8 'OrbitClient\libs\README.md' @'
# Local-only build dependency

The Skin Layers 3D binary is intentionally excluded from this public source package.
To rebuild Orbit Client, obtain skinlayers3d-fabric-1.11.3-mc26.2.jar from the author
and place it here. Its license is included. Launcher builds use the already bundled
Orbit Client JAR and do not require this local dependency or Java.
'@
    $files = @($copied | Sort-Object)
    Assert-OrbitPublicSource $source $files
    return [pscustomobject]@{Root=$source;Files=$files;Count=$files.Count}
}
