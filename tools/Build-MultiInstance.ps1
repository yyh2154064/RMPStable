param(
    [Parameter(Mandatory=$true)][string]$GameReferences,
    [Parameter(Mandatory=$true)][string]$ModPck,
    [string]$OutputRoot = 'F:/projectFile/RMPStable-development/build'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/..").Path
$root = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force $root | Out-Null
foreach ($target in @(@('v0.107.1','stable','v0107'), @('v0.111.0','beta','v0111'))) {
    & dotnet build "$repo/RMPStable/src/RMPStable.csproj" -c Release --configfile "$repo/RMPStable/src/NuGet.Config" "-p:GameManagedDir=$GameReferences/$($target[0])" "-p:GameCompatibility=$($target[1])" "-p:AssemblyName=RMPStablePayload_$($target[2])" "-p:BaseIntermediateOutputPath=$root/obj-$($target[2])/" -p:RestoreFallbackFolders= --output "$root/payload-$($target[2])"
    if ($LASTEXITCODE -ne 0) { throw 'Payload build failed' }
}
& dotnet build "$repo/RMPStable-Bootstrap/RMPStable-Bootstrap.csproj" -c Release --configfile "$repo/RMPStable/src/NuGet.Config" "-p:GameManagedDir=$GameReferences/v0.107.1" "-p:StablePayloadPath=$root/payload-v0107/RMPStablePayload_v0107.dll" "-p:BetaPayloadPath=$root/payload-v0111/RMPStablePayload_v0111.dll" "-p:BaseIntermediateOutputPath=$root/obj-bootstrap/" -p:RestoreFallbackFolders= --output "$root/bootstrap"
if ($LASTEXITCODE -ne 0) { throw 'Bootstrap build failed' }
& dotnet build "$repo/tests/MultiInstanceSmoke/MultiInstanceSmoke.csproj" -c Release --configfile "$repo/RMPStable/src/NuGet.Config" "-p:GameManagedDir=$GameReferences/v0.111.0" -p:GameCompatibility=beta "-p:BaseIntermediateOutputPath=$root/obj-test/" -p:RestoreFallbackFolders= --output "$root/test"
if ($LASTEXITCODE -ne 0) { throw 'Smoke harness build failed' }
# This script only builds. It never starts a game or deploys to its directory.
$package = "$root/package/RMPStable"
New-Item -ItemType Directory -Force $package | Out-Null
Copy-Item -LiteralPath "$root/bootstrap/RMPStable.dll", "$repo/RMPStable/RMPStable.json", $ModPck -Destination $package -Force
Copy-Item -LiteralPath "$repo/docs/README-multiInstance.md" -Destination $package -Force
Write-Output "Package: $package"
