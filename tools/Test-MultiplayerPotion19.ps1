param([switch]$BuildOnly)
# Independent entry for the user's multiplayer potion report. It reuses only
# native three-peer startup and visible input/evidence infrastructure.
& "$PSScriptRoot/Test-MultiplayerVisible.ps1" -Label multiplayer-potions19 -Case potions -BuildOnly:$BuildOnly
