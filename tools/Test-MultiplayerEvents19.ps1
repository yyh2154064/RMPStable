param([switch]$BuildOnly,[switch]$Remaining,[switch]$LanternOnly)
if($Remaining -and $LanternOnly){throw 'Select Remaining or LanternOnly'}
$eventCase=if($LanternOnly){'eventsLantern'}elseif($Remaining){'eventsRemaining'}else{'events'}
& "$PSScriptRoot/Test-MultiplayerVisible.ps1" -Label events19 -Case $eventCase -BuildOnly:$BuildOnly
