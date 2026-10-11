param([string]$Label='phase1',[switch]$BuildOnly)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root='F:\projectFile\RMPStable-development'
$runtime=Join-Path $root 'runtime'
$package=Join-Path $root 'build\package\RMPStable'
$probe=Join-Path $root 'build\multiplayer-phase1-probe'
$pack='F:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck'
if ((& git -C $repo branch --show-current) -ne 'multiInstanceTest') {throw 'Wrong test branch'}
if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $runtime 'SlayTheSpire2.exe')) {throw 'Existing isolated runtime in use'}
& (Join-Path $repo 'tools\Build-MultiInstance.ps1') -GameReferences (Join-Path $root 'references') -ModPck 'C:\Users\山岚\Desktop\RMPStable\RMPStable.pck'
if ($LASTEXITCODE -ne 0) {throw 'Payload build failed'}
& dotnet build (Join-Path $repo 'tests\MultiplayerPhase1Probe\MultiplayerPhase1Probe.csproj') -c Release --configfile (Join-Path $repo 'RMPStable\src\NuGet.Config') "-p:GameManagedDir=$root/references/v0.111.0" "-p:BaseIntermediateOutputPath=$root/build/obj-multiplayer-phase1-probe/" -p:RestoreFallbackFolders= --output $probe
if ($LASTEXITCODE -ne 0) {throw 'Phase one probe build failed'}
if ($BuildOnly) {return}
Copy-Item -LiteralPath $package -Destination (Join-Path $runtime 'mods') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $probe 'MultiInstanceSmoke.dll'),(Join-Path $repo 'tests\MultiplayerPhase1Probe\MultiInstanceSmoke.json') -Destination (Join-Path $runtime 'mods\MultiInstanceSmoke') -Force
$evidence=Join-Path $root ('evidence\multiplayer-'+$Label+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$profiles=@()
$owned=@()
try {
    foreach ($role in @('host','client')) {
        $profile=[IO.Path]::GetFullPath((Join-Path $root ('profile-multiplayer-phase1-'+$role)))
        if (!$profile.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $profile)) {throw 'Inspect retained phase one profile before reuse'}
        $profiles+=$profile
        foreach ($relative in @('Roaming/SlayTheSpire2/default/1/settings.save','Roaming/SlayTheSpire2/default/1/modded/profile1/saves/progress.save','Roaming/SlayTheSpire2/default/1/modded/profile1/saves/prefs.save','Roaming/SlayTheSpire2/default/1/profile1/saves/progress.save','Roaming/SlayTheSpire2/default/1/profile1/saves/prefs.save')) {
            $destination=Join-Path $profile $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
            Copy-Item -LiteralPath (Join-Path $root ('profile-seed/'+$relative)) -Destination $destination
        }
        $settings=Join-Path $profile 'Roaming/SlayTheSpire2/default/1/settings.save'
        $config=Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
        $config.volume_master=0; $config.fps_limit=30; $config.skip_intro_logo=$true
        $config.limit_fps_in_background=$false
        $config.mod_settings.mod_list=@(@{id='RMPStable';is_enabled=$true;source='mods_directory'},@{id='MultiInstanceSmoke';is_enabled=$true;source='mods_directory'})
        $config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $settings -Encoding utf8
        $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $runtime 'SlayTheSpire2.exe'))
        $start.WorkingDirectory=$runtime; $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
        $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
        foreach ($name in @($start.Environment.Keys)) {if ($name.StartsWith('RMP_',[StringComparison]::Ordinal)) {$start.Environment.Remove($name)|Out-Null}}
        $start.Environment['APPDATA']=Join-Path $profile 'Roaming'; $start.Environment['LOCALAPPDATA']=Join-Path $profile 'Local'
        $start.Environment['RMP_MULTI_TEST']='1'; $start.Environment['RMP_MULTI_HEADLESS']='1'; $start.Environment['RMP_MULTI_MAIN_PACK']=$pack
        $start.Environment['RMP_PHASE1_TEST']='1'; $start.Environment['RMP_PHASE1_ROLE']=$role
        $start.Environment['RMP_PHASE1_EVIDENCE']=$evidence; $start.Environment['RMP_PHASE1_PORT']='33793'
        foreach ($arg in @('--headless','--main-pack',$pack,'--force-steam','off','--max-fps','30','--log-file',(Join-Path $evidence ($role+'.log')))) {$start.ArgumentList.Add($arg)}
        $process=[Diagnostics.Process]::Start($start); $owned+=$process
        $process.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal
        $process.BeginOutputReadLine(); $process.BeginErrorReadLine()
    }
    @{dll=(Get-FileHash -LiteralPath (Join-Path $package 'RMPStable.dll')).Hash; probe=(Get-FileHash -LiteralPath (Join-Path $probe 'MultiInstanceSmoke.dll')).Hash; headless=$true; mainUnmodified=$true; runtime=$runtime; pids=@($owned.Id)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'manifest.json') -Encoding utf8
    Write-Output ('HEADLESS ORIGINAL MULTIPLAYER PHASE ONE; evidence: '+$evidence)
    $deadline=[DateTime]::UtcNow.AddMinutes(6)
    while (@($owned | Where-Object {!$_.HasExited}).Count -gt 0) {
        if ([DateTime]::UtcNow -gt $deadline) {throw 'Phase one timeout'}
        foreach ($process in $owned) {if ($process.HasExited -and $process.ExitCode -ne 0) {throw ('Original peer failed: '+$process.Id)}}
        Start-Sleep -Milliseconds 500
    }
    foreach ($role in @('host','client')) {
        $result=Get-Content -LiteralPath (Join-Path $evidence ($role+'-result.json')) -Raw | ConvertFrom-Json
        if ($result.passed -ne $true) {throw ('Phase one failed: '+$role)}
        Write-Output ($role+' checks: '+$result.checks)
    }
} finally {
    foreach ($process in $owned) {if (!$process.HasExited) {$process.Kill()}; $process.WaitForExit(); $process.Dispose()}
    $exitDeadline=[DateTime]::UtcNow.AddSeconds(20)
    while (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $runtime 'SlayTheSpire2.exe')) {
        if ([DateTime]::UtcNow -gt $exitDeadline) {throw 'Isolated replica still exiting; profiles retained'}
        Start-Sleep -Milliseconds 500
    }
    foreach ($profile in $profiles) {
        if (!(Test-Path -LiteralPath $profile)) {continue}
        foreach ($log in Get-ChildItem -LiteralPath $profile -Filter renderer.log -Recurse -File) {
            Copy-Item -LiteralPath $log.FullName -Destination (Join-Path $evidence ((Split-Path $profile -Leaf)+'-'+$log.Directory.Name+'-renderer.log'))
        }
        if ((Get-Item -LiteralPath $profile).Attributes -band [IO.FileAttributes]::ReparsePoint -or (Get-ChildItem -LiteralPath $profile -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint})) {throw 'Profile reparse point; cleanup stopped'}
        Remove-Item -LiteralPath $profile -Recurse -Force
    }
}
Write-Output ('Phase one evidence: '+$evidence)
