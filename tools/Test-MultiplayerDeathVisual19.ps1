param([ValidateSet('defeat','victory')][string]$Outcome='defeat',[switch]$BuildOnly)
# This round's independent native death rendering test. Always background.
$case=if($Outcome -eq 'victory'){'deathVictory'}else{'deathDefeat'}
& "$PSScriptRoot/Test-MultiplayerVisible.ps1" -Label ('death-visual19-'+$Outcome) -Case $case -Silent -BuildOnly:$BuildOnly
