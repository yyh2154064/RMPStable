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
$userAgent = 'RMPStable-Updater/0.3.5'

function Write-Step([string]$message) {
    Write-Host "`n==> $message" -ForegroundColor Cyan
}

function New-GitHubRequest([string]$url) {
    $request = [Net.HttpWebRequest]::Create($url)
    $request.Method = 'GET'
    $request.AllowAutoRedirect = $true
    $request.MaximumAutomaticRedirections = 10
    $request.UserAgent = $userAgent
    $request.Accept = 'text/html,application/json,application/octet-stream'
    $request.Timeout = 30000
    $request.ReadWriteTimeout = 120000
    if ($null -ne [Net.WebRequest]::DefaultWebProxy) {
        $request.Proxy = [Net.WebRequest]::DefaultWebProxy
        $request.Proxy.Credentials = [Net.CredentialCache]::DefaultNetworkCredentials
    }
    return $request
}

function Get-WebText([string]$url) {
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($null -ne $curl) {
        $curlText = @(& $curl.Source --location --fail --silent --show-error --connect-timeout 15 --max-time 60 --retry 2 --user-agent $userAgent $url 2>&1)
        if ($LASTEXITCODE -eq 0) {
            return $curlText -join "`n"
        }
        Write-Host "curl.exe text request failed; retrying with Windows .NET networking: $($curlText -join ' ')" -ForegroundColor Yellow
    }
    $request = New-GitHubRequest $url
    $response = $null
    $reader = $null
    try {
        $response = [Net.HttpWebResponse]$request.GetResponse()
        $reader = New-Object IO.StreamReader($response.GetResponseStream())
        return $reader.ReadToEnd()
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
        if ($null -ne $response) { $response.Dispose() }
    }
}

function Read-ModManifest([string]$path) {
    $json = [IO.File]::ReadAllText($path)
    $idMatch = [regex]::Match($json, '"id"\s*:\s*"([^"\\]+)"')
    $versionMatch = [regex]::Match($json, '"version"\s*:\s*"([^"\\]+)"')
    if (-not $idMatch.Success -or -not $versionMatch.Success) {
        throw "Invalid mod manifest: $path"
    }
    return New-Object PSObject -Property @{
        id = $idMatch.Groups[1].Value
        version = $versionMatch.Groups[1].Value
    }
}

function Compare-NumericVersions([string]$installed, [string]$published) {
    try {
        $installedCore = ($installed.Trim() -replace '^[vV]', '').Split('-')[0]
        $publishedCore = ($published.Trim() -replace '^[vV]', '').Split('-')[0]
        $installedVersion = New-Object Version($installedCore)
        $publishedVersion = New-Object Version($publishedCore)
        return $installedVersion.CompareTo($publishedVersion)
    }
    catch {
        Write-Host "Could not safely compare versions '$installed' and '$published'." -ForegroundColor Yellow
        return $null
    }
}

function Resolve-LatestReleaseTag {
    $latestUrl = "https://github.com/$repository/releases/latest"
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($null -ne $curl) {
        $effectiveOutput = @(& $curl.Source --location --fail --silent --show-error --connect-timeout 15 --max-time 60 --retry 2 --user-agent $userAgent --output NUL --write-out '%{url_effective}' $latestUrl 2>&1)
        $effectiveUrl = ($effectiveOutput -join '').Trim()
        if ($LASTEXITCODE -eq 0 -and $effectiveUrl -match '/releases/tag/([^/?#]+)') {
            return [Uri]::UnescapeDataString($Matches[1])
        }
        Write-Host "curl.exe release check failed; retrying with Windows .NET networking: $effectiveUrl" -ForegroundColor Yellow
    }
    $request = New-GitHubRequest $latestUrl
    $response = $null
    try {
        $response = [Net.HttpWebResponse]$request.GetResponse()
        $releaseUri = $response.ResponseUri
        if ($null -ne $releaseUri -and $releaseUri.AbsolutePath -match '/releases/tag/([^/]+)$') {
            return [Uri]::UnescapeDataString($Matches[1])
        }
        throw "GitHub did not redirect to a release tag: $releaseUri"
    }
    catch {
        Write-Host "GitHub Releases page was unavailable; trying the repository manifest." -ForegroundColor Yellow
        Write-Host "First method: $($_.Exception.Message)" -ForegroundColor DarkYellow
        $manifestUrl = "https://raw.githubusercontent.com/$repository/main/RMPStable/RMPStable.json"
        $manifestText = Get-WebText $manifestUrl
        $versionMatch = [regex]::Match($manifestText, '"version"\s*:\s*"([^"\\]+)"')
        if (-not $versionMatch.Success) {
            throw 'Could not determine the latest version from GitHub Releases or the repository manifest.'
        }
        return 'v' + $versionMatch.Groups[1].Value
    }
    finally {
        if ($null -ne $response) { $response.Dispose() }
    }
}

