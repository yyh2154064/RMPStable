using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static int battleHandled;
    static object? battlePending;
    static void PrepareBattleDeck(RunState state,Player[] players)
    {
        foreach(var p in players)
        {
            foreach(var relic in new RelicModel[]{ModelDb.Relic<ChoicesParadox>(),ModelDb.Relic<BagOfPreparation>(),ModelDb.Relic<RingOfTheSnake>(),ModelDb.Relic<PhilosophersStone>(),ModelDb.Relic<Lantern>(),ModelDb.Relic<Ectoplasm>()})p.AddRelicInternal(relic.ToMutable(),silent:true);
            p.Deck.Clear(true);var grit=state.CreateCard<TrueGrit>(p);grit.UpgradeInternal();grit.FinalizeUpgradeInternal();p.Deck.AddInternal(grit,silent:true);
            p.Deck.AddInternal(state.CreateCard<Armaments>(p),silent:true);p.Deck.AddInternal(state.CreateCard<Headbutt>(p),silent:true);p.Deck.AddInternal(state.CreateCard<Discovery>(p),silent:true);
            var prepared=state.CreateCard<Prepared>(p);prepared.UpgradeInternal();prepared.FinalizeUpgradeInternal();p.Deck.AddInternal(prepared,silent:true);
            for(int i=0;i<4;i++)p.Deck.AddInternal(state.CreateCard<DefendIronclad>(p),silent:true);
            foreach(var potion in new PotionModel[]{System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="potions"?ModelDb.Potion<BloodPotion>():ModelDb.Potion<FocusPotion>(),ModelDb.Potion<BlockPotion>(),ModelDb.Potion<FirePotion>()})p.AddPotionInternal(potion.ToMutable(),silent:true);
        }
    }
    static async Task PrepareNoncombat(RunState state,Player[] players)
    {
        await Stage("第四阶段 · 非战斗房间准备");await Barrier("battle-to-room-fixture");
        foreach(var p in players){foreach(var relic in p.Relics.Where(r=>r is ChoicesParadox or Ectoplasm).ToArray())p.RemoveRelicInternal(relic,silent:true);p.Gold=1000;p.Creature.SetCurrentHpInternal(40);}
        await Barrier("noncombat-fixture-ready");
    }
    static async Task<JsonElement> RemoteBattle(string kind,string model="",int index=-1,int target=-1)
    {
        await Pace();int seq=++sequence;string dir=(string)Get(Proc,"DirectoryPath")!;WritePath(Path.Combine(dir,"visible-battle-input.json"),new{seq,kind,model,index,target});
        await Wait(()=>ReadPath(Path.Combine(dir,"visible-battle-output.json")).ValueKind==JsonValueKind.Object&&ReadPath(Path.Combine(dir,"visible-battle-output.json")).GetProperty("seq").GetInt32()==seq,"native battle operation "+kind+" "+model);
        var output=ReadPath(Path.Combine(dir,"visible-battle-output.json"));Check(output.GetProperty("issued").GetBoolean(),"source acknowledges visible replica "+kind+" "+model);return output.GetProperty("request").Clone();
    }
    static async Task ObserveBattle(object renderer,RunState state)
    {
        if(battlePending!=null){WritePath(Path.Combine(Root,"visible-battle-output.json"),battlePending);battlePending=null;}
        var input=ReadPath(Path.Combine(Root,"visible-battle-input.json"));if(input.ValueKind!=JsonValueKind.Object||input.GetProperty("seq").GetInt32()==battleHandled||Get(renderer,"MultiplayerInputReady") is not true)return;
        var json=JsonSerializer.SerializeToElement(Get(renderer,"_battleAuthority"));if(json.ValueKind!=JsonValueKind.Object)return;
        int seq=input.GetProperty("seq").GetInt32();string kind=input.GetProperty("kind").GetString()!,model=input.GetProperty("model").GetString()!;
        int index=input.GetProperty("index").GetInt32(),target=input.GetProperty("target").GetInt32();var me=LocalContext.GetMe(state)!;
        if(kind=="play")index=me.PlayerCombatState!.Hand.Cards.ToList().FindIndex(c=>c.GetType().Name==model);
        if(kind=="potion")index=me.Potions.ToList().FindIndex(p=>p.GetType().Name==model);
        var action=json.GetProperty("Actions").EnumerateArray().FirstOrDefault(a=>a.GetProperty("Enabled").GetBoolean()&&a.GetProperty("Kind").GetString()==kind&&(kind is "play" or "potion"?a.GetProperty("NativeIndex").GetInt32()==index:kind!="select"||(!a.GetProperty("NativePath").GetString()!.Contains("NSelectedHandCardHolder")&&(model.Length==0||a.GetProperty("CardId").GetString()!.Contains(model,StringComparison.OrdinalIgnoreCase)))));
        if(action.ValueKind!=JsonValueKind.Object)return;
        var potionTargets=MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress?me.Creature.CombatState.Creatures.ToList():state.Players.Select(p=>p.Creature).ToList();
        if(target==-3)target=potionTargets.FindIndex(c=>c.IsPlayer&&c.Player!.NetId==me.NetId%3+1);
        if(target==-4)target=potionTargets.IndexOf(me.Creature);
        if(target==-2)target=json.GetProperty("Targets").EnumerateArray().First(t=>t.GetProperty("Enemy").GetBoolean()).GetProperty("NativeIndex").GetInt32();
        var targetItem=json.GetProperty("Targets").EnumerateArray().FirstOrDefault(t=>t.GetProperty("NativeIndex").GetInt32()==target);
        long before=(long)Get(renderer,"_request")!,epoch=(long)Get(renderer,"_epoch")!,generation=(long)Get(renderer,"_generation")!;int events=(int)Get(renderer,"_events")!;
        if(kind=="potion")
        {
            var potion=me.Potions.ElementAt(index);var holder=Descendants<NPotionHolder>(NRun.Instance.GlobalUi.TopBar).First(h=>h.Potion?.Model==potion);await NativeClick(renderer,NativePoint(holder));
            var popup=Descendants<NPotionPopup>(NRun.Instance).First(p=>!p.IsMarkedForRemoval);await NativeClick(renderer,NativePoint((NClickableControl)Get(popup,"_useButton")!));
            if(NTargetManager.Instance.IsInSelection){if(target<0)target=potionTargets.IndexOf(me.Creature);Control hitbox=MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress?NRun.Instance.CombatRoom.GetCreatureNode(potionTargets[target]).Hitbox:Descendants<MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState>(NRun.Instance).First(p=>p.Player.Creature==potionTargets[target]).Hitbox;await NativeClick(renderer,NativePoint(hitbox));}
        }
        else if(kind is "select" or "choice" or "ending")
        {
            var node=Static("MirrorNativeUi","Resolve",action.GetProperty("NativePath").GetString()!,true) as Control;if(node==null)return;var hit=node is NCardHolder h?h.Hitbox:node;
            await NativeClick(renderer,NativePoint(hit));
        }
        else if(kind=="play"&&model=="Prepared"||kind=="endTurn")
        {
            var queue=(System.Collections.Concurrent.ConcurrentQueue<(uint,uint,long,long,long)>)Get(renderer,"_keyboard")!;
            string shortcut=kind=="endTurn"?"ui_end_turn":"mega_select_card_"+(NPlayerHand.Instance.ActiveHolders.ToList().FindIndex(h=>h.CardModel?.GetType().Name==model)+1);uint key=(uint)NInputManager.Instance.GetMKbHotkey(shortcut);
            queue.Enqueue((0x100,key,0,generation,epoch));await Frames(3);queue.Enqueue((0x101,key,0,generation,epoch));
            if(kind=="play"){await Frames(2);queue.Enqueue((0x100,13,0,generation,epoch));await Frames(2);queue.Enqueue((0x101,13,0,generation,epoch));}
        }
        else Call(renderer,"SendIntent",kind,index,target,Hash(state),events,action.GetProperty("NativePath").GetString()!,"");
        for(int i=0;i<60&&(long)Get(renderer,"_request")! ==before;i++)await Frames();long issued=(long)Get(renderer,"_request")!;if(issued==before)return;
        for(int i=0;i<120&&(long)Get(renderer,"LastMultiplayerResult")!<issued;i++)await Frames();
        if((long)Get(renderer,"LastMultiplayerResult")! !=issued)throw new Exception("No source acknowledgement for battle request "+issued);
        if(Get(renderer,"LastMultiplayerAccepted") is not true){await Frames(8);return;}
        battleHandled=seq;battlePending=new{seq,issued=true,request=new{Epoch=epoch,Request=issued,Generation=generation,Events=events,NodeKey=action.GetProperty("Id").GetString(),Value=json.GetProperty("Context").GetString(),TargetKey=targetItem.ValueKind==JsonValueKind.Object?targetItem.GetProperty("Id").GetString():""}};
        WritePath(Path.Combine(Root,"visible-battle-output.json"),battlePending);battlePending=null;
    }
    static void Forward(JsonElement request,Action<object>? change=null)
    {var message=JsonSerializer.Deserialize(request.GetRawText(),Rmp("MirrorMessage"))!;change?.Invoke(message);Static("MultiplayerMirrorSession","ForwardControlIntent",message);}
    static async Task GrantBattle(string tag,bool deny=false)
    {
        await Pace();((Button)Get(View!,"_controlToggle")!).EmitSignal(Button.SignalName.Toggled,true);
        await Wait(()=>Session("_pendingViewer") is ulong id&&id!=0,"source permission request");Check(Session("_controlViewer") is ulong id&&id==0,"another player cannot acquire control without source approval");
        await Wait(()=>Session(deny?"_denyButton":"_allowButton") is Button,"source permission panel");await Pace(2200);((Button)Session(deny?"_denyButton":"_allowButton")!).EmitSignal(Button.SignalName.Pressed);
        await Barrier(tag);await Wait(()=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("control").GetBoolean()!=deny,"source grant or denial reaches native replica");
        Check((bool)Get(View!,"ControlEnabled")! !=deny,"visible shell follows source "+(deny?"denial":"grant"));
    }
    static async Task SelectVisible(string kind,int count,string model,string tag)
    {
        await Wait(()=>ChoiceKind()==kind&&ChoiceMatches(),"native "+kind+" candidates match source");Check(ChoiceMatches(),kind+" selection contract follows selected source");
        if(kind!="Grid")
        {
            Static("MultiplayerMirrorSession","RevokeControl");await Barrier(tag+"-readonly-revoke");
            await Wait(()=>!Child().GetProperty("control").GetBoolean(),"readonly "+kind+" authority");
            await ContractReadonly(kind,tag+"-readonly");
            if(kind=="Upgrade")
            {
                int oldPid=Pid;long oldGeneration=(long)Get(Mirror,"LocalGeneration")!;((Button)Get(View!,"_refresh")!).EmitSignal(Button.SignalName.Pressed);
                await Wait(()=>(long)Get(Mirror,"LocalGeneration")!>oldGeneration&&(bool)Get(Proc,"DisplayReady")!&&ChoiceKind()==kind&&ChoiceMatches(),"reset while native upgrade is waiting");
                Check(Pid==oldPid&&ChoiceMatches(),"explicit reset reuses native process and restores upgrade without completing source choice");
                await Barrier(tag+"-rebuilt");
            }
            await GrantBattle(tag+"-grant");
        }
        for(int i=0;i<count;i++){await RemoteBattle("select",model);await Pace(600);}
        if(kind=="Hand"&&count==2)
        {
            await Barrier(tag+"-two-selected");
            var row=(Node)Get(NPlayerHand.Instance,"_selectedHandCardContainer")!;
            var first=row.GetChildren().OfType<NSelectedHandCardHolder>().First().CardModel!;
            Call(row,"DeselectCard",first);
            var holder=NPlayerHand.Instance.ActiveHolders.First(h=>h.CardModel==first);holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);
            Write(Role+"-selection-order.json",new{order=SelectedRow(state:RunManager.Instance.DebugOnlyGetState()!)});
            await Barrier(tag+"-reselected");
            await Wait(()=>Child().GetProperty("selectedRow").GetRawText()==ReadPath(Path.Combine(Root,SourceRole+"-selection-order.json")).GetProperty("order").GetRawText(),"rapid multi-selection row order");
            Check(Child().GetProperty("selectedRow").GetRawText()==ReadPath(Path.Combine(Root,SourceRole+"-selection-order.json")).GetProperty("order").GetRawText(),"rapid native deselect/reselect preserves selected row order");
        }
        await Pace();if(ChoiceKind()==kind&&Authority().GetProperty("Actions").EnumerateArray().Any(a=>a.GetProperty("Kind").GetString()=="choice"&&a.GetProperty("Id").GetString()!.StartsWith("confirm:")))await RemoteBattle("choice");await Barrier(tag);
    }
    static async Task PacketRejections()
    {
        await Stage("第一阶段 · 三人数据通路及消息边界");Check(Child().GetProperty("service").GetString()=="Replay"&&!Child().GetProperty("save").GetBoolean(),"native replica remains Replay and cannot save or join as a fourth player");
        Check(((System.Collections.ICollection)Static("MultiplayerMirrorSession","Sources")!).Count==3,"real roster includes exactly three connected players");
        int before=(int)Session("RejectedPackets")!;var frame=Activator.CreateInstance(Rmp("MultiplayerMirrorFrame"))!;frame.GetType().GetProperty("Kind")!.SetValue(frame,"subscribe");frame.GetType().GetProperty("Build")!.SetValue(frame,new string('x',50000));Rmp("MultiplayerMirrorSession").GetMethod("Send",Any)!.Invoke(null,new object[]{SourceId,Guid.NewGuid().ToString("N"),frame});
        await Wait(()=>(int)Session("RejectedPackets")!>before,"32 KiB fragmented incompatible subscription rejected");Check(Session("Error") as string==""&&Session("Mirror")!=null,"fragmented wrong build and obsolete token do not replace active view");
        var packetType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Network.RmpMirrorPacket")).First(t=>t!=null)!;var packet=Activator.CreateInstance(packetType)!;packetType.GetField("Origin")!.SetValue(packet,999UL);packetType.GetField("Destination")!.SetValue(packet,Id);before=(int)Session("RejectedPackets")!;Rmp("MultiplayerMirrorSession").GetMethod("Receive",Any)!.Invoke(null,new object[]{packet,Host?2UL:1UL});Check((int)Session("RejectedPackets")! ==before+1,"dispatch rejects forged sender identity");await Barrier("packet-boundaries");
    }
    static async Task VisibleBattle(RunState state,Player[] players)
    {
        await PacketRejections();await Stage("第二阶段 · F8、三角色来源及原版选择页");
        var entry=RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.Monster).coord);
        await Wait(()=>ChoiceKind()=="Grid"&&ChoiceMatches()&&(bool)Get(Proc,"DisplayReady")!,"opening native three-player grid");await Barrier("visible-opening-grid");
        int previous=Pid;await Hotkey();Check(View==null&&Pid==previous,"F8 hides the shell and keeps its replica alive");await Hotkey();await Wait(()=>View!=null,"F8 reopens visible window");Check(Pid==previous,"F8 reuses authenticated native replica");
        Check(((System.Collections.ICollection)Get(View!,"_sourceOptions")!).Count==3,"public navigation exposes three real player sources");
        for(int offset=0;offset<3;offset++)await ChooseSource((Id+(ulong)offset-1)%3+1,"roster-cycle-"+offset);
        await ChooseSource(DefaultSource,"restore-ring-source");await ContractReadonly("Grid","grid-readonly");
        previous=Pid;long previousGeneration=(long)Get(Mirror,"LocalGeneration")!;Static("LiveSharingController","RecoverMirror");await Wait(()=>(long)Get(Mirror,"LocalGeneration")!>previousGeneration&&(bool)Get(Proc,"DisplayReady")!&&ChoiceKind()=="Grid"&&ChoiceMatches(),"resident reset restores waiting native grid");Write(Role+"-replica-path.json",new{directory=Get(Proc,"DirectoryPath")});Check(Pid==previous&&ChoiceMatches(),"reset reuses native process and restores selected original waiting choice");await Barrier("rebuilt-grid");
        await Stage("第三阶段 · 来源授权与战斗操作");
        Check(!Child().GetProperty("control").GetBoolean(),"initial remote view has no gameplay authority");
        int rejected=(int)Session("RejectedControlRequests")!;Forward(JsonSerializer.SerializeToElement(new{Epoch=-1,Request=1,Generation=Get(Mirror,"LocalGeneration"),Events=0,NodeKey="end:1",Value="forged"}));await Barrier("unauthorized");await Wait(()=>(int)Session("RejectedControlRequests")!>rejected,"unauthorized rejected");Check((int)Session("AcceptedControlRequests")! ==0,"ungranted operation does not execute");
        await GrantBattle("denied",true);await GrantBattle("granted");await SelectVisible("Grid",1,"","grid-input");await entry;await Sync(state,"grid-result");
        await Wait(()=>Child().GetProperty("portraits").GetArrayLength()==3,"three original portraits");Check(Foreground(JsonSerializer.SerializeToElement(Portraits()),Id),"own original character is foremost among three distinct characters");Check(Foreground(Child().GetProperty("portraits"),SourceId),"selected source character is foremost in native replica");
        var replay=(CombatReplay)Get(null,"CurrentReplay",Rmp("MirrorState"))!;string immutable=Convert.ToHexString(SHA256.HashData((byte[])Static("MirrorState","Pack",replay.serializableRun)!));
        Check(replay.events.Any(e=>e.eventType==CombatReplayEventType.HookAction)&&replay.events.Any(e=>e.eventType==CombatReplayEventType.PlayerChoice),"original Hook and recorded choice are present");
        var me=LocalContext.GetMe(state)!;
        await BattleControls(state,players,me);
        Check(immutable==Convert.ToHexString(SHA256.HashData((byte[])Static("MirrorState","Pack",replay.serializableRun)!)),"window never mutates original battle checkpoint");
        Check(replay.events.Any(e=>e.eventType==CombatReplayEventType.GameAction)&&replay.events.Any(e=>e.eventType==CombatReplayEventType.ResumeAction),"original action and choice resume events are replayed");
        await Stage("第三阶段 · 心跳超时、撤权及来源切换");Rmp("MultiplayerMirrorSession").GetField("_nextControlHeartbeat",Any)!.SetValue(null,ulong.MaxValue);await Barrier("heartbeat-stopped");await Wait(()=>Session("_controlViewer") is ulong owner&&owner==0,"real heartbeat expires",25);await Barrier("lease-expired");await Wait(()=>!Child().GetProperty("control").GetBoolean(),"expired grant reaches replica");Check(!Child().GetProperty("control").GetBoolean(),"15-second heartbeat failure revokes control");Rmp("MultiplayerMirrorSession").GetField("_nextControlHeartbeat",Any)!.SetValue(null,0UL);
        await GrantBattle("before-switch");
        if(System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="battleSwitch19")
        {for(ulong actor=1;actor<=3;actor++){if(Id==actor)await ChooseSource(Id,"self-source",synchronize:false);await Barrier("self-source-"+actor);}}
        else await ChooseSource(Id,"self-source");
        await Wait(()=>Session("_controlViewer") is ulong owner&&owner==0,"old remote grant released");Check(Session("_controlViewer") is ulong owner&&owner==0,"source switch revokes exact old subscription capability");Static("MultiplayerMirrorSession","RequestControl",true);await Wait(()=>Child().GetProperty("control").GetBoolean(),"self-view grants own control");Check(Child().GetProperty("control").GetBoolean(),"own source can self-authorize");
        if(System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="battleSwitch19")
        {for(ulong actor=1;actor<=3;actor++){if(Id==actor)await ChooseSource(DefaultSource,"back-to-ring",synchronize:false);await Barrier("back-to-ring-"+actor);}}
        else await ChooseSource(DefaultSource,"back-to-ring");
        await Sync(state,"battle-source-restored");
        Check(Child().GetProperty("rendering").GetBoolean()&&!Child().GetProperty("fastReplica").GetBoolean(),"hidden catchup restores native drawing and ordinary playback before interaction");
    }
}
