param([ValidateSet('paid','payment','both')][string]$Scenario='both',[switch]$CheckRestart)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root='F:\projectFile\RMPStable-development'
$runtime=Join-Path $root 'runtime'
$package=Join-Path $root 'build/package/RMPStable'
$build=Join-Path $root 'build/crystal-fog-probe'
$pack='F:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck'
if((& git -C $repo branch --show-current) -notin @('main','multiInstanceTest')){throw 'Wrong branch'}
if(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue|Where-Object Path -eq (Join-Path $runtime 'SlayTheSpire2.exe')){throw 'Isolated runtime in use'}
& dotnet build (Join-Path $repo 'tests/CrystalFogProbe/CrystalFogProbe.csproj') -c Release --configfile (Join-Path $repo 'RMPStable/src/NuGet.Config') "-p:GameManagedDir=$root/references/v0.111.0" "-p:BaseIntermediateOutputPath=$root/build/obj-crystal-fog-probe/" -p:RestoreFallbackFolders= --output $build
if($LASTEXITCODE -ne 0){throw 'Fog probe build failed'}
Copy-Item -LiteralPath $package -Destination (Join-Path $runtime 'mods') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $build 'MultiInstanceSmoke.dll'),(Join-Path $repo 'tests/CrystalFogProbe/MultiInstanceSmoke.json') -Destination (Join-Path $runtime 'mods/MultiInstanceSmoke') -Force
$evidence=Join-Path $root ('evidence/crystal-fog-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $evidence|Out-Null
$scenarios=if($Scenario -eq 'both'){@('paid','payment')}else{@($Scenario)}
$results=@()
foreach($branch in $scenarios){
    $profile=[IO.Path]::GetFullPath((Join-Path $root ('profile-crystal-fog-'+$branch)))
    if(!$profile.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $profile)){throw 'Inspect retained focused profile before reuse'}
    foreach($relative in @('Roaming/SlayTheSpire2/default/1/settings.save','Roaming/SlayTheSpire2/default/1/modded/profile1/saves/progress.save','Roaming/SlayTheSpire2/default/1/modded/profile1/saves/prefs.save','Roaming/SlayTheSpire2/default/1/profile1/saves/progress.save','Roaming/SlayTheSpire2/default/1/profile1/saves/prefs.save')){
        $destination=Join-Path $profile $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)|Out-Null
        Copy-Item -LiteralPath (Join-Path $root ('profile-seed/'+$relative)) -Destination $destination
    }
    $settings=Join-Path $profile 'Roaming/SlayTheSpire2/default/1/settings.save'
    $config=Get-Content -LiteralPath $settings -Raw|ConvertFrom-Json
    $config.fullscreen=$false;$config.window_position.X=40;$config.window_position.Y=40
    $config.window_size.X=1920;$config.window_size.Y=1080;$config.fps_limit=120
    $config.limit_fps_in_background=$false;$config.vsync='off';$config.volume_master=0;$config.skip_intro_logo=$true
    $config.mod_settings.mod_list=@(@{id='RMPStable';is_enabled=$true;source='mods_directory'},@{id='MultiInstanceSmoke';is_enabled=$true;source='mods_directory'})
    $config|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $settings -Encoding utf8
    $phases=if($CheckRestart -and $branch -eq 'paid'){@('fresh','restart')}else{@('fresh')}
    try{foreach($phase in $phases){
        $caseEvidence=Join-Path $evidence ($branch+'-'+$phase)
        New-Item -ItemType Directory -Path $caseEvidence|Out-Null
        $log=Join-Path $caseEvidence 'source.log'
        $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $runtime 'SlayTheSpire2.exe'))
        $start.WorkingDirectory=$runtime;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Normal
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        foreach($key in @($start.Environment.Keys)){if($key.StartsWith('RMP_',[StringComparison]::Ordinal)){$start.Environment.Remove($key)|Out-Null}}
        $start.Environment['APPDATA']=Join-Path $profile 'Roaming';$start.Environment['LOCALAPPDATA']=Join-Path $profile 'Local'
        $start.Environment['RMP_MULTI_TEST']='1';$start.Environment['RMP_CRYSTAL_FOG_TEST']='1';$start.Environment['RMP_MULTI_HEADLESS']='0';$start.Environment['RMP_MULTI_MAIN_PACK']=$pack
        $start.Environment['RMP_CRYSTAL_BRANCH']=$branch;$start.Environment['RMP_FOG_EVIDENCE']=$caseEvidence;$start.Environment['RMP_FOG_RESTART']=if($phase -eq 'restart'){'1'}else{'0'}
        foreach($arg in @('--main-pack',$pack,'--force-steam','off','--rendering-method','forward_plus','--rendering-driver','d3d12','--windowed','--position','40,40','--max-fps','120','--resolution','1920x1080','--quit-after','60000','--log-file',$log)){$start.ArgumentList.Add($arg)}
        @{scenario=$branch;phase=$phase;version='0.4.5';dll=(Get-FileHash (Join-Path $package 'RMPStable.dll')).Hash;probe=(Get-FileHash (Join-Path $build 'MultiInstanceSmoke.dll')).Hash;visible=$true;profile=$profile}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $caseEvidence 'manifest.json')
        Write-Output ('VISIBLE FOG TEST '+$branch+' '+$phase+': '+$caseEvidence)
        $owned=[Diagnostics.Process]::Start($start);$stdout=$owned.StandardOutput.ReadToEndAsync();$stderr=$owned.StandardError.ReadToEndAsync()
        try{
            if(!$owned.WaitForExit(600000)){throw 'Focused test timeout'}
            $owned.WaitForExit();$exit=$owned.ExitCode
            $stdout.Result|Set-Content -LiteralPath (Join-Path $caseEvidence 'stdout.log');$stderr.Result|Set-Content -LiteralPath (Join-Path $caseEvidence 'stderr.log')
            $result=if(Test-Path -LiteralPath (Join-Path $caseEvidence 'result.json')){Get-Content -LiteralPath (Join-Path $caseEvidence 'result.json') -Raw|ConvertFrom-Json}else{$null}
            $results+=@{scenario=$branch;phase=$phase;exit=$exit;result=$result}
            Write-Output ($results[-1]|ConvertTo-Json -Depth 5)
        }finally{
            if(!$owned.HasExited){$owned.Kill()};$owned.Dispose()
            $end=[DateTime]::UtcNow.AddSeconds(20)
            while(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue|Where-Object Path -eq (Join-Path $runtime 'SlayTheSpire2.exe')){if([DateTime]::UtcNow -gt $end){throw 'Owned replica still exiting; retain profile'};Start-Sleep -Milliseconds 250}
            foreach($file in Get-ChildItem -LiteralPath $profile -Recurse -File|Where-Object {$_.Name -eq 'renderer.log' -or $_.Extension -eq '.png' -or $_.Name -in @('crystal-state.json','rmp-spectator.json')}){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $caseEvidence ($file.Directory.Name+'-'+$file.Name))}
        }
        if($exit -ne 0 -or $null -eq $result -or $result.failures -ne 0){throw 'Fog validation failed; see evidence'}
    }}finally{
        if(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue|Where-Object Path -eq (Join-Path $runtime 'SlayTheSpire2.exe')){throw 'Isolated process still running; retain profile'}
        # The exact profile is under the approved development root; never follow reparse points.
        if(!(Test-Path -LiteralPath $profile)){throw 'Focused profile disappeared'}
        if((Get-Item -LiteralPath $profile).Attributes -band [IO.FileAttributes]::ReparsePoint -or (Get-ChildItem -LiteralPath $profile -Recurse -Force|Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint})){throw 'Profile reparse point; retain profile'}
        Remove-Item -LiteralPath $profile -Recurse -Force
        $results|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $evidence 'results.json')
    }
}
Write-Output ('Focused fog evidence: '+$evidence)
