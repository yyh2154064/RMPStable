param([string]$Label='visible-three',[switch]$BuildOnly,[switch]$Silent=$true,[ValidateSet('fourStages','battleSwitch19','rooms19','window','potions','cursor','endingDefeat','endingVictory','events','eventsRemaining','eventsLantern','deathDefeat','deathVictory','shopFeedback','returnExtras','purity','openingSingle','openingMulti2','openingMulti3')][string]$Case='fourStages')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root='F:\projectFile\RMPStable-development'
$runtime=Join-Path $root 'runtime'
$package=Join-Path $root 'build\package\RMPStable'
$probe=Join-Path $root 'build\multiplayer-visible-probe'
$pack='F:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck'
if ((& git -C $repo branch --show-current) -ne 'multiInstanceTest') {throw 'Wrong test branch'}
if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $runtime 'SlayTheSpire2.exe')) {throw 'Existing isolated runtime in use'}
if ((Get-FileHash -LiteralPath (Join-Path $package 'RMPStable.dll')).Hash -ne '0F7F37F7346ACCF7CFE5A63BC5F6CE5BD4E7F6FF632DF372DD94300D09CCFD76') {throw 'Expected v0.4.0 resident/UI candidate (version-only rebuild of final .19 gameplay)'}
& dotnet build (Join-Path $repo 'tests\MultiplayerVisibleProbe\MultiplayerVisibleProbe.csproj') -c Release --configfile (Join-Path $repo 'RMPStable\src\NuGet.Config') "-p:GameManagedDir=$root/references/v0.111.0" "-p:BaseIntermediateOutputPath=$root/build/obj-multiplayer-visible-probe/" -p:RestoreFallbackFolders= --output $probe
if ($LASTEXITCODE -ne 0) {throw 'Visible three-player probe build failed'}
if ($BuildOnly) {return}
Copy-Item -LiteralPath $package -Destination (Join-Path $runtime 'mods') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $probe 'MultiInstanceSmoke.dll'),(Join-Path $repo 'tests\MultiplayerVisibleProbe\MultiInstanceSmoke.json') -Destination (Join-Path $runtime 'mods\MultiInstanceSmoke') -Force
$evidence=Join-Path $root ('evidence\multiplayer-'+$Label+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$profiles=@()
$owned=@()
$streams=@()
$hiddenParents=@()
$testRoles=if($Case -eq 'openingSingle'){@('me')}else{@('me','testerA','testerB')}
if ($Silent -and !('RmpTestWindowHost' -as [type])) {Add-Type -Path (Join-Path $repo 'tests/TestWindowHost.cs')}
try {
    foreach ($role in $testRoles) {
        $profile=[IO.Path]::GetFullPath((Join-Path $root ('profile-multiplayer-visible-'+$role)))
        if (!$profile.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $profile)) {throw 'Inspect retained phase four profile before reuse'}
        $profiles+=$profile
        foreach ($relative in @('Roaming/SlayTheSpire2/default/1/settings.save','Roaming/SlayTheSpire2/default/1/modded/profile1/saves/progress.save','Roaming/SlayTheSpire2/default/1/modded/profile1/saves/prefs.save','Roaming/SlayTheSpire2/default/1/profile1/saves/progress.save','Roaming/SlayTheSpire2/default/1/profile1/saves/prefs.save')) {
            $destination=Join-Path $profile $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
            Copy-Item -LiteralPath (Join-Path $root ('profile-seed/'+$relative)) -Destination $destination
        }
        $settings=Join-Path $profile 'Roaming/SlayTheSpire2/default/1/settings.save'
        $config=Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
        $config.fullscreen=$false; $config.window_size.X=640; $config.window_size.Y=360; $config.vsync='off'; $config.volume_master=0; $config.fps_limit=30; $config.skip_intro_logo=$true
        $config.keyboard_mapping.rmpLiveSharing='F8'
        $config.limit_fps_in_background=$false
        $config.mod_settings.mod_list=@(@{id='RMPStable';is_enabled=$true;source='mods_directory'},@{id='MultiInstanceSmoke';is_enabled=$true;source='mods_directory'})
        $config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $settings -Encoding utf8
        $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $runtime 'SlayTheSpire2.exe'))
        $start.WorkingDirectory=$runtime; $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.WindowStyle=if($Silent){[Diagnostics.ProcessWindowStyle]::Hidden}else{[Diagnostics.ProcessWindowStyle]::Normal}
        $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
        foreach ($name in @($start.Environment.Keys)) {if ($name.StartsWith('RMP_',[StringComparison]::Ordinal)) {$start.Environment.Remove($name)|Out-Null}}
        $start.Environment['APPDATA']=Join-Path $profile 'Roaming'; $start.Environment['LOCALAPPDATA']=Join-Path $profile 'Local'
        $start.Environment['RMP_MULTI_TEST']='1'; $start.Environment['RMP_MULTI_HEADLESS']='0'; $start.Environment['RMP_MULTI_MAIN_PACK']=$pack
        $start.Environment['RMP_VISIBLE_TEST']='1'; $start.Environment['RMP_VISIBLE_ROLE']=$role
        $start.Environment['RMP_VISIBLE_CASE']=$Case
        if($Silent){$start.Environment['RMP_VISIBLE_SILENT']='1';$hiddenParent=[RmpTestWindowHost]::CreateHost();$hiddenParents+=$hiddenParent;$start.Environment['RMP_TEST_STARTUP_PARENT']=$hiddenParent.ToString();$start.Environment['RMP_TEST_LAUNCHER']=$PID.ToString();$start.ArgumentList.Add('--wid');$start.ArgumentList.Add($hiddenParent.ToString())}
        $start.Environment['RMP_VISIBLE_EVIDENCE']=$evidence; $start.Environment['RMP_VISIBLE_PORT']='33797'
        foreach ($arg in @('--main-pack',$pack,'--force-steam','off','--rendering-method','forward_plus','--rendering-driver','d3d12','--windowed','--resolution','640x360','--max-fps','30','--log-file',(Join-Path $evidence ($role+'.log')))) {$start.ArgumentList.Add($arg)}
        $process=[Diagnostics.Process]::Start($start); $owned+=$process
        $process.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal
        $streams+=@{process=$process;role=$role;stdout=$process.StandardOutput.ReadToEndAsync();stderr=$process.StandardError.ReadToEndAsync()}
    }
    @{dll=(Get-FileHash -LiteralPath (Join-Path $package 'RMPStable.dll')).Hash; probe=(Get-FileHash -LiteralPath (Join-Path $probe 'MultiInstanceSmoke.dll')).Hash; visible=!$Silent; actualRendering=$true; invisibleStartupParents=$Silent.IsPresent; mainUnmodified=$true; runtime=$runtime; pids=@($owned.Id)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'manifest.json') -Encoding utf8
    Write-Output ('ORIGINAL THREE-PLAYER TEST ('+$Case+', silent='+$Silent+'); evidence: '+$evidence)
    $deadline=[DateTime]::UtcNow.AddMinutes(35)
    while (@($owned | Where-Object {!$_.HasExited}).Count -gt 0) {
        if ([DateTime]::UtcNow -gt $deadline) {throw 'Visible three-player timeout'}
        foreach ($process in $owned) {if ($process.HasExited -and $process.ExitCode -ne 0) {throw ('Original peer failed: '+$process.Id+' exit='+$process.ExitCode)}}
        Start-Sleep -Milliseconds 500
    }
    foreach ($role in $testRoles) {
        $result=Get-Content -LiteralPath (Join-Path $evidence ($role+'-result.json')) -Raw | ConvertFrom-Json
        if ($result.passed -ne $true) {throw ('Visible three-player failed: '+$role)}
        Write-Output ($role+' checks: '+$result.checks)
    }
} finally {
    foreach ($stream in $streams) {
        $process=$stream.process
        $terminatedByCleanup=!$process.HasExited
        if($terminatedByCleanup){$process.Kill()}
        $process.WaitForExit()
        @{pid=$process.Id;exitCode=$process.ExitCode;terminatedByCleanup=$terminatedByCleanup} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence ($stream.role+'-exit.json')) -Encoding utf8
        $process.Dispose()
    }
    foreach ($hiddenParent in $hiddenParents) {[RmpTestWindowHost]::Release($hiddenParent)}
    $exitDeadline=[DateTime]::UtcNow.AddSeconds(20)
    while (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $runtime 'SlayTheSpire2.exe')) {
        if ([DateTime]::UtcNow -gt $exitDeadline) {throw 'Isolated replica still exiting; profiles retained'}
        Start-Sleep -Milliseconds 500
    }
    foreach($stream in $streams){foreach($name in 'stdout','stderr'){if($stream[$name].IsCompletedSuccessfully){$stream[$name].Result | Set-Content -LiteralPath (Join-Path $evidence ($stream.role+'-'+$name+'.log')) -Encoding utf8}}}
    foreach ($profile in $profiles) {
        if (!(Test-Path -LiteralPath $profile)) {continue}
        foreach ($log in Get-ChildItem -LiteralPath $profile -Filter renderer.log -Recurse -File) {
            Copy-Item -LiteralPath $log.FullName -Destination (Join-Path $evidence ((Split-Path $profile -Leaf)+'-'+$log.Directory.Name+'-renderer.log'))
        }
        foreach ($snapshot in Get-ChildItem -LiteralPath $profile -Filter 'visible-*.json*' -Recurse -File) { Copy-Item -LiteralPath $snapshot.FullName -Destination (Join-Path $evidence ((Split-Path $profile -Leaf)+'-'+$snapshot.Directory.Name+'-'+$snapshot.Name)) }
        if ((Get-Item -LiteralPath $profile).Attributes -band [IO.FileAttributes]::ReparsePoint -or (Get-ChildItem -LiteralPath $profile -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint})) {throw 'Profile reparse point; cleanup stopped'}
        Remove-Item -LiteralPath $profile -Recurse -Force
    }
}
Write-Output ('Visible three-player evidence: '+$evidence)
