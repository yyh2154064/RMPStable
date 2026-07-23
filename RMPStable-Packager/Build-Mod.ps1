[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$GodotPath,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 3.0

$modId = "RMPStable"
$packagerDir = $PSScriptRoot
$repositoryDir = Split-Path -Parent $packagerDir
$sourceDir = Join-Path $repositoryDir $modId
$projectPath = Join-Path $sourceDir "src\RMPStable.csproj"
$nugetConfig = Join-Path $sourceDir "src\NuGet.Config"
$assetSourceDir = Join-Path $sourceDir "assets"
$manifestPath = Join-Path $sourceDir "RMPStable.json"
$updaterPath = Join-Path $packagerDir "update.cmd"
$outputDir = Join-Path $packagerDir "output"
$workDir = Join-Path $packagerDir ".work"

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Test-GameDirectory([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $false
    }

    $managed = Join-Path $Candidate "data_sts2_windows_x86_64"
    return (Test-Path -LiteralPath (Join-Path $managed "sts2.dll") -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $managed "GodotSharp.dll") -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $managed "Steamworks.NET.dll") -PathType Leaf)
}

function Resolve-GameDirectory([string]$ExplicitDirectory) {
    $candidates = New-Object System.Collections.Generic.List[string]

    if (-not [string]::IsNullOrWhiteSpace($ExplicitDirectory)) {
        $candidates.Add($ExplicitDirectory)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:STS2_GAME_DIR)) {
        $candidates.Add($env:STS2_GAME_DIR)
    }

    $steamRoots = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $steamRoots.Add((Join-Path ${env:ProgramFiles(x86)} "Steam"))
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $steamRoots.Add((Join-Path $env:ProgramFiles "Steam"))
    }
    foreach ($drive in Get-PSDrive -PSProvider FileSystem) {
        $steamRoots.Add((Join-Path $drive.Root "SteamLibrary"))
    }

    foreach ($steamRoot in $steamRoots) {
        $candidates.Add((Join-Path $steamRoot "steamapps\common\Slay the Spire 2"))
        $libraryFile = Join-Path $steamRoot "steamapps\libraryfolders.vdf"
        if (Test-Path -LiteralPath $libraryFile -PathType Leaf) {
            $content = Get-Content -Raw -LiteralPath $libraryFile
            foreach ($match in [regex]::Matches($content, '"path"\s+"([^"]+)"')) {
                $libraryRoot = $match.Groups[1].Value -replace '\\\\', '\'
                $candidates.Add((Join-Path $libraryRoot "steamapps\common\Slay the Spire 2"))
            }
        }
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-GameDirectory $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "找不到 Slay the Spire 2。请使用 -GameDir 指定游戏目录，或设置 STS2_GAME_DIR。"
}

function Install-PortableGodot {
    $godotVersion = "4.5.1"
    $expectedHash = "DEFCCC78669E644861B4247626B01AE362CD9F23975EDF19C8BFD2EB1F6A1783"
    $downloadUrl = "https://github.com/godotengine/godot-builds/releases/download/4.5.1-stable/Godot_v4.5.1-stable_win64.exe.zip"
    $toolsDir = Join-Path $packagerDir "tools"
    $installDir = Join-Path $toolsDir "godot-$godotVersion"
    $archivePath = Join-Path $toolsDir "Godot_v4.5.1-stable_win64.exe.zip"
    $executablePath = Join-Path $installDir "Godot_v4.5.1-stable_win64_console.exe"

    if (Test-Path -LiteralPath $executablePath -PathType Leaf) {
        return $executablePath
    }

    Write-Step "首次运行：下载官方 Godot $godotVersion 便携版（约 74 MB）"
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash
        if ($actualHash -ne $expectedHash) {
            throw "Godot 下载文件校验失败。期望：$expectedHash，实际：$actualHash"
        }
        if (Test-Path -LiteralPath $installDir) {
            Remove-Item -LiteralPath $installDir -Recurse -Force
        }
        Expand-Archive -LiteralPath $archivePath -DestinationPath $installDir -Force
    }
    finally {
        if (Test-Path -LiteralPath $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }
    }

    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "Godot 已下载，但没有找到预期程序：$executablePath"
    }
    return $executablePath
}

