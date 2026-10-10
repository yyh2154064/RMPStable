param([ValidateSet('paid','payment','both')][string]$Scenario='both')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root='F:\projectFile\RMPStable-development'
$runtime=Join-Path $root 'runtime'
$mainPack='F:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck'
$build=Join-Path $root 'build\crystal-probe'
if ((& git -C $repo branch --show-current) -ne 'multiInstanceTest') {throw 'Wrong test branch'}
if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $runtime 'SlayTheSpire2.exe')) {throw 'Existing isolated runtime still in use'}
$dll='C:\Users\山岚\Desktop\RMPStable\RMPStable.dll'
if ((Get-FileHash -LiteralPath $dll).Hash -ne 'F57AF11C85632C53F3843D0A4D217BEB7B837CF7F55BA6915E25DB4FB187B488') {throw 'Expected accepted .12 DLL'}
New-Item -ItemType Directory -Force -Path $build | Out-Null
& dotnet build (Join-Path $repo 'tests\CrystalSphereProbe\CrystalSphereProbe.csproj') -c Release --configfile (Join-Path $repo 'RMPStable\src\NuGet.Config') "-p:GameManagedDir=$root/references/v0.111.0" "-p:BaseIntermediateOutputPath=$root/build/obj-crystal-probe/" -p:RestoreFallbackFolders= --output $build
if ($LASTEXITCODE -ne 0) {throw 'Focused probe build failed'}
# Existing runtime/test slot is reused, but this DLL contains only the new probe.
Copy-Item -LiteralPath 'C:\Users\山岚\Desktop\RMPStable' -Destination (Join-Path $runtime 'mods') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $build 'MultiInstanceSmoke.dll'),(Join-Path $repo 'tests\CrystalSphereProbe\MultiInstanceSmoke.json') -Destination (Join-Path $runtime 'mods\MultiInstanceSmoke') -Force
$runLabel='crystal-v12-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$evidence=Join-Path $root ('evidence\'+$runLabel)
New-Item -ItemType Directory -Path $evidence | Out-Null
$scenarios=if($Scenario -eq 'both') {@('paid','payment')} else {@($Scenario)}
$results=@()
foreach($branch in $scenarios) {
    $profile=[IO.Path]::GetFullPath((Join-Path $root ('profile-crystal-'+$branch)))
    if(!$profile.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) {throw 'Unexpected focused profile'}
    $caseEvidence=Join-Path $evidence $branch
    New-Item -ItemType Directory -Path $caseEvidence | Out-Null
    if(Test-Path -LiteralPath $profile) {
        if((Get-Item -LiteralPath $profile).Attributes -band [IO.FileAttributes]::ReparsePoint -or (Get-ChildItem -LiteralPath $profile -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) {throw 'Unexpected old profile reparse point'}
        foreach($file in Get-ChildItem -LiteralPath $profile -Recurse -File | Where-Object { $_.Name -eq 'renderer.log' -or $_.Extension -eq '.png' -or $_.Name -eq 'crystal-state.json' }) {Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $caseEvidence ('previous-'+$file.Directory.Name+'-'+$file.Name))}
        Remove-Item -LiteralPath $profile -Recurse -Force
    }
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
    $env:RMP_MULTI_TEST='1';$env:RMP_MULTI_CRYSTAL_TEST='1';$env:RMP_MULTI_HEADLESS='0';$env:RMP_MULTI_MAIN_PACK=$mainPack
    $env:RMP_CRYSTAL_BRANCH=$branch;$env:RMP_CRYSTAL_EVIDENCE=$caseEvidence
    foreach($name in @('RMP_MULTI_FULL_TEST','RMP_MULTI_FEEDBACK_TEST','RMP_MULTI_REPLAY_TEST','RMP_MULTI_ENDING_TEST')) {Remove-Item -LiteralPath ('Env:\'+$name) -ErrorAction SilentlyContinue}
    @{scenario=$branch;version='0.3.9-multiInstance.12';dll=(Get-FileHash -LiteralPath $dll).Hash;probe=(Get-FileHash -LiteralPath (Join-Path $build 'MultiInstanceSmoke.dll')).Hash;visible=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $caseEvidence 'manifest.json') -Encoding utf8
    $log=Join-Path $caseEvidence 'source.log'
    $arguments='--main-pack "'+$mainPack+'" --force-steam off --rendering-method forward_plus --rendering-driver d3d12 --windowed --position 40,40 --max-fps 120 --resolution 1920x1080 --quit-after 60000 --log-file "'+$log+'"'
    Write-Output ('VISIBLE CRYSTAL TEST: '+$branch+'; evidence: '+$caseEvidence)
    # User explicitly requested visible observation for this test.
    $owned=Start-Process -FilePath (Join-Path $runtime 'SlayTheSpire2.exe') -WorkingDirectory $runtime -ArgumentList $arguments -WindowStyle Normal -PassThru
    try {
        if(!$owned.WaitForExit(600000)){throw 'Focused test timeout'}
        $owned.Refresh();$exit=$owned.ExitCode
        $results+=@{scenario=$branch;exit=$exit;result=if(Test-Path -LiteralPath (Join-Path $caseEvidence 'result.json')){Get-Content -LiteralPath (Join-Path $caseEvidence 'result.json') -Raw|ConvertFrom-Json}else{$null}}
        Select-String -LiteralPath $log -Pattern '\[CrystalProbe\] (PASS|FAIL|CRYSTAL)' | Select-Object -ExpandProperty Line
    } finally {
        if(!$owned.HasExited){$owned.Kill()};$owned.Dispose()
        $childExitDeadline=[DateTime]::UtcNow.AddSeconds(15)
        while(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $runtime 'SlayTheSpire2.exe')) {
            if([DateTime]::UtcNow -gt $childExitDeadline){throw 'Owned replica still exiting; profile retained'}
            Start-Sleep -Milliseconds 250
        }
        # Retain evidence; cleanup only this resolved, disposable test profile.
        if(Get-ChildItem -LiteralPath $profile -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {throw 'Unexpected profile reparse point'}
        foreach($file in Get-ChildItem -LiteralPath $profile -Recurse -File | Where-Object { $_.Name -eq 'renderer.log' -or $_.Extension -eq '.png' -or $_.Name -eq 'crystal-state.json' }) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $caseEvidence ($file.Directory.Name+'-'+$file.Name))
        }
        Remove-Item -LiteralPath $profile -Recurse -Force
    }
}
$results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding utf8
Write-Output ('Focused evidence: '+$evidence)
Write-Output ($results | ConvertTo-Json -Depth 8)
