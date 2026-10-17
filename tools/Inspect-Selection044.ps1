param(
    [string]$GameReferences='F:/projectFile/RMPStable-development/references/v0.111.0',
    [string]$OutputPath
)
$ErrorActionPreference='Stop'
# Read the actual game IL without starting Godot or invoking any game methods.
[Reflection.Assembly]::LoadFrom((Join-Path $GameReferences 'GodotSharp.dll')) | Out-Null
$game=[Reflection.Assembly]::LoadFrom((Join-Path $GameReferences 'sts2.dll'))
$opcodes=@{}
foreach($field in [Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static')) {
    $op=$field.GetValue($null); $opcodes[[int]$op.Value -band 65535]=$op
}
function Read-Calls([Reflection.MethodInfo]$Method) {
    $state=$Method.GetCustomAttributesData() | Where-Object AttributeType -Match 'AsyncStateMachineAttribute' | Select-Object -First 1
    if($state){$Method=$state.ConstructorArguments[0].Value.GetMethod('MoveNext',[Reflection.BindingFlags]'Instance,NonPublic,Public')}
    if(!$Method.GetMethodBody()){return}
    $body=$Method.GetMethodBody().GetILAsByteArray()
    for($offset=0;$offset -lt $body.Length) {
        $value=[int]$body[$offset++]; if($value -eq 254){$value=65024+[int]$body[$offset++]}
        $op=$opcodes[$value]; if(!$op){throw 'Unknown original IL opcode'}
        $size=switch($op.OperandType.ToString()) {
            'InlineNone'{0}; 'ShortInlineI'{1}; 'ShortInlineVar'{1}; 'ShortInlineBrTarget'{1}; 'InlineVar'{2}
            'InlineI8'{8}; 'InlineR'{8}; 'InlineSwitch'{4+4*[BitConverter]::ToInt32($body,$offset)}; default{4}
        }
        if($op.OperandType.ToString() -eq 'InlineMethod') {
            try {$called=$Method.Module.ResolveMethod([BitConverter]::ToInt32($body,$offset)); $called.DeclaringType.Name+'.'+$called.ToString()} catch {}
        }
        $offset+=$size
    }
}
$events=@()
foreach($type in $game.GetTypes() | Where-Object { $_.Namespace -eq 'MegaCrit.Sts2.Core.Models.Events' -and $_.IsSubclassOf($game.GetType('MegaCrit.Sts2.Core.Models.EventModel')) }) {
    foreach($method in $type.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly')) {
        $calls=@(Read-Calls $method)
        if($calls -match 'CardSelectCmd.*FromDeckForEnchantment') {$events+=@{model=$type.Name; method=$method.Name; calls=@($calls | Where-Object {$_ -match 'FromDeckForEnchantment|ModelDb.*Enchantment'})}}
    }
}
$relics=@()
foreach($name in 'BeautifulBracelet','ElectricShrymp','GnarledHammer','Kifuda','PaelsGrowth','PunchDagger','RoyalStamp','TriBoomerang','Claws','PandorasBox','ScrollBoxes') {
    $method=$game.GetType('MegaCrit.Sts2.Core.Models.Relics.'+$name).GetMethod('AfterObtained')
    $relics+=@{model=$name; calls=@(Read-Calls $method | Where-Object {$_ -match 'CardSelectCmd|ModelDb.*Enchantment|TakeRandom|CardCmd.*(Enchant|Transform)|ShowScreen'})}
}
$result=@{version=Split-Path $GameReferences -Leaf; gameSha256=(Get-FileHash (Join-Path $GameReferences 'sts2.dll')).Hash; enchantmentEvents=$events; relics=$relics}
$json=$result | ConvertTo-Json -Depth 8
if($OutputPath){$json | Set-Content -LiteralPath $OutputPath -Encoding utf8} else {$json}