function Resolve-GodotExecutable([string]$ExplicitPath) {
    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $candidates.Add($ExplicitPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:GODOT_EXE)) {
        $candidates.Add($env:GODOT_EXE)
    }

    foreach ($commandName in @("godot", "godot4")) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue
        if ($null -ne $command) {
            $candidates.Add($command.Source)
        }
    }

    $searchDirectories = @(
        $packagerDir,
        (Join-Path $packagerDir "tools\godot-4.5.1"),
        (Join-Path $repositoryDir "tools"),
        (Join-Path $env:LOCALAPPDATA "Programs\Godot"),
        (Join-Path $env:ProgramFiles "Godot")
    )
    foreach ($directory in $searchDirectories) {
        if (Test-Path -LiteralPath $directory -PathType Container) {
            Get-ChildItem -LiteralPath $directory -Filter "Godot_v4.5*.exe" -File -ErrorAction SilentlyContinue |
                Sort-Object @{ Expression = { $_.Name -notmatch '_console\.exe$' } }, Name |
                ForEach-Object { $candidates.Add($_.FullName) }
        }
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $versionLines = @(& $candidate --version 2>&1)
            $versionExitCode = $LASTEXITCODE
            $version = $versionLines | Select-Object -First 1
            if ($versionExitCode -eq 0 -and "$version" -match '^4\.5(\.|\D)') {
                return (Resolve-Path -LiteralPath $candidate).Path
            }
        }
    }

    return Install-PortableGodot
}

function Invoke-Native([string]$FilePath, [string[]]$Arguments, [string]$FailureMessage) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage（退出代码：$LASTEXITCODE）"
    }
}

foreach ($requiredPath in @($projectPath, $nugetConfig, $assetSourceDir, $manifestPath, $updaterPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "缺少项目文件：$requiredPath"
    }
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.id -ne $modId -or [string]::IsNullOrWhiteSpace($manifest.version)) {
    throw "RMPStable.json 中的 id 或 version 无效。"
}
$safeVersion = "$($manifest.version)" -replace '[^0-9A-Za-z._-]', '_'

Write-Step "检查构建环境"
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    throw "找不到 dotnet。请安装 .NET 7 或更高版本的 SDK。"
}
$sdkLines = @(& $dotnet.Source --list-sdks 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "无法读取已安装的 .NET SDK 列表。"
}
$sdkMajors = @($sdkLines | Where-Object { "$_" -match '^(\d+)\.' } | ForEach-Object { [int]$Matches[1] })
if ($sdkMajors.Count -eq 0 -or ($sdkMajors | Measure-Object -Maximum).Maximum -lt 7) {
    throw "需要 .NET 7 或更高版本的 SDK；仅安装 .NET Runtime 不足以编译 Mod。"
}
$resolvedGameDir = Resolve-GameDirectory $GameDir
$gameManagedDir = Join-Path $resolvedGameDir "data_sts2_windows_x86_64"
$godot = Resolve-GodotExecutable $GodotPath
Write-Host "游戏目录：$resolvedGameDir"
Write-Host "Godot：$godot"

$resolvedPackagerDir = (Resolve-Path -LiteralPath $packagerDir).Path
$expectedWorkDir = Join-Path $resolvedPackagerDir ".work"
if ([System.IO.Path]::GetFullPath($workDir) -ne $expectedWorkDir) {
    throw "拒绝清理非预期工作目录：$workDir"
}
if (Test-Path -LiteralPath $workDir) {
    Remove-Item -LiteralPath $workDir -Recurse -Force
}