function Download-File([string]$url, [string]$destination) {
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($null -ne $curl) {
        $curlOutput = @(& $curl.Source --location --fail --silent --show-error --connect-timeout 15 --max-time 120 --retry 2 --user-agent $userAgent --output $destination $url 2>&1)
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $destination -PathType Leaf) -and (Get-Item -LiteralPath $destination).Length -gt 0) {
            Write-Host 'Download method: Windows curl.exe'
            return
        }
        if (Test-Path -LiteralPath $destination -PathType Leaf) {
            Remove-Item -LiteralPath $destination -Force
        }
        Write-Host "curl.exe failed; retrying with Windows .NET networking: $($curlOutput -join ' ')" -ForegroundColor Yellow
    }

    $request = New-GitHubRequest $url
    $response = $null
    $input = $null
    $output = $null
    try {
        $response = [Net.HttpWebResponse]$request.GetResponse()
        $input = $response.GetResponseStream()
        $output = [IO.File]::Create($destination)
        $buffer = New-Object byte[] 65536
        while (($count = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $output.Write($buffer, 0, $count)
        }
        Write-Host 'Download method: Windows .NET networking'
    }
    finally {
        if ($null -ne $output) { $output.Dispose() }
        if ($null -ne $input) { $input.Dispose() }
        if ($null -ne $response) { $response.Dispose() }
    }
}

try {
    Write-Host 'RMP Stable updater' -ForegroundColor Green
    Write-Host "Install directory: $modDirectory"

    $localManifestPath = Join-Path $modDirectory 'RMPStable.json'
    $localVersion = 'unknown'
    if (Test-Path -LiteralPath $localManifestPath -PathType Leaf) {
        $localManifest = Read-ModManifest $localManifestPath
        if ($localManifest.id -ne $modId) {
            throw "This updater is not inside a valid $modId mod directory."
        }
        $localVersion = [string]$localManifest.version
    }

    Write-Step 'Checking the latest published GitHub release'
    # Do not use api.github.com here. Anonymous API requests are limited to
    # 60 per hour per public IP, which is commonly exhausted by shared VPNs.
    # The normal release redirect is preferred. A raw repository manifest is
    # used as a fallback for machines/IPs where github.com returns HTTP 403.
    $releaseTag = Resolve-LatestReleaseTag
    $releaseVersion = $releaseTag.TrimStart('v', 'V')
    $assetName = "RMPStable-$releaseTag.zip"
    $downloadUrl = "https://github.com/$repository/releases/download/$releaseTag/$assetName"

    Write-Host "Installed version: $localVersion"
    Write-Host "Latest version:    $releaseVersion"
    Write-Host "Release asset:     $assetName"

    if ($localVersion -ne 'unknown') {
        $versionComparison = Compare-NumericVersions $localVersion $releaseVersion
        if ($null -eq $versionComparison) {
            throw 'Update cancelled because the installed and published versions could not be compared safely. Local files were not changed.'
        }
        if ($null -ne $versionComparison -and $versionComparison -gt 0) {
            Write-Host "`nNo update installed: local version $localVersion is newer than GitHub Latest $releaseVersion." -ForegroundColor Green
            Write-Host 'The local DLL, PCK, and JSON were left unchanged.'
            exit 0
        }
        if ($null -ne $versionComparison -and $versionComparison -eq 0) {
            Write-Host "`nAlready up to date: $localVersion." -ForegroundColor Green
            Write-Host 'The local DLL, PCK, and JSON were left unchanged.'
            exit 0
        }
    }

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("RMPStable-update-" + [Guid]::NewGuid().ToString('N'))
    $archivePath = Join-Path $tempRoot 'release.zip'
    $extractDirectory = Join-Path $tempRoot 'extracted'
    $backupDirectory = Join-Path $tempRoot 'backup'
    New-Item -ItemType Directory -Path $tempRoot, $extractDirectory, $backupDirectory | Out-Null

    Write-Step 'Downloading release package'
    Download-File $downloadUrl $archivePath
    if ((Get-Item -LiteralPath $archivePath).Length -le 0) {
        throw 'The downloaded release package is empty.'
    }

    Write-Step 'Validating release contents'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $extractDirectory)
    $downloadedModDirectory = Join-Path $extractDirectory $modId
    $requiredFiles = @('RMPStable.dll', 'RMPStable.pck', 'RMPStable.json')
    foreach ($fileName in $requiredFiles) {
        $candidate = Join-Path $downloadedModDirectory $fileName
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "The release package is missing $fileName."
        }
    }
    $newManifest = Read-ModManifest (Join-Path $downloadedModDirectory 'RMPStable.json')
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
    Write-Host "PowerShell version: $($PSVersionTable.PSVersion)"
    Write-Host 'Make sure the game is closed. If GitHub only works through a browser extension proxy, configure a Windows system proxy/TUN mode and try again.'
    exit 1
}
finally {
    if ($null -ne $tempRoot -and (Test-Path -LiteralPath $tempRoot -PathType Container)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
