param(
    [string]$DevelopmentRoot = 'F:/projectFile/RMPStable-development',
    [string]$GamePack = 'F:/SteamLibrary/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck',
    [ValidateRange(1, 20)][int]$Repetitions = 3,
    [switch]$CheckReplica,
    [ValidateSet('Standard', 'Blitz', 'Dread')][string]$MultiplayerMode
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($DevelopmentRoot)
$repo = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $root 'runtime/v0.113.0'
$exe = Join-Path $runtime 'SlayTheSpire2.exe'
$source = Join-Path $repo 'tests/Beta113StartupProbe'
$candidate = Join-Path $root 'build/v046/package/RMPStable'
$probeBuild = Join-Path $root 'build/v046/startup-probe'
$version = (Get-Content -LiteralPath (Join-Path $runtime 'release_info.json') -Raw | ConvertFrom-Json).version
if ($version -ne 'v0.113.0' -or !(Test-Path -LiteralPath $GamePack -PathType Leaf)) { throw 'The isolated beta runtime or matching PCK is missing.' }
if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) { throw 'The isolated runtime is already running.' }

& dotnet build (Join-Path $source 'Beta113StartupProbe.csproj') -c Release --configfile (Join-Path $repo 'RMPStable/src/NuGet.Config') `
    "-p:GameManagedDir=$runtime/data_sts2_windows_x86_64" "-p:BaseIntermediateOutputPath=$probeBuild/obj/" --output $probeBuild
if ($LASTEXITCODE -ne 0) { throw 'Startup probe build failed.' }

$probeMod = Join-Path $runtime 'mods/Beta113StartupProbe'
$modDir = Join-Path $runtime 'mods/RMPStable'
New-Item -ItemType Directory -Force -Path $probeMod, $modDir | Out-Null
Copy-Item -LiteralPath (Join-Path $probeBuild 'Beta113StartupProbe.dll'), (Join-Path $source 'Beta113StartupProbe.json') -Destination $probeMod -Force
Get-ChildItem -LiteralPath $candidate -File | Copy-Item -Destination $modDir -Force

$evidence = Join-Path $root ('evidence/beta113-startup-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$modHash = (Get-FileHash -LiteralPath (Join-Path $candidate 'RMPStable.dll')).Hash
$results = @()
function New-CaseStart([string]$case, [bool]$enableMod, [string]$resultPath) {
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
            @{id='Beta113StartupProbe';is_enabled=$true;source='mods_directory'},
            @{id='RMPStable';is_enabled=$enableMod;source='mods_directory'}
        )
        $settings.skip_intro_logo = $true
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
        $start.Environment['RMP_046_PROBE'] = $resultPath
        $start.Environment['RMP_046_OPEN'] = if ($CheckReplica -and $enableMod) { '1' } else { '0' }
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
    Get-ChildItem -LiteralPath $resolved -Filter renderer.log -Recurse -File | Copy-Item -Destination (Join-Path $case 'renderer.log')
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Wait-ReplicaExit {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Replica still exiting. Evidence: $evidence" }
        Start-Sleep -Milliseconds 250
    }
}

if ($MultiplayerMode) {
    $processes = @()
    $cases = @()
    try {
        foreach ($role in @('host', 'client')) {
            $case = Join-Path $evidence $role
            $start = New-CaseStart $case $true (Join-Path $evidence "$role.json")
            $start.Environment['RMP_046_NET_ROLE'] = $role
            $start.Environment['RMP_046_MODE'] = $MultiplayerMode
            $cases += $case
            $processes += [Diagnostics.Process]::Start($start)
        }
        foreach ($owned in $processes) {
            if (!$owned.WaitForExit(150000) -or $owned.ExitCode -ne 0) { throw "Multiplayer probe failed. Evidence: $evidence" }
        }
        Get-Content -LiteralPath (Join-Path $evidence 'host.json'), (Join-Path $evidence 'client.json')
        Write-Output $evidence
    } finally {
        foreach ($owned in $processes) {
            if (!$owned.HasExited) { $owned.Kill() }
            $owned.Dispose()
        }
        Wait-ReplicaExit
        foreach ($case in $cases) { Clear-CaseProfile $case }
    }
    return
}

for ($iteration = 1; $iteration -le $Repetitions; $iteration++) {
    $order = if ($iteration % 2 -eq 1) { @('baseline', 'mod') } else { @('mod', 'baseline') }
    foreach ($variant in $order) {
        $case = Join-Path $evidence "$iteration-$variant"
        $resultPath = Join-Path $case 'result.json'
        $start = New-CaseStart $case ($variant -eq 'mod') $resultPath
        $startedMs = ([DateTime]::UtcNow - [DateTime]::UnixEpoch).TotalMilliseconds
        $owned = [Diagnostics.Process]::Start($start)
        try {
            if (!$owned.WaitForExit(150000)) { throw "Startup probe timed out. Evidence: $case" }
            if ($owned.ExitCode -ne 0 -or !(Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw "Startup probe failed. Evidence: $case" }
            $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
            if ($result.modLoaded -ne ($variant -eq 'mod') -or $result.childAtMenu -or $result.childAtSelection) { throw "Unexpected mod or replica state. Evidence: $case" }
            $results += [pscustomobject]@{ variant=$variant; iteration=$iteration; menuMs=$result.menuUtcMs-$startedMs; selectMs=$result.selectMs; startup=$result.startup; selection=$result.selection; replicaMs=$result.replicaMs; quickSl=$result.quickSl; playedCard=$result.playedCard }
        } finally {
            if (!$owned.HasExited) { $owned.Kill() }
            $owned.Dispose()
            Wait-ReplicaExit
            Clear-CaseProfile $case
        }
    }
}
@{modSha256=$modHash; game=$version; results=$results} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'measurements.json') -Encoding utf8
Write-Output $evidence
$results | Format-Table variant,iteration,menuMs,selectMs,replicaMs