$managedOutputDir = Join-Path $workDir "managed"
$intermediateOutputDir = Join-Path $workDir "obj"
$assetWorkDir = Join-Path $workDir "assets"
$packageRoot = Join-Path $workDir "package"
$packageModDir = Join-Path $packageRoot $modId
New-Item -ItemType Directory -Force -Path $managedOutputDir, $assetWorkDir, $packageModDir, $outputDir | Out-Null

try {
    Write-Step "从 C# 源码生成 RMPStable.dll"
    Invoke-Native $dotnet.Source @(
        "restore", $projectPath,
        "--configfile", $nugetConfig,
        "-p:GameManagedDir=$gameManagedDir",
        "-p:BaseIntermediateOutputPath=$intermediateOutputDir\",
        "--nologo"
    ) "C# 项目还原失败"
    Invoke-Native $dotnet.Source @(
        "build", $projectPath,
        "--configuration", $Configuration,
        "--output", $managedOutputDir,
        "--no-restore",
        "-p:GameManagedDir=$gameManagedDir",
        "-p:BaseIntermediateOutputPath=$intermediateOutputDir\",
        "--nologo"
    ) "DLL 构建失败"

    $builtDll = Join-Path $managedOutputDir "RMPStable.dll"
    if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) {
        throw "构建完成但没有生成 RMPStable.dll。"
    }

    Write-Step "从 Godot 资源源码生成 RMPStable.pck"
    Get-ChildItem -LiteralPath $assetSourceDir -Force |
        Where-Object { $_.Name -ne ".godot" } |
        Copy-Item -Destination $assetWorkDir -Recurse -Force
    $builtPck = Join-Path $packageModDir "RMPStable.pck"
    $originalAppData = $env:APPDATA
    $originalLocalAppData = $env:LOCALAPPDATA
    $env:APPDATA = Join-Path $workDir "godot-profile\Roaming"
    $env:LOCALAPPDATA = Join-Path $workDir "godot-profile\Local"
    New-Item -ItemType Directory -Force -Path $env:APPDATA, $env:LOCALAPPDATA | Out-Null
    try {
        Invoke-Native $godot @(
            "--headless", "--editor", "--path", $assetWorkDir, "--import", "--quit"
        ) "Godot 资源导入失败"
        Invoke-Native $godot @(
            "--headless", "--path", $assetWorkDir,
            "--export-pack", "RMPStable Resources", $builtPck
        ) "PCK 构建失败"
    }
    finally {
        $env:APPDATA = $originalAppData
        $env:LOCALAPPDATA = $originalLocalAppData
    }

    Write-Step "组装并压缩 Mod"
    Copy-Item -LiteralPath $builtDll -Destination (Join-Path $packageModDir "RMPStable.dll") -Force
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $packageModDir "RMPStable.json") -Force
    Copy-Item -LiteralPath $updaterPath -Destination (Join-Path $packageModDir "update.cmd") -Force

    $zipPath = Join-Path $outputDir "RMPStable-v$safeVersion.zip"
    Compress-Archive -LiteralPath $packageModDir -DestinationPath $zipPath -CompressionLevel Optimal -Force

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    }
    finally {
        $archive.Dispose()
    }
    $expectedEntries = @(
        "RMPStable/RMPStable.dll",
        "RMPStable/RMPStable.pck",
        "RMPStable/RMPStable.json",
        "RMPStable/update.cmd"
    )
    foreach ($entry in $expectedEntries) {
        if ($entries -notcontains $entry) {
            throw "ZIP 校验失败，缺少：$entry"
        }
    }
    if ($entries.Count -ne $expectedEntries.Count) {
        throw "ZIP 校验失败：应仅包含 DLL、PCK、JSON 与 update.cmd，实际包含 $($entries.Count) 个文件。"
    }

    $zip = Get-Item -LiteralPath $zipPath
    Write-Host "`n打包成功：$($zip.FullName)" -ForegroundColor Green
    Write-Host ("ZIP 大小：{0:N2} MB" -f ($zip.Length / 1MB))
}
finally {
    if (Test-Path -LiteralPath $workDir) {
        Remove-Item -LiteralPath $workDir -Recurse -Force
    }
}
