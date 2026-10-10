# One-time .13 delivery. Never executes or requires the accepted .12 suites.
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root='F:\projectFile\RMPStable-development'
$package=Join-Path $root 'build\package\RMPStable'
$desktop='C:\Users\山岚\Desktop\RMPStable'
$context='C:\Users\山岚\Desktop\RMPStable_多实例原生观战_新方案上下文_2026-10-08.md'
$expected='CE009FC8C121C2D1F7985204D9E0FAA857A6A50B97870DBF1B48FFA1BE3B2BB7'
function Git {
    $gitArguments=@($args)
    $output=& git.exe -C $repo @gitArguments
    if($LASTEXITCODE -ne 0){throw ('Git failed: '+($gitArguments -join ' '))}
    return $output
}
if((Git @('branch','--show-current')) -ne 'multiInstanceTest'){throw 'Wrong delivery branch'}
$branches=@(Git @('for-each-ref','--format=%(refname:short)','refs/heads'))|Sort-Object
if(($branches -join ',') -ne 'liveSharingTest,main,multiInstanceTest'){throw 'Unexpected branch inventory'}
$main=Git @('rev-parse','main')
if($main -ne 'ef59867edb864cb77bf66fd98f1fd4fe496afa5e'){throw 'Main changed; stop delivery'}
if((Get-FileHash -LiteralPath (Join-Path $package 'RMPStable.dll')).Hash -ne $expected){throw 'Unverified DLL'}
foreach($case in @('reward-hover-v13-20261011-023949','startup-display-v13-final-20261011-024106','timer-sync-v13-20261011-023856')) {
    $directory=Join-Path $root ('evidence\'+$case)
    $manifest=Get-Content -LiteralPath (Join-Path $directory 'manifest.json') -Raw|ConvertFrom-Json
    $result=Get-Content -LiteralPath (Join-Path $directory 'result.json') -Raw|ConvertFrom-Json
    if($manifest.dll -ne $expected -or $result.passed -ne $true -or (Test-Path -LiteralPath (Join-Path $directory 'failure.txt'))){throw ('Focused validation failed: '+$case)}
}
if(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue|Where-Object Path -eq (Join-Path $root 'runtime\SlayTheSpire2.exe')){throw 'Isolated runtime still in use'}
foreach($profile in @('profile-reward-hover','profile-startup-display','profile-timer-sync')) {
    if(Test-Path -LiteralPath (Join-Path $root $profile)){throw ('Focused profile remains: '+$profile)}
}
if((Get-Item -LiteralPath $package).Attributes -band [IO.FileAttributes]::ReparsePoint -or (Get-Item -LiteralPath $desktop).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Unexpected package link'}
$files=@('README-multiInstance.md','RMPStable.dll','RMPStable.json','RMPStable.pck')
if((@(Get-ChildItem -LiteralPath $package -Force|Select-Object -ExpandProperty Name|Sort-Object) -join ',') -ne ($files -join ',') -or (@(Get-ChildItem -LiteralPath $desktop -Force|Select-Object -ExpandProperty Name|Sort-Object) -join ',') -ne ($files -join ',')){throw 'Unexpected package contents'}
Copy-Item -LiteralPath (Join-Path $repo 'docs\README-multiInstance.md') -Destination (Join-Path $package 'README-multiInstance.md') -Force
$delivery=[ordered]@{version='0.3.9-multiInstance.13';destination=$desktop;date=[DateTime]::Now.ToString('s');acceptance='Waiting for user';files=@()}
foreach($name in $files) {
    Copy-Item -LiteralPath (Join-Path $package $name) -Destination (Join-Path $desktop $name) -Force
    $built=(Get-FileHash -LiteralPath (Join-Path $package $name)).Hash
    $delivered=(Get-FileHash -LiteralPath (Join-Path $desktop $name)).Hash
    if($built -ne $delivered){throw ('Delivery mismatch: '+$name)}
    $delivery.files+=@{name=$name;sha256=$delivered}
}
$delivery|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $root 'evidence\delivery-v13-focused.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $repo 'docs\session-context.md') -Destination $context -Force
Git @('diff','--check')|Out-Null
$paths=@('AGENTS.md','docs/session-context.md','docs/README-multiInstance.md','docs/crystal-sphere-focused-test.md','docs/multi-instance-v13-focused-test.md','RMPStable/RMPStable.json','RMPStable/src/RemoveMultiplayerPlayerLimit/Core/ModEntry.cs','RMPStable/src/RemoveMultiplayerPlayerLimit/Features/LiveSharing/MirrorClock.cs','RMPStable/src/RemoveMultiplayerPlayerLimit/Features/LiveSharing/MirrorHost.cs','RMPStable/src/RemoveMultiplayerPlayerLimit/Features/LiveSharing/MirrorJournal.cs','RMPStable/src/RemoveMultiplayerPlayerLimit/Features/LiveSharing/MirrorNativeWindow.cs','RMPStable/src/RemoveMultiplayerPlayerLimit/Features/LiveSharing/MirrorProtocol.cs','RMPStable/src/RemoveMultiplayerPlayerLimit/Features/LiveSharing/MirrorRenderer.cs','tests/CrystalSphereProbe','tests/RewardHoverProbe','tests/StartupDisplayProbe','tests/TimerSyncProbe','tools/Build-MultiInstance.ps1','tools/Test-CrystalSphere.ps1','tools/Test-RewardHover.ps1','tools/Test-StartupDisplay.ps1','tools/Test-TimerSync.ps1','tools/Deliver-FocusedV13.ps1')
Git (@('add','--')+$paths)|Out-Null
Git @('commit','-m','Fix native window startup, reward hover and authority timer in .13')
Git @('push','origin','multiInstanceTest')
if(Git @('status','--porcelain')){throw 'Unrelated changes remain; do not switch branches'}
$savedContext=Get-Content -LiteralPath (Join-Path $repo 'docs\session-context.md') -Raw
$savedRules=Get-Content -LiteralPath (Join-Path $repo 'AGENTS.md') -Raw
try {
    Git @('switch','liveSharingTest')
    [IO.File]::WriteAllText((Join-Path $repo 'docs\session-context.md'),$savedContext,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $repo 'AGENTS.md'),$savedRules,[Text.UTF8Encoding]::new($false))
    Git @('add','--','AGENTS.md','docs/session-context.md')|Out-Null
    Git @('commit','-m','Record native .13 focused delivery and pending user acceptance')
    Git @('push','origin','liveSharingTest')
} finally {Git @('switch','multiInstanceTest')}
$remote=@(Git @('ls-remote','--heads','origin'))
if($remote.Count -ne 3){throw 'Unexpected remote branches'}
foreach($branch in @('main','liveSharingTest','multiInstanceTest')) {
    $row=@($remote|Where-Object {($_ -split '\s+')[1] -eq ('refs/heads/'+$branch)})
    if($row.Count -ne 1 -or ($row[0] -split '\s+')[0] -ne (Git @('rev-parse',$branch))){throw ('Branch mismatch: '+$branch)}
}
if((Git @('rev-parse','main')) -ne $main -or (Git @('status','--porcelain'))){throw 'Final repository validation failed'}
Write-Output 'Delivered .13: four files verified; three branches synchronized; main unchanged; awaiting user acceptance.'
