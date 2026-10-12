param([ValidateSet('defeat','victory')][string]$Outcome='defeat',[switch]$BuildOnly)
# Independent full native ending/authorized Continue test, three real peers.
$case=if($Outcome -eq 'victory'){'endingVictory'}else{'endingDefeat'}
& "$PSScriptRoot/Test-MultiplayerVisible.ps1" -Label ('ending19-'+$Outcome) -Case $case -BuildOnly:$BuildOnly
