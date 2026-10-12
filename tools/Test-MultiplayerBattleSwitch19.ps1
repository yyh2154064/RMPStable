param([switch]$BuildOnly)
# Current battle-history source-switch scope; room flows have an independent entry.
& "$PSScriptRoot/Test-MultiplayerVisible.ps1" -Label battle-switch19 -Case battleSwitch19 -Silent -BuildOnly:$BuildOnly
