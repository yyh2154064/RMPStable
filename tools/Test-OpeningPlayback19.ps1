param([ValidateSet('single','multi')][string]$Mode='single',[switch]$Unupgraded,[switch]$BuildOnly)
# Only the newly reported Imbued Catastrophe opening playback after mod SL.
$case=if($Mode -eq 'single'){'openingSingle'}elseif($Unupgraded){'openingMulti2'}else{'openingMulti3'}
& "$PSScriptRoot/Test-MultiplayerVisible.ps1" -Label $case -Case $case -Silent -BuildOnly:$BuildOnly
