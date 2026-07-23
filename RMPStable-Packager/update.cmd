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
        'Accept' = 'text/html,application/xhtml+xml,application/octet-stream'
    }

    # Do not use api.github.com here. Anonymous API requests are limited to
    # 60 per hour per public IP, which is commonly exhausted by shared VPNs.
    # GitHub's ordinary /releases/latest redirect provides the same tag
    # without consuming that API quota.
    $latestUrl = "https://github.com/$repository/releases/latest"
    $request = [Net.HttpWebRequest]::Create($latestUrl)
    $request.Method = 'GET'
    $request.AllowAutoRedirect = $true
    $request.MaximumAutomaticRedirections = 10
    $request.UserAgent = $headers['User-Agent']
    $request.Accept = $headers['Accept']
    $request.Timeout = 30000
    $request.ReadWriteTimeout = 30000
    if ($null -ne [Net.WebRequest]::DefaultWebProxy) {
        $request.Proxy = [Net.WebRequest]::DefaultWebProxy
        $request.Proxy.Credentials = [Net.CredentialCache]::DefaultNetworkCredentials
    }
    $response = $null
    try {
        $response = [Net.HttpWebResponse]$request.GetResponse()
        $releaseUri = $response.ResponseUri
    }
    finally {
        if ($null -ne $response) {
            $response.Dispose()
        }
    }
    if ($null -eq $releaseUri -or $releaseUri.AbsolutePath -notmatch '/releases/tag/([^/]+)$') {
        throw "GitHub did not redirect to a release tag: $releaseUri"
    }
    $releaseTag = [Uri]::UnescapeDataString($Matches[1])
    $releaseVersion = $releaseTag.TrimStart('v', 'V')
    $assetName = "RMPStable-$releaseTag.zip"
    $downloadUrl = "https://github.com/$repository/releases/download/$releaseTag/$assetName"

    Write-Host "Installed version: $localVersion"
    Write-Host "Latest version:    $releaseVersion"
    Write-Host "Release asset:     $assetName"

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
