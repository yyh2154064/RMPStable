param([string]$PackageRoot='C:\Users\山岚\Desktop\RMPStable',[string]$Label='v12',[switch]$ExpectFixed)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root='F:\projectFile\RMPStable-development'
$runtime=Join-Path $root 'runtime'
$mainPack='F:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck'
$build=Join-Path $root 'build\reward-hover-probe'
if ((& git -C $repo branch --show-current) -ne 'multiInstanceTest') {throw 'Wrong test branch'}
if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $runtime 'SlayTheSpire2.exe')) {throw 'Existing isolated runtime still in use'}
New-Item -ItemType Directory -Force -Path $build | Out-Null
& dotnet build (Join-Path $repo 'tests\RewardHoverProbe\RewardHoverProbe.csproj') -c Release --configfile (Join-Path $repo 'RMPStable\src\NuGet.Config') "-p:GameManagedDir=$root/references/v0.111.0" "-p:BaseIntermediateOutputPath=$root/build/obj-reward-hover-probe/" -p:RestoreFallbackFolders= --output $build
if ($LASTEXITCODE -ne 0) {throw 'Focused probe build failed'}
Copy-Item -LiteralPath $PackageRoot -Destination (Join-Path $runtime 'mods') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $build 'MultiInstanceSmoke.dll'),(Join-Path $repo 'tests\RewardHoverProbe\MultiInstanceSmoke.json') -Destination (Join-Path $runtime 'mods\MultiInstanceSmoke') -Force
$evidence=Join-Path $root ('evidence\reward-hover-'+$Label+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$profile=[IO.Path]::GetFullPath((Join-Path $root 'profile-reward-hover'))
if(!$profile.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) {throw 'Unexpected focused profile'}
if(Test-Path -LiteralPath $profile) {throw 'Previous test profile retained; inspect it before reuse'}
foreach($relative in @('Roaming/SlayTheSpire2/default/1/settings.save','Roaming/SlayTheSpire2/default/1/modded/profile1/saves/progress.save','Roaming/SlayTheSpire2/default/1/modded/profile1/saves/prefs.save','Roaming/SlayTheSpire2/default/1/profile1/saves/progress.save','Roaming/SlayTheSpire2/default/1/profile1/saves/prefs.save')) {
    $destination=Join-Path $profile $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root ('profile-seed/'+$relative)) -Destination $destination
}
$settings=Join-Path $profile 'Roaming/SlayTheSpire2/default/1/settings.save'
$config=Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
$config.fullscreen=$false;$config.window_position.X=40;$config.window_position.Y=40
$config.window_size.X=1920;$config.window_size.Y=1080;$config.fps_limit=120
$config.limit_fps_in_background=$false;$config.vsync='off';$config.volume_master=0;$config.skip_intro_logo=$true
$config.mod_settings.mod_list=@(@{id='RMPStable';is_enabled=$true;source='mods_directory'},@{id='MultiInstanceSmoke';is_enabled=$true;source='mods_directory'})
$config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $settings -Encoding utf8
$env:APPDATA=Join-Path $profile 'Roaming';$env:LOCALAPPDATA=Join-Path $profile 'Local'
$env:RMP_MULTI_TEST='1';$env:RMP_MULTI_HOVER_TEST='1';$env:RMP_MULTI_HEADLESS='0';$env:RMP_MULTI_MAIN_PACK=$mainPack
$env:RMP_HOVER_EVIDENCE=$evidence
$env:RMP_HOVER_EXPECT_FIXED=if($ExpectFixed){'1'}else{'0'}
foreach($name in @('RMP_MULTI_CRYSTAL_TEST','RMP_MULTI_FULL_TEST','RMP_MULTI_FEEDBACK_TEST','RMP_MULTI_REPLAY_TEST','RMP_MULTI_ENDING_TEST')) {Remove-Item -LiteralPath ('Env:\'+$name) -ErrorAction SilentlyContinue}
@{label=$Label;dll=(Get-FileHash -LiteralPath (Join-Path $PackageRoot 'RMPStable.dll')).Hash;probe=(Get-FileHash -LiteralPath (Join-Path $build 'MultiInstanceSmoke.dll')).Hash;visible=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'manifest.json') -Encoding utf8
$log=Join-Path $evidence 'source.log'
$arguments='--main-pack "'+$mainPack+'" --force-steam off --rendering-method forward_plus --rendering-driver d3d12 --windowed --position 40,40 --max-fps 120 --resolution 1920x1080 --quit-after 60000 --log-file "'+$log+'"'
Write-Output ('VISIBLE REWARD HOVER TEST; evidence: '+$evidence)
$owned=Start-Process -FilePath (Join-Path $runtime 'SlayTheSpire2.exe') -WorkingDirectory $runtime -ArgumentList $arguments -WindowStyle Normal -PassThru
try {
    if(!$owned.WaitForExit(300000)){throw 'Focused test timeout'}
    $owned.Refresh();$exit=$owned.ExitCode
    Select-String -LiteralPath $log -Pattern '\[RewardHover\] (RESULT|FAIL|鼠标|仍不|移动)' | Select-Object -ExpandProperty Line
    if($exit -ne 0){throw ('Probe failed with exit '+$exit)}
    Get-Content -LiteralPath (Join-Path $evidence 'result.json') -Raw
} finally {
    if(!$owned.HasExited){$owned.Kill()};$owned.Dispose()
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    while(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $runtime 'SlayTheSpire2.exe')) {
        if([DateTime]::UtcNow -gt $deadline){throw 'Owned replica still exiting; profile retained'}
        Start-Sleep -Milliseconds 250
    }
    if((Get-Item -LiteralPath $profile).Attributes -band [IO.FileAttributes]::ReparsePoint -or (Get-ChildItem -LiteralPath $profile -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) {throw 'Unexpected profile reparse point'}
    foreach($file in Get-ChildItem -LiteralPath $profile -Recurse -File | Where-Object { $_.Name -eq 'renderer.log' -or $_.Extension -eq '.png' -or $_.Name -eq 'hover-state.json' }) {Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $evidence ($file.Directory.Name+'-'+$file.Name))}
    Remove-Item -LiteralPath $profile -Recurse -Force
}
Write-Output ('Focused evidence: '+$evidence)
