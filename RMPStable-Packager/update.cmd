@echo off
setlocal
set "RMP_UPDATE_SELF=%~f0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$raw=[IO.File]::ReadAllText($env:RMP_UPDATE_SELF);$marker='#==RMPSTABLE_POWERSHELL==';$pos=$raw.LastIndexOf($marker);if($pos -lt 0){throw 'Updater payload is missing.'};Invoke-Expression $raw.Substring($pos+$marker.Length)"
set "RMP_UPDATE_EXIT=%ERRORLEVEL%"
echo.
if not "%RMP_UPDATE_EXIT%"=="0" echo Update failed. See the message above.
if "%RMP_UPDATE_EXIT%"=="0" echo You may now close this window.
pause
exit /b %RMP_UPDATE_EXIT%
#==RMPSTABLE_POWERSHELL==

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repository = 'yyh2154064/RMPStable'
$modId = 'RMPStable'
$modDirectory = Split-Path -Parent $env:RMP_UPDATE_SELF
$tempRoot = $null

function Write-Step([string]$message) {
    Write-Host "`n==> $message" -ForegroundColor Cyan
}

try {
    Write-Host 'RMP Stable updater' -ForegroundColor Green
    Write-Host "Install directory: $modDirectory"

    $localManifestPath = Join-Path $modDirectory 'RMPStable.json'
    $localVersion = 'unknown'
    if (Test-Path -LiteralPath $localManifestPath -PathType Leaf) {
        $localManifest = Get-Content -Raw -LiteralPath $localManifestPath | ConvertFrom-Json
        if ($localManifest.id -ne $modId) {
            throw "This updater is not inside a valid $modId mod directory."
        }
        $localVersion = [string]$localManifest.version
    }

    Write-Step 'Checking the latest published GitHub release'
    $headers = @{
        'User-Agent' = 'RMPStable-Updater'
        'Accept' = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    $releaseUrl = "https://api.github.com/repos/$repository/releases/latest"
    $release = Invoke-RestMethod -Uri $releaseUrl -Headers $headers -Method Get
    $releaseVersion = ([string]$release.tag_name).TrimStart('v', 'V')
    $preferredAssetName = "RMPStable-v$releaseVersion.zip"
    $asset = @($release.assets | Where-Object { $_.name -eq $preferredAssetName }) | Select-Object -First 1
    if ($null -eq $asset) {
        $asset = @($release.assets | Where-Object { $_.name -match '^RMPStable-v[0-9A-Za-z._-]+\.zip$' }) | Select-Object -First 1
    }
    if ($null -eq $asset) {
        throw "Release $($release.tag_name) does not contain an RMPStable-v*.zip asset."
    }
    $downloadUrl = [string]$asset.browser_download_url
    if (-not $downloadUrl.StartsWith('https://github.com/', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'GitHub returned an unexpected download URL.'
    }

    Write-Host "Installed version: $localVersion"
    Write-Host "Latest version:    $releaseVersion"
    Write-Host "Release asset:     $($asset.name)"

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("RMPStable-update-" + [Guid]::NewGuid().ToString('N'))
    $archivePath = Join-Path $tempRoot 'release.zip'
    $extractDirectory = Join-Path $tempRoot 'extracted'
    $backupDirectory = Join-Path $tempRoot 'backup'
    New-Item -ItemType Directory -Path $tempRoot, $extractDirectory, $backupDirectory | Out-Null

    Write-Step 'Downloading release package'
    Invoke-WebRequest -Uri $downloadUrl -Headers $headers -OutFile $archivePath -UseBasicParsing
    if ((Get-Item -LiteralPath $archivePath).Length -le 0) {
        throw 'The downloaded release package is empty.'
    }

    $digestProperty = $asset.PSObject.Properties['digest']
    if ($null -ne $digestProperty -and -not [string]::IsNullOrWhiteSpace([string]$digestProperty.Value)) {
        $digest = [string]$digestProperty.Value
        if ($digest -match '^sha256:([0-9a-fA-F]{64})$') {
            $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash
            if ($actualHash -ne $Matches[1]) {
                throw 'The downloaded release package failed its SHA-256 check.'
            }
            Write-Host 'SHA-256 check passed.'
        }
    }

    Write-Step 'Validating release contents'
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDirectory -Force
    $downloadedModDirectory = Join-Path $extractDirectory $modId
    $requiredFiles = @('RMPStable.dll', 'RMPStable.pck', 'RMPStable.json')
    foreach ($fileName in $requiredFiles) {
        $candidate = Join-Path $downloadedModDirectory $fileName
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "The release package is missing $fileName."
        }
    }
    $newManifest = Get-Content -Raw -LiteralPath (Join-Path $downloadedModDirectory 'RMPStable.json') | ConvertFrom-Json
    if ($newManifest.id -ne $modId -or [string]::IsNullOrWhiteSpace([string]$newManifest.version)) {
        throw 'The release package contains an invalid RMPStable.json.'
    }

    Write-Step 'Installing DLL, PCK, and manifest'
    $replacedFiles = New-Object System.Collections.Generic.List[string]
    try {
        foreach ($fileName in $requiredFiles) {
            $target = Join-Path $modDirectory $fileName
            $staged = "$target.update-new"
            Copy-Item -LiteralPath (Join-Path $downloadedModDirectory $fileName) -Destination $staged -Force
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                Copy-Item -LiteralPath $target -Destination (Join-Path $backupDirectory $fileName) -Force
            }
        }
        foreach ($fileName in $requiredFiles) {
            $target = Join-Path $modDirectory $fileName
            Move-Item -LiteralPath "$target.update-new" -Destination $target -Force
            $replacedFiles.Add($fileName)
        }
    }
    catch {
        foreach ($fileName in $replacedFiles) {
            $target = Join-Path $modDirectory $fileName
            $backup = Join-Path $backupDirectory $fileName
            if (Test-Path -LiteralPath $backup -PathType Leaf) {
                Copy-Item -LiteralPath $backup -Destination $target -Force
            }
            elseif (Test-Path -LiteralPath $target -PathType Leaf) {
                Remove-Item -LiteralPath $target -Force
            }
        }
        foreach ($fileName in $requiredFiles) {
            $staged = (Join-Path $modDirectory $fileName) + '.update-new'
            if (Test-Path -LiteralPath $staged -PathType Leaf) {
                Remove-Item -LiteralPath $staged -Force
            }
        }
        throw
    }

    Write-Host "`nUpdate complete: $localVersion -> $($newManifest.version)" -ForegroundColor Green
    Write-Host 'RMPStable.dll, RMPStable.pck, and RMPStable.json were updated.'
    exit 0
}
catch {
    Write-Host "`nUpdate error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Make sure the game is closed and that GitHub is reachable, then try again.'
    exit 1
}
finally {
    if ($null -ne $tempRoot -and (Test-Path -LiteralPath $tempRoot -PathType Container)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
