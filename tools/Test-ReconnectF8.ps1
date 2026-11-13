param(
    [string]$DevelopmentRoot = 'F:/projectFile/RMPStable-development',
    [string]$GamePack = 'F:/SteamLibrary/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck',
    [string]$Candidate
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($DevelopmentRoot)
$repo = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $root 'runtime/v0.113.0'
$exe = Join-Path $runtime 'SlayTheSpire2.exe'
$source = Join-Path $repo 'tests/ReconnectF8Probe'
if (!$Candidate) { $Candidate = Join-Path $root 'build/v047/package/RMPStable' }
$probeBuild = Join-Path $root 'build/v047/reconnect-probe'
$version = (Get-Content -LiteralPath (Join-Path $runtime 'release_info.json') -Raw | ConvertFrom-Json).version
if ($version -ne 'v0.113.0' -or !(Test-Path -LiteralPath $GamePack -PathType Leaf)) { throw 'The isolated beta runtime or matching PCK is missing.' }
if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) { throw 'The isolated runtime is already running.' }

& dotnet build (Join-Path $source 'ReconnectF8Probe.csproj') -c Release --configfile (Join-Path $repo 'RMPStable/src/NuGet.Config') `
    "-p:GameManagedDir=$runtime/data_sts2_windows_x86_64" "-p:BaseIntermediateOutputPath=$probeBuild/obj/" --output $probeBuild
if ($LASTEXITCODE -ne 0) { throw 'Reconnect probe build failed.' }

$probeMod = Join-Path $runtime 'mods/ReconnectF8Probe'
$modDir = Join-Path $runtime 'mods/RMPStable'
New-Item -ItemType Directory -Force -Path $probeMod, $modDir | Out-Null
Copy-Item -LiteralPath (Join-Path $probeBuild 'ReconnectF8Probe.dll'), (Join-Path $source 'ReconnectF8Probe.json') -Destination $probeMod -Force
Get-ChildItem -LiteralPath $Candidate -File | Copy-Item -Destination $modDir -Force

$evidence = Join-Path $root ('evidence/reconnect-f8-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$modHash = (Get-FileHash -LiteralPath (Join-Path $Candidate 'RMPStable.dll')).Hash
function New-CaseStart([string]$case, [string]$resultPath) {
        $profile = Join-Path $case 'profile'
        New-Item -ItemType Directory -Path $case -Force | Out-Null
        foreach ($relative in @(
            'Roaming/SlayTheSpire2/default/1/settings.save',
            'Roaming/SlayTheSpire2/default/1/modded/profile1/saves/progress.save',
            'Roaming/SlayTheSpire2/default/1/modded/profile1/saves/prefs.save',
            'Roaming/SlayTheSpire2/default/1/profile1/saves/progress.save',
            'Roaming/SlayTheSpire2/default/1/profile1/saves/prefs.save'
        )) {
            $destination = Join-Path $profile $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
            Copy-Item -LiteralPath (Join-Path $root "profile-seed/$relative") -Destination $destination
        }
        $settingsPath = Join-Path $profile 'Roaming/SlayTheSpire2/default/1/settings.save'
        $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        $settings.mod_settings.mod_list = @(
            @{id='ReconnectF8Probe';is_enabled=$true;source='mods_directory'},
            @{id='RMPStable';is_enabled=$true;source='mods_directory'}
        )
        $settings.skip_intro_logo = $true
        $settings.keyboard_mapping.rmpLiveSharing = 'F8'
        $settings.volume_master = 0
        $settings.fullscreen = $false
        $settings.fps_limit = 120
        $settings.limit_fps_in_background = $false
        $settings | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $settingsPath -Encoding utf8

        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.WorkingDirectory = $runtime
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        $start.Environment['APPDATA'] = Join-Path $profile 'Roaming'
        $start.Environment['LOCALAPPDATA'] = Join-Path $profile 'Local'
        foreach ($key in @($start.Environment.Keys)) { if ($key.StartsWith('RMP_')) { $start.Environment.Remove($key) | Out-Null } }
        $start.Environment['RMP_047_PROBE'] = $resultPath
        $start.Environment['RMP_MULTI_MAIN_PACK'] = $GamePack
        foreach ($argument in @('--main-pack', $GamePack, '--force-steam', 'off', '--windowed', '--resolution', '1280x720', '--max-fps', '120', '--rendering-method', 'gl_compatibility', '--log-file', (Join-Path $case 'source.log'))) {
            $start.ArgumentList.Add($argument)
        }
        return $start
}

function Clear-CaseProfile([string]$case) {
    $profile = Join-Path $case 'profile'
    $resolved = (Resolve-Path -LiteralPath $profile).Path
    if (!$resolved.StartsWith([IO.Path]::GetFullPath($case) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected test profile path.' }
    if ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint -or
        (Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) { throw 'Test profile contains a reparse point.' }
    Get-ChildItem -LiteralPath $resolved -Filter renderer.log -Recurse -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $case ('renderer-' + $_.Directory.Name + '.log'))
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Wait-ReplicaExit {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Replica still exiting. Evidence: $evidence" }
        Start-Sleep -Milliseconds 250
    }
}


$processes = @()
$cases = @()
Write-Output "Evidence: $evidence"
try {
    foreach ($role in @('host', 'client')) {
        $case = Join-Path $evidence $role
        $start = New-CaseStart $case (Join-Path $evidence "$role.json")
        $start.Environment['RMP_047_ROLE'] = $role
        $cases += $case
        $processes += [Diagnostics.Process]::Start($start)
    }
    foreach ($owned in $processes) {
        if (!$owned.WaitForExit(230000) -or $owned.ExitCode -ne 0) {
            Get-ChildItem -LiteralPath $evidence -Filter '*.error' | Get-Content
            throw "Reconnect probe failed. Evidence: $evidence"
        }
    }
    foreach ($case in $cases) {
        if (Select-String -LiteralPath (Join-Path $case 'source.log') -Quiet -Pattern 'Could not find connection for peer|Tried to send message to client with ID.*no client|\[RMP:Mirror\] System\.') {
            throw "Unexpected mirror exception or send to an offline peer. Evidence: $case"
        }
    }
    $hostResult = Get-Content -LiteralPath (Join-Path $evidence 'host.json') -Raw | ConvertFrom-Json
    $clientResult = Get-Content -LiteralPath (Join-Path $evidence 'client.json') -Raw | ConvertFrom-Json
    $summary = @{modSha256=$modHash; game=$version; host=$hostResult; client=$clientResult}
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    $summary | ConvertTo-Json -Depth 6
} finally {
    foreach ($owned in $processes) {
        if (!$owned.HasExited) { $owned.Kill() }
        $owned.Dispose()
    }
    Wait-ReplicaExit
    foreach ($case in $cases) { Clear-CaseProfile $case }
    foreach ($file in @('ReconnectF8Probe.dll', 'ReconnectF8Probe.json')) {
        Remove-Item -LiteralPath (Join-Path $probeMod $file) -ErrorAction SilentlyContinue
    }
}
