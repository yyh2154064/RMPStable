param([switch]$BuildOnly)
# The user's current three-player four-stage acceptance scope, not a legacy suite.
& "$PSScriptRoot/Test-MultiplayerVisible.ps1" -Label four-stages19 -Case fourStages -Silent -BuildOnly:$BuildOnly
