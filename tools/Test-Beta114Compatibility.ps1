param(
    [string]$DevelopmentRoot = 'F:/projectFile/RMPStable-development',
    [string]$GameDirectory = 'F:/SteamLibrary/steamapps/common/Slay the Spire 2',
    [string]$Candidate
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($DevelopmentRoot)
$repo = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $root 'runtime/v0.114.0'
$exe = Join-Path $runtime 'SlayTheSpire2.exe'
$source = Join-Path $repo 'tests/Beta114CompatibilityProbe'
$game = Get-Content -LiteralPath (Join-Path $runtime 'release_info.json') -Raw | ConvertFrom-Json
$packGame = Get-Content -LiteralPath (Join-Path $GameDirectory 'release_info.json') -Raw | ConvertFrom-Json
$pack = Join-Path (Resolve-Path -LiteralPath $GameDirectory).Path 'SlayTheSpire2.pck'
$managed = 'data_sts2_windows_x86_64'
$gameDll = "$managed/sts2.dll"
$gameHash = (Get-FileHash -LiteralPath (Join-Path $runtime $gameDll)).Hash
if ($game.version -ne 'v0.114.0' -or $game.version -ne $packGame.version -or $game.commit -ne $packGame.commit -or
    $gameHash -ne (Get-FileHash -LiteralPath (Join-Path $GameDirectory $gameDll)).Hash -or
    !(Test-Path -LiteralPath $pack -PathType Leaf)) { throw 'The isolated v0.114.0 runtime and game PCK must match.' }
if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) {
    throw 'The isolated v0.114.0 runtime is already running.'
}
if (!$Candidate) { $Candidate = Join-Path $root 'build/v049/package/RMPStable' }
$probeBuild = Join-Path $root 'build/v049/beta114-compatibility-probe'
& dotnet build (Join-Path $source 'Beta114CompatibilityProbe.csproj') -c Release --configfile (Join-Path $repo 'RMPStable/src/NuGet.Config') `
    "-p:GameManagedDir=$runtime/$managed" "-p:BaseIntermediateOutputPath=$probeBuild/obj/" --output $probeBuild
if ($LASTEXITCODE -ne 0) { throw 'Compatibility probe build failed.' }

$modDir = Join-Path $runtime 'mods/RMPStable'
$probeMod = Join-Path $runtime 'mods/Beta114CompatibilityProbe'
New-Item -ItemType Directory -Force -Path $probeMod, $modDir | Out-Null
Get-ChildItem -LiteralPath $Candidate -File | Copy-Item -Destination $modDir -Force
Copy-Item -LiteralPath (Join-Path $probeBuild 'Beta114CompatibilityProbe.dll'), (Join-Path $source 'Beta114CompatibilityProbe.json') -Destination $probeMod -Force

$evidence = Join-Path $root ('evidence/beta114-compatibility-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $evidence | Out-Null
Write-Output "Evidence: $evidence"

function New-IsolatedStart([string]$role, [string]$case) {
    $profile = Join-Path $case 'profile'
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
        @{id='Beta114CompatibilityProbe';is_enabled=$true;source='mods_directory'},
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
    $start.Environment['RMP_049_PROBE'] = Join-Path $evidence "$role.json"
    $start.Environment['RMP_049_ROLE'] = $role
    $start.Environment['RMP_MULTI_MAIN_PACK'] = $pack
    foreach ($argument in @('--main-pack', $pack, '--force-steam', 'off', '--windowed', '--resolution', '1280x720', '--max-fps', '120',
        '--rendering-method', 'gl_compatibility', '--log-file', (Join-Path $case 'source.log'))) {
        $start.ArgumentList.Add($argument)
    }
    return $start
}

function Remove-IsolatedProfile([string]$case) {
    $profile = Join-Path $case 'profile'
    if (!(Test-Path -LiteralPath $profile -PathType Container)) { return }
    $profile = (Resolve-Path -LiteralPath $profile).Path
    if (!$profile.StartsWith($evidence + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        ((Get-Item -LiteralPath $profile).Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        (Get-ChildItem -LiteralPath $profile -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) {
        throw "Unexpected isolated profile path: $profile"
    }
    Get-ChildItem -LiteralPath $profile -Filter renderer.log -Recurse -File |
        Copy-Item -Destination (Join-Path $case 'renderer.log')
    Remove-Item -LiteralPath $profile -Recurse -Force
}

$cases = @{}
$processes = @{}
try {
    foreach ($role in @('host', 'client')) {
        $cases[$role] = Join-Path $evidence $role
        $processes[$role] = [Diagnostics.Process]::Start((New-IsolatedStart $role $cases[$role]))
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(250)
    while ($true) {
        $failure = @($processes.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 })
        if ($failure -or [DateTime]::UtcNow -gt $deadline) {
            Get-ChildItem -LiteralPath $evidence -Filter '*.error' | Get-Content
            throw "v0.114.0 probe failed. Evidence: $evidence"
        }
        if (!($processes.Values | Where-Object { !$_.HasExited })) { break }
        Start-Sleep -Milliseconds 200
    }
} finally {
    foreach ($owned in $processes.Values) {
        if (!$owned.HasExited) { $owned.Kill(); $null = $owned.WaitForExit(5000) }
        $owned.Dispose()
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Isolated renderer still running. Evidence: $evidence" }
        Start-Sleep -Milliseconds 250
    }
    foreach ($case in $cases.Values) { Remove-IsolatedProfile $case }
    foreach ($file in @('Beta114CompatibilityProbe.dll', 'Beta114CompatibilityProbe.json')) {
        Remove-Item -LiteralPath (Join-Path $probeMod $file) -ErrorAction SilentlyContinue
    }
}

$summary = [ordered]@{
    game = $game.version
    gameCommit = $game.commit
    gameDllSha256 = $gameHash
    modDllSha256 = (Get-FileHash -LiteralPath (Join-Path $Candidate 'RMPStable.dll')).Hash
}
foreach ($role in @('host', 'client')) {
    foreach ($name in @('source.log', 'renderer.log')) {
        $log = Join-Path $cases[$role] $name
        if (!(Select-String -LiteralPath $log -SimpleMatch -Quiet 'Loaded v0.4.9 compatibility payload for v0.114.0.') -or
            (Select-String -LiteralPath $log -Quiet -Pattern 'Exception thrown when calling mod initializer|\[RMP\] Failed to (initialize|create node for) module|\[RMP Stable\] Payload initializer failed|MissingMethodException|\[RMP:Mirror\] System\.|at .*\.AnimateRelicAwards\(')) {
            throw "Unexpected initialization result: $log"
        }
    }
    $summary[$role] = Get-Content -LiteralPath (Join-Path $evidence "$role.json") -Raw | ConvertFrom-Json
}
if (($summary.host.relics | ConvertTo-Json -Depth 4 -Compress) -ne ($summary.client.relics | ConvertTo-Json -Depth 4 -Compress)) {
    throw "Final relic inventories diverged. Evidence: $evidence"
}
foreach ($role in @('host', 'client')) {
    $log = Join-Path $cases[$role] 'source.log'
    if (!(Select-String -LiteralPath $log -Quiet -Pattern 'Discarding vote from player .* because it was cast on round 0, but it.s currently round 1')) {
        throw "The native queue did not discard a late vote in $role. Evidence: $evidence"
    }
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 6
