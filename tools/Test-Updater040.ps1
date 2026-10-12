param([string]$DevelopmentRoot = 'F:/projectFile/RMPStable-development')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root = [IO.Path]::GetFullPath($DevelopmentRoot)
$archive = Join-Path $root 'build/package/RMPStable-v0.4.0.zip'
if (!(Test-Path -LiteralPath $archive)) { throw 'Build the v0.4.0 release ZIP first.' }
$evidence = Join-Path $root 'evidence/updater-v040'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$fixture = Join-Path $root ('build/updater040-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$results = [Collections.Generic.List[object]]::new()
$process = $null
function Get-Payload([string]$raw) {
    $marker = '#==RMPSTABLE_POWERSHELL=='
    $payload = $raw.Substring($raw.LastIndexOf($marker) + $marker.Length).Replace("`r`n", "`n")
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseInput($payload, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw "Updater syntax errors: $errors" }
    return $payload
}
function Invoke-Case([string]$name, [string]$installed, [string]$tag,
                     [string]$zip, [int]$expectedExit, [bool]$updated,
                     [string]$payload, [bool]$downloadFailure = $false) {
    $caseRoot = Join-Path $fixture $name
    New-Item -ItemType Directory -Path $caseRoot | Out-Null
    $manifestPath = Join-Path $caseRoot 'RMPStable.json'
    [IO.File]::WriteAllText($manifestPath, (@{id='RMPStable';version=$installed} | ConvertTo-Json), $utf8)
    foreach ($file in @('RMPStable.dll','RMPStable.pck')) {
        [IO.File]::WriteAllText((Join-Path $caseRoot $file), 'original-install', $utf8)
    }
    $before = @{}
    foreach ($file in @('RMPStable.dll','RMPStable.pck','RMPStable.json')) {
        $before[$file] = (Get-FileHash -LiteralPath (Join-Path $caseRoot $file)).Hash
    }
    $escapedRoot = $caseRoot.Replace("'", "''")
    $escapedZip = $zip.Replace("'", "''")
    $mock = @"
function Resolve-LatestReleaseTag { return '$tag' }
function Download-File([string]`$url, [string]`$destination) {
    [IO.File]::WriteAllText('$escapedRoot/request.txt', `$url)
    if (`$$downloadFailure) { throw 'Simulated unpublished/missing release asset' }
    Copy-Item -LiteralPath '$escapedZip' -Destination `$destination
}
"@
    $entry = "try {`n    Write-Host 'RMP Stable updater'"
    if (!$payload.Contains($entry)) { throw 'Updater entry point changed; update this independent fixture.' }
    $instrumented = $payload.Replace($entry, ($mock + "`r`n" + $entry))
    $runner = Join-Path $caseRoot 'run.ps1'
    $setup = "`$env:RMP_UPDATE_SELF = '$escapedRoot/update.cmd'`r`n`$env:TEMP = '$escapedRoot'`r`n`$env:TMP = '$escapedRoot'`r`n"
    [IO.File]::WriteAllText($runner, ($setup + $instrumented), $utf8)
    $script:process = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File', ('"' + $runner + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $caseRoot 'stdout.txt') -RedirectStandardError (Join-Path $caseRoot 'stderr.txt')
    if (!$script:process.WaitForExit(30000)) { throw "Updater timed out: $name" }
    $script:process.Refresh()
    if ($script:process.ExitCode -ne $expectedExit) {
        throw "$name exit $($script:process.ExitCode); $(Get-Content -LiteralPath (Join-Path $caseRoot 'stdout.txt') -Raw)"
    }
    if ($updated) {
        foreach ($file in @('RMPStable.dll','RMPStable.pck','RMPStable.json')) {
            $actual = (Get-FileHash -LiteralPath (Join-Path $caseRoot $file)).Hash
            $expected = (Get-FileHash -LiteralPath (Join-Path $root "build/package/RMPStable/$file")).Hash
            if ($actual -ne $expected) { throw "$name did not install exact v0.4.0 $file" }
        }
    } else {
        foreach ($file in $before.Keys) {
            if ((Get-FileHash -LiteralPath (Join-Path $caseRoot $file)).Hash -ne $before[$file]) { throw "$name changed original $file" }
        }
    }
    $request = Join-Path $caseRoot 'request.txt'
    if (Test-Path -LiteralPath $request) {
        $expectedUrl = "https://github.com/yyh2154064/RMPStable/releases/download/$tag/RMPStable-$tag.zip"
        if ([IO.File]::ReadAllText($request) -ne $expectedUrl) { throw "$name requested wrong asset" }
    } elseif ($updated -or $expectedExit -ne 0) { throw "$name never attempted expected download" }
    $results.Add([pscustomobject]@{case=$name;installed=$installed;published=$tag;exit=$expectedExit;updated=$updated;passed=$true})
}
try {
    $raw = [IO.File]::ReadAllText((Join-Path $repo 'RMPStable-Packager/update.cmd'))
    $payload = Get-Payload $raw
    $oldRaw = (& git -C $repo show 'ef59867edb864cb77bf66fd98f1fd4fe496afa5e:RMPStable-Packager/update.cmd') -join "`r`n"
    if ($LASTEXITCODE) { throw 'Cannot read existing v0.3.9 updater.' }
    Invoke-Case 'old-updater-039-to040' '0.3.9' 'v0.4.0' $archive 0 $true (Get-Payload $oldRaw)
    Invoke-Case '039-to040' '0.3.9' 'v0.4.0' $archive 0 $true $payload
    Invoke-Case '019-to040' '0.3.9-multiInstance.19' 'v0.4.0' $archive 0 $true $payload
    Invoke-Case '040-current' '0.4.0' 'v0.4.0' $archive 0 $false $payload
    Invoke-Case 'newer-local' '0.4.1' 'v0.4.0' $archive 0 $false $payload
    Invoke-Case 'unpublished-older-latest' '0.4.0' 'v0.3.9' $archive 0 $false $payload
    Invoke-Case 'missing-release-asset' '0.3.9' 'v0.4.0' $archive 1 $false $payload $true
    $badRoot = Join-Path $fixture 'bad-package'
    $badMod = Join-Path $badRoot 'RMPStable'
    New-Item -ItemType Directory -Path $badMod | Out-Null
    foreach ($file in @('RMPStable.dll','RMPStable.pck')) { [IO.File]::WriteAllText((Join-Path $badMod $file), 'bad-package', $utf8) }
    [IO.File]::WriteAllText((Join-Path $badMod 'RMPStable.json'), '{"id":"RMPStable","version":"0.3.9"}', $utf8)
    $badZip = Join-Path $fixture 'wrong-version.zip'
    Compress-Archive -LiteralPath $badMod -DestinationPath $badZip
    Invoke-Case 'tag-package-mismatch' '0.3.9' 'v0.4.0' $badZip 1 $false $payload
    Remove-Item -LiteralPath (Join-Path $badMod 'RMPStable.pck')
    $missingZip = Join-Path $fixture 'missing-file.zip'
    Compress-Archive -LiteralPath $badMod -DestinationPath $missingZip
    Invoke-Case 'missing-runtime-file' '0.3.9' 'v0.4.0' $missingZip 1 $false $payload
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding UTF8
    Write-Output "$($results.Count) updater cases passed; network resolution/download mocked; exact real release files verified."
} finally {
    if ($null -ne $process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force }
    $resolved = [IO.Path]::GetFullPath($fixture)
    $allowed = [IO.Path]::GetFullPath((Join-Path $root 'build')) + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup path escaped approved build root.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
