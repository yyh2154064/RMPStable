$ErrorActionPreference = "Stop"

$projectDir = $PSScriptRoot
$outputPath = Join-Path (Split-Path -Parent $projectDir) "RMPStable.pck"
$godot = Get-Command godot -ErrorAction SilentlyContinue

if (-not $godot) {
    $godot = Get-Command godot4 -ErrorAction SilentlyContinue
}

if (-not $godot) {
    throw "Godot 4.5.x was not found in PATH. Install Godot and expose godot.exe as 'godot' or 'godot4'."
}

& $godot.Source --headless --editor --path $projectDir --import --quit
if ($LASTEXITCODE -ne 0) {
    throw "Godot resource import failed with exit code $LASTEXITCODE."
}

& $godot.Source --headless --path $projectDir --export-pack "RMPStable Resources" $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "Godot PCK export failed with exit code $LASTEXITCODE."
}

Write-Host "Built: $outputPath"
