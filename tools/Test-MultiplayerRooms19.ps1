param([switch]$BuildOnly)
# Current stage-four rooms only; no historical or stage-one/three replay suite.
& "$PSScriptRoot/Test-MultiplayerVisible.ps1" -Label rooms19 -Case rooms19 -Silent -BuildOnly:$BuildOnly
