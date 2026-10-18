param(
    [Parameter(Mandatory=$true)][string]$SourceCommit,
    [Parameter(Mandatory=$true)][string]$VerifiedDllHash,
    [Parameter(Mandatory=$true)][string[]]$EvidenceDirectories,
    [switch]$VerifyOnly
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root='F:\projectFile\RMPStable-development'
$package=Join-Path $root 'build/package/RMPStable'
$zip=Join-Path $root 'build/package/RMPStable-v0.4.5.zip'
$body=[IO.File]::ReadAllText((Join-Path $repo 'docs/release-v0.4.5.md'))
if((& git -C $repo branch --show-current) -ne 'main'){throw 'Release requires the explicitly authorized main branch'}
if((& git -C $repo rev-parse 'v0.4.5^{commit}') -ne $SourceCommit){throw 'Release tag does not identify the reviewed source commit'}
if((Get-FileHash (Join-Path $package 'RMPStable.dll')).Hash -ne $VerifiedDllHash){throw 'Unverified delivery DLL'}
if((Get-Content (Join-Path $package 'RMPStable.json') -Raw|ConvertFrom-Json).version -ne '0.4.5'){throw 'Wrong package version'}
foreach($directory in $EvidenceDirectories){
    $manifest=Get-Content (Join-Path $directory 'manifest.json') -Raw|ConvertFrom-Json
    if($manifest.dll -ne $VerifiedDllHash){throw ('Evidence does not use final DLL: '+$directory)}
    $results=@(Get-ChildItem -LiteralPath $directory -Filter '*result.json' -File)
    if(!$results.Count){throw ('No test result: '+$directory)}
    foreach($file in $results){
        $result=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json
        if(($null -ne $result.failures -and $result.failures -ne 0) -or ($null -ne $result.passed -and !$result.passed)){throw ('Failed test: '+$file.FullName)}
    }
}
if(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue|Where-Object Path -eq (Join-Path $root 'runtime/SlayTheSpire2.exe')){throw 'Owned tests still running'}
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
    $expected=@('README-multiInstance.md','RMPStable.dll','RMPStable.json','RMPStable.pck','update.cmd')|Sort-Object
    $entries=@($archive.Entries|Where-Object Name)
    if((($entries.Name|Sort-Object) -join ',') -ne ($expected -join ',') -or @($entries|Where-Object {$_.FullName -ne ('RMPStable/'+$_.Name)}).Count){throw 'Unexpected release archive layout'}
    foreach($entry in $entries){
        $stream=$entry.Open();try{$entryHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
        if($entryHash -ne (Get-FileHash (Join-Path $package $entry.Name)).Hash){throw ('Archive/package mismatch: '+$entry.Name)}
    }
}finally{$archive.Dispose()}
$zipHash=(Get-FileHash $zip).Hash
if($VerifyOnly){
    @{source=$SourceCommit;dll=$VerifiedDllHash;zip=$zipHash;bytes=(Get-Item $zip).Length;archiveVerified=$true;evidenceVerified=$true}|ConvertTo-Json
    return
}

# Use the GitHub CLI's own normal authenticated session. This workflow never
# reads or repurposes a Git credential-manager password/token.
$gh=Get-Command gh -ErrorAction SilentlyContinue
if(!$gh){throw 'GitHub CLI is unavailable; connect the official GitHub integration or configure gh login before publishing'}
function GhJson([string]$Method,[string]$Endpoint,$Data=$null){
    $arguments=@('api','--hostname','github.com','--method',$Method,$Endpoint)
    if($null -ne $Data){
        $requestFile=Join-Path $root 'build/release045-api-request.json'
        [IO.File]::WriteAllText($requestFile,($Data|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
        $arguments+=@('--input',$requestFile)
    }
    $output=& $gh.Source @arguments
    if($LASTEXITCODE -ne 0){throw ('GitHub CLI request failed: '+$Method+' '+$Endpoint)}
    ($output -join [Environment]::NewLine)|ConvertFrom-Json
}
$base='repos/yyh2154064/RMPStable'
$remoteTag=GhJson GET ($base+'/git/ref/tags/v0.4.5')
if($remoteTag.object.sha -ne $SourceCommit){throw 'GitHub tag differs from source commit'}
$releases=GhJson GET ($base+'/releases?per_page=100')
$release=@($releases|Where-Object tag_name -eq 'v0.4.5')|Select-Object -First 1
if(!$release){$release=GhJson POST ($base+'/releases') @{tag_name='v0.4.5';target_commitish=$SourceCommit;name='RMP Stable v0.4.5';body=$body;draft=$true;prerelease=$false}}
if($release.target_commitish -ne $SourceCommit -or $release.tag_name -ne 'v0.4.5'){throw 'Existing release belongs to another source revision'}
if(!$release.draft -and $release.body.TrimEnd() -ne $body.TrimEnd()){throw 'An existing published release has different content; inspect before updating'}
$asset=@($release.assets|Where-Object name -eq 'RMPStable-v0.4.5.zip')|Select-Object -First 1
if(!$asset){
    & $gh.Source release upload v0.4.5 $zip --repo yyh2154064/RMPStable
    if($LASTEXITCODE -ne 0){throw 'GitHub release upload failed'}
    $release=GhJson GET ($base+'/releases/'+$release.id)
    $asset=@($release.assets|Where-Object name -eq 'RMPStable-v0.4.5.zip')|Select-Object -First 1
}
if($asset.state -ne 'uploaded' -or $asset.size -ne (Get-Item $zip).Length -or $asset.digest -ne ('sha256:'+$zipHash.ToLowerInvariant())){throw 'Remote asset size/digest differs from local archive'}
$release=GhJson PATCH ($base+'/releases/'+$release.id) @{name='RMP Stable v0.4.5';body=$body;draft=$false;prerelease=$false;make_latest='true'}
$latest=GhJson GET ($base+'/releases/latest')
if($latest.id -ne $release.id -or $latest.draft -or $latest.prerelease -or $latest.name -ne 'RMP Stable v0.4.5' -or $latest.body.TrimEnd() -ne $body.TrimEnd()){throw 'Published Latest release verification failed'}
$record=@{url=$latest.html_url;tag=$latest.tag_name;title=$latest.name;source=$SourceCommit;dll=$VerifiedDllHash;zip=$zipHash;bytes=$asset.size;remoteDigest=$asset.digest;latestVerified=$true;bodyVerified=$true}
$record|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'evidence/release-v045-published.json') -Encoding utf8
$record|ConvertTo-Json
