param([switch]$BuildOnly)
# Independent entry for the user's .19 window reports; shares only isolated
# three-peer startup, original native input queues, layout, and evidence tools.
& "$PSScriptRoot/Test-MultiplayerVisible.ps1" -Label window-interactions19 -Case window -BuildOnly:$BuildOnly
