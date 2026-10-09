param(
    [ValidateSet('full','feedback')][string]$Mode = 'feedback',
    [string]$ResourceRoot = 'F:/projectFile/RMPStable-development',
    [string]$MainPack = 'F:/SteamLibrary/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root = [IO.Path]::GetFullPath($ResourceRoot)
$runtime = Join-Path $root 'runtime'
$profile = Join-Path $root ('profile-' + $Mode)
$evidence = Join-Path $root 'evidence'
if ((Get-Item -LiteralPath $root).PSDrive.Name -eq 'C') { throw 'Development output must stay off C drive' }
if (Test-Path -LiteralPath $profile) { throw "Existing test profile: $profile. Inspect and clear it before rerunning." }
foreach ($relative in @('Roaming/SlayTheSpire2/default/1/settings.save','Roaming/SlayTheSpire2/default/1/modded/profile1/saves/progress.save','Roaming/SlayTheSpire2/default/1/modded/profile1/saves/prefs.save','Roaming/SlayTheSpire2/default/1/profile1/saves/progress.save','Roaming/SlayTheSpire2/default/1/profile1/saves/prefs.save')) {
    $destination = Join-Path $profile $relative
    New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root "profile-seed/$relative") -Destination $destination
}
$settings = Join-Path $profile 'Roaming/SlayTheSpire2/default/1/settings.save'
$config = Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
$config.fullscreen = $false; $config.window_position.X = -30000; $config.window_position.Y = -30000
$config.window_size.X = 1280; $config.window_size.Y = 720; $config.fps_limit = 120
$config.limit_fps_in_background = $false; $config.vsync = 'off'; $config.volume_master = 0; $config.skip_intro_logo = $true
$config.mod_settings.mod_list = @(@{id='RMPStable';is_enabled=$true;source='mods_directory'},@{id='MultiInstanceSmoke';is_enabled=$true;source='mods_directory'})
$config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $settings -Encoding utf8
$env:APPDATA = Join-Path $profile 'Roaming'; $env:LOCALAPPDATA = Join-Path $profile 'Local'
$env:RMP_MULTI_TEST = '1'; $env:RMP_MULTI_FULL_TEST = if ($Mode -eq 'full') {'1'} else {'0'}
$env:RMP_MULTI_FEEDBACK_TEST = if ($Mode -eq 'feedback') {'1'} else {'0'}
$env:RMP_MULTI_HEADLESS = '0'; $env:RMP_MULTI_MAIN_PACK = $MainPack
Copy-Item -LiteralPath (Join-Path $root 'build/package/RMPStable') -Destination (Join-Path $runtime 'mods') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'build/test/MultiInstanceSmoke.dll'), (Join-Path $repo 'tests/MultiInstanceSmoke/MultiInstanceSmoke.json') -Destination (Join-Path $runtime 'mods/MultiInstanceSmoke') -Force
$log = Join-Path $evidence ("test-v8-$Mode.log")
@{dll=(Get-FileHash -LiteralPath (Join-Path $runtime 'mods/RMPStable/RMPStable.dll')).Hash;test=(Get-FileHash -LiteralPath (Join-Path $runtime 'mods/MultiInstanceSmoke/MultiInstanceSmoke.dll')).Hash} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence "test-v8-$Mode-manifest.json") -Encoding utf8
$arguments = '--main-pack "' + $MainPack + '" --force-steam off --rendering-method forward_plus --rendering-driver d3d12 --windowed --position -30000,-30000 --max-fps 120 --resolution 1280x720 --quit-after 36000 --log-file "' + $log + '"'
$owned = Start-Process -FilePath (Join-Path $runtime 'SlayTheSpire2.exe') -WorkingDirectory $runtime -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (!$owned.WaitForExit(900000)) { throw 'Isolated test timed out' }
    $owned.Refresh()
    $exitCode = $owned.ExitCode
    Write-Output "Exit: $exitCode; log: $log"
    Get-Content -LiteralPath $log -Tail 12
    if ($exitCode -ne 0) { throw "Isolated game returned $exitCode" }
    if (!(Select-String -LiteralPath $log -SimpleMatch ($Mode.ToUpperInvariant() + ' PASSED') -Quiet)) { throw 'Harness did not report success' }
} finally {
    if (!$owned.HasExited) { $owned.Kill() }
    $owned.Dispose()
}
