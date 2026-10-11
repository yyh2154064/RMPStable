using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using Env = System.Environment;

// Phase two only: native window navigation and personal UI. No historical suite.
[ModInitializer(nameof(Initialize))]
public static class MultiplayerPhase2Probe
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static bool Renderer => Env.GetEnvironmentVariable("RMP_MULTI_ROLE") == "renderer";
    static bool Host => Env.GetEnvironmentVariable("RMP_PHASE2_ROLE") == "host";
    static string Root => Renderer ? Env.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")! : Env.GetEnvironmentVariable("RMP_PHASE2_EVIDENCE")!;
    static string Role => Host ? "host" : "client";
    static int checks;
    static Type Rmp(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing." + name)).First(t => t != null)!;
    static object? Get(object? o, string name, Type? type = null)
    { for (var t = o?.GetType() ?? type; t != null; t = t.BaseType) { if(t.GetProperty(name,Any|BindingFlags.DeclaredOnly) is {} p && (o!=null||p.GetMethod!.IsStatic)) return p.GetValue(o); if(t.GetField(name,Any|BindingFlags.DeclaredOnly) is {} f && (o!=null||f.IsStatic)) return f.GetValue(o); } return null; }
    static object? Call(object? o, string name, params object[] args) => o!.GetType().GetMethod(name,Any)!.Invoke(o,args);
    static object? Static(string type, string name, params object[] args) => Rmp(type).GetMethod(name,Any)!.Invoke(null,args);
    static object? Session(string name) => Get(null,name,Rmp("MultiplayerMirrorSession"));
    static object? View => Get(null,"_view",Rmp("LiveSharingController"));
    static object Mirror => Session("Mirror")!;
    static object Proc => Get(Mirror,"Process")!;
    static int Pid => ((Process)Get(Proc,"Child")!).Id;
    static string Hash(RunState state) => (string)Static("MirrorState","MultiplayerHash",state)!;
    static async Task Frames(int n=1) {var tree=(SceneTree)Engine.GetMainLoop(); for(int i=0;i<n;i++) await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    static async Task Wait(Func<bool> c,string label,int seconds=75)
    {ulong end=Time.GetTicksMsec()+(ulong)(seconds*1000); while(Time.GetTicksMsec()<end) {if(c())return; if(Session("Error") is string e && e.Length>0) throw new Exception(e); await Frames();} throw new Exception("Timeout: "+label+" phase="+Get(Session("Mirror") is {} m?Get(m,"Process"):null,"Phase",Rmp("MirrorProcess"))+" mirror="+Get(Session("Mirror"),"Error",Rmp("MirrorHost")));}
    static void Check(bool c,string label)
    {File.AppendAllText(Path.Combine(Root,Role+"-checks.jsonl"),JsonSerializer.Serialize(new{label,passed=c})+"\n"); if(!c)throw new Exception(label); checks++; GD.Print("[MultiplayerPhase2] PASS "+label);}
    static void Write(string name,object value) {string p=Path.Combine(Root,name); File.WriteAllText(p+".tmp",JsonSerializer.Serialize(value)); File.Move(p+".tmp",p,true);}
    static JsonElement ReadPath(string p) {try{using var s=new FileStream(p,FileMode.Open,System.IO.FileAccess.Read,FileShare.ReadWrite|FileShare.Delete); using var d=JsonDocument.Parse(s);return d.RootElement.Clone();}catch{return default;}}
    static async Task Barrier(string stage) {Write(Role+"-"+stage+"-barrier.json",new{ready=true});await Wait(()=>File.Exists(Path.Combine(Root,(Host?"client":"host")+"-"+stage+"-barrier.json")),stage);}
    static JsonElement Child() => Session("Mirror")==null?default:ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"phase2-state.json"));
    static JsonElement Choice() {var c=Child();return c.ValueKind==JsonValueKind.Object?c.GetProperty("choice"):default;}
    static string Kind() => Choice().ValueKind==JsonValueKind.Object?Choice().GetProperty("Kind").GetString()!:"";
    static IEnumerable<NCardHolder> Holders(Node n) {foreach(var c in n.GetChildren()){if(c is NCardHolder h)yield return h;foreach(var h2 in Holders(c))yield return h2;}}
    static IEnumerable<NCreature> Creatures(Node n) {foreach(var c in n.GetChildren()){if(c is NCreature h)yield return h;foreach(var h2 in Creatures(c))yield return h2;}}
    static bool Foreground(JsonElement child,ulong id)
    {var all=child.GetProperty("portraits").EnumerateArray().ToArray();var mine=all.FirstOrDefault(p=>p.GetProperty("id").GetUInt64()==id);return all.Length==2&&mine.ValueKind==JsonValueKind.Object&&all.All(p=>p.GetProperty("x").GetSingle()<=mine.GetProperty("x").GetSingle()&&p.GetProperty("index").GetInt32()<=mine.GetProperty("index").GetInt32());}
    public static void Initialize()
    {if(Env.GetEnvironmentVariable("RMP_PHASE2_TEST")!="1")return; TaskHelper.RunSafely(Renderer?Observe():Run()); if(!Renderer)TaskHelper.RunSafely(Diagnostics());}
    static async Task Diagnostics()
    {while(true){await Frames(30);var state=RunManager.Instance.DebugOnlyGetState();if(state==null||RunManager.Instance.ActionExecutor==null)continue;Write(Role+"-fixture.json",new{room=state?.CurrentRoom?.RoomType.ToString(),overlay=NOverlayStack.Instance?.Peek()?.GetType().Name,mode=NPlayerHand.Instance?.CurrentMode.ToString(),starting=CombatManager.Instance.IsStarting,paused=RunManager.Instance.ActionExecutor.IsPaused,running=RunManager.Instance.ActionExecutor.IsRunning,view=View!=null,selected=Session("Selected"),mirrorError=Get(Session("Mirror"),"Error"),phase=Get(Session("Mirror") is {} m?Get(m,"Process"):null,"Phase"),cards=LocalContext.GetMe(state)?.PlayerCombatState?.Hand.Cards.Select(c=>c.Id.ToString()).ToArray()});}}
    static async Task Observe()
    {
        string readonlyKind=""; uint readonlyId=uint.MaxValue;
        while(true)
        {
            await Frames(3); var state=RunManager.Instance.DebugOnlyGetState();if(state==null)continue;
            try
            {
                var choice=Static("MultiplayerPersonalUi","Capture");
                string currentKind=(string?)Get(choice,"Kind")??"";
                uint currentId=choice==null?uint.MaxValue:(uint)Get(choice,"Id")!;
                if(File.Exists(Path.Combine(Root,"try-input"))&&File.ReadAllText(Path.Combine(Root,"try-input"))==currentKind&&(readonlyKind!=currentKind||readonlyId!=currentId)&&choice!=null)
                {
                    Node? v=Get(null,"_view",Rmp("MultiplayerPersonalUi")) as Node;
                    if(v is NPlayerHand hand) {hand.ActiveHolders.First().EmitSignal(NCardHolder.SignalName.Pressed,hand.ActiveHolders.First());((NClickableControl)Get(hand,"_selectModeConfirmButton")!).ForceClick();}
                    else if(v!=null) {var h=Holders(v).FirstOrDefault();if(h!=null) h.EmitSignal(NCardHolder.SignalName.Pressed,h);if(Get(v,"_confirmButton") is NClickableControl b)b.ForceClick();if(Get(v,"_skipButton") is NClickableControl skip)skip.ForceClick();}
                    readonlyKind=currentKind; readonlyId=currentId;
                }
                Node? active=Get(null,"_view",Rmp("MultiplayerPersonalUi")) as Node;
                object? completion=active==null?null:Get(active,active is NPlayerHand?"_selectionCompletionSource":"_completionSource");
                Write("phase2-state.json",new{local=LocalContext.NetId,choice,active=active?.GetType().Name,readonlyChecked=readonlyKind==currentKind&&readonlyId==currentId&&currentKind.Length>0,
                    selectedRow=active is NPlayerHand && Get(active,"_selectedHandCardContainer") is Node row?row.GetChildren().OfType<NSelectedHandCardHolder>().Select(h=>LocalContext.GetMe(state)!.PlayerCombatState!.Hand.Cards.ToList().IndexOf(h.CardModel!)).ToArray():null,
                    completed=completion==null?(bool?)null:((Task)Get(completion,"Task")!).IsCompleted,
                    hash=Hash(state),events=Get(Get(null,"Active",Rmp("MirrorRenderer")),"_events"),
                    portraits=NRun.Instance?.CombatRoom is {} room?Creatures(room).Where(c=>c.Entity.IsPlayer).Select(c=>new{id=c.Entity.Player!.NetId,x=c.Position.X,index=c.GetIndex()}).ToArray():null,
                    control=Get(Get(null,"Active",Rmp("MirrorRenderer")),"_control")});
            }
            catch(IOException){} catch(UnauthorizedAccessException){} catch(Exception e){GD.PrintErr("[Phase2Observer] "+e);throw;}
        }
    }
    static async Task Hotkey()
    {Input.ParseInputEvent(new InputEventKey{Keycode=Key.F8,Pressed=true});await Frames(3);Input.ParseInputEvent(new InputEventKey{Keycode=Key.F8,Pressed=false});await Frames(3);}
    static async Task UiReady(string kind)
    {await Wait(()=>Kind()==kind && Choice().GetProperty("Cards").GetArrayLength()>0 && (bool)Get(Proc,"DisplayReady")!,kind+" replica native choice");}
    static async Task Contract(string kind,string tag="")
    {
        if(tag.Length==0)tag=kind;
        await UiReady(kind);
        var mine=JsonSerializer.SerializeToElement(Static("MultiplayerPersonalUi","Capture"));
        Write(Role+"-"+tag+"-source.json",mine);await Barrier(tag+"-metadata");
        var remote=ReadPath(Path.Combine(Root,(Host?"client":"host")+"-"+tag+"-source.json"));
        var replica=Choice();
        Check(remote.GetProperty("Cards").GetArrayLength()>0&&replica.GetProperty("Cards").GetArrayLength()>0,kind+" original and replica expose nonempty native candidate lists");
        Check(new[]{"Id","Kind","Prompt","Min","Max","Manual","Cancelable","CanSkip","Cards","Eligible"}.All(k=>remote.GetProperty(k).GetRawText()==replica.GetProperty(k).GetRawText()),kind+" original candidates, prompt and selection constraints match selected player");
        string dir=(string)Get(Proc,"DirectoryPath")!;
        File.WriteAllText(Path.Combine(dir,"try-input"),kind);
        await Wait(()=>Child().GetProperty("readonlyChecked").GetBoolean(),kind+" native input rejection");
        await Frames(10);
        Check(Child().GetProperty("completed").ValueKind==JsonValueKind.False && Kind()==kind && Child().GetProperty("control").ValueKind==JsonValueKind.False,kind+" replica click/confirm cannot resolve authoritative choice");
        Check(Static("MultiplayerPersonalUi","Capture")!=null,kind+" source remains waiting for its own decision");
        await Barrier(tag+"-readonly");
    }
    static async Task Synced(RunState state,string label)
    {
        await Wait(()=>Session("Mirror") is {} m&&(bool)Get(m,"Verified")!&&Get(m,"SourceHash") as string==Hash(state)&&Kind()=="",label+" replica converges");
        Check((string)Get(Proc,"LastHash")! == Hash(state),label+" recorded result closes personal UI and state agrees");
        await Barrier(label+"-done");
    }
    static async Task ExitPid(int pid)
    {await Wait(()=>{try{using var p=Process.GetProcessById(pid);return p.HasExited;}catch(ArgumentException){return true;}},"old replica exits",20);}
    static async Task Run()
    {
        int exit=0;
        try
        {
            await Wait(()=>NGame.Instance?.MainMenu!=null,"main menu");await Frames(60);
            Engine.MaxFps=30;SaveManager.Instance.SetFtuesEnabled(false);Static("LiveSharingController","Suspend");
            var registration=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Network.PayloadMessageRegistration")).First(t=>t!=null)!;
            registration.GetMethod("EnsureRegistered",Any)!.Invoke(null,null);
            ushort port=ushort.Parse(Env.GetEnvironmentVariable("RMP_PHASE2_PORT")!);INetGameService service;
            if(Host)
            {
                var host=new NetHostGameService(PeerVersionInfo.LocalDefault());service=host;
                if(host.StartENetHost(port,2).HasValue)throw new Exception("Host failed");Write("host-listening.json",new{port});
                ulong end=Time.GetTicksMsec()+60000;while(host.ConnectedPeers.Count==0&&Time.GetTicksMsec()<end){host.Update();await Frames();}
                if(host.ConnectedPeers.Count!=1)throw new Exception("Client missing");host.SetPeerReadyForBroadcasting(2);
            }
            else
            {
                await Wait(()=>File.Exists(Path.Combine(Root,"host-listening.json")),"host listening");
                var client=new NetClientGameService(PeerVersionInfo.LocalDefault());service=client;var transport=new ENetClient(client);
                client.Initialize(transport,MegaCrit.Sts2.Core.Platform.PlatformType.None);
                if((await transport.ConnectToHost(2,"127.0.0.1",port)).HasValue)throw new Exception("Client failed");
            }
            var players=new[]{Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),1),Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),2)};
            var state=RunState.CreateForNewRun(players,ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPMULTIPHASE2");
            foreach(var player in players)
            {
                player.AddRelicInternal(ModelDb.Relic<ChoicesParadox>().ToMutable(),silent:true);
                player.AddRelicInternal(ModelDb.Relic<BagOfPreparation>().ToMutable(),silent:true);
                player.AddRelicInternal(ModelDb.Relic<RingOfTheSnake>().ToMutable(),silent:true);
                player.AddRelicInternal(ModelDb.Relic<PhilosophersStone>().ToMutable(),silent:true);
                player.AddRelicInternal(ModelDb.Relic<Sozu>().ToMutable(),silent:true);
                player.Deck.Clear(true);var grit=state.CreateCard<TrueGrit>(player);grit.UpgradeInternal();grit.FinalizeUpgradeInternal();player.Deck.AddInternal(grit,silent:true);
                player.Deck.AddInternal(state.CreateCard<Armaments>(player),silent:true);player.Deck.AddInternal(state.CreateCard<Discovery>(player),silent:true);player.Deck.AddInternal(state.CreateCard<Headbutt>(player),silent:true);
                var prepared=state.CreateCard<Prepared>(player);prepared.UpgradeInternal();prepared.FinalizeUpgradeInternal();player.Deck.AddInternal(prepared,silent:true);
                for(int i=0;i<4;i++)player.Deck.AddInternal(state.CreateCard<DefendIronclad>(player),silent:true);
            }
            RunManager.Instance.SetUpTest(state,service,disableCombatStateSync:false,shouldSave:false);RunManager.Instance.GenerateRooms();
            await PreloadManager.LoadRunAssets(players.Select(p=>p.Character));RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);
            var entry=RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.Monster).coord);
            await Wait(()=>NOverlayStack.Instance?.Peek() is NSimpleCardSelectScreen,"native ChoicesParadox grid");
            // Only game-local events: no Windows key, pointer, focus or real process.
            await Hotkey();await Wait(()=>View!=null&&Session("Mirror")!=null,"F8 public multiplayer view");
            Check((ulong)Session("Selected")! ==(Host?2UL:1UL),"F8 defaults to another actual player");
            var sources=(ICollection)Get(View!,"_sourceOptions")!;
            Check(sources.Count==2 && ((Button)Get(View!,"_controlToggle")!).Disabled,"two real roster entries and disabled control mode");
            await Contract("Grid");
            var grid=(Node)NOverlayStack.Instance.Peek()!;var gridHolder=Holders(grid).First(h=>h.CardModel is not TrueGrit and not Armaments and not Discovery and not Headbutt and not DefendIronclad);
            gridHolder.EmitSignal(NCardHolder.SignalName.Pressed,gridHolder);
            if(Get(grid,"_confirmButton") is NClickableControl gridConfirm&&gridConfirm.IsEnabled)gridConfirm.ForceClick();
            await entry;await Wait(()=>!CombatManager.Instance.IsStarting&&!RunManager.Instance.ActionExecutor.IsRunning,"first turn ready");await Barrier("first-turn");await Frames(60);
            await Synced(state,"grid-result");
            int originalPid=Pid;await Hotkey();Check(View==null&&Pid==originalPid,"F8 closes shell while keeping owned subscription");
            await Hotkey();await Wait(()=>View!=null,"F8 reopen");
            Check(Pid==originalPid,"F8 reopen reuses selected replica");
            var me=players[Host?0:1];ulong mine=me.NetId;
            // Selecting an actual player button must replace observation identity.
            var slots=(IList)Get(View!,"_sourceButtons")!;
            ((Button)Get(slots[Host?0:1],"Item1")!).EmitSignal(Button.SignalName.Pressed);
            await Wait(()=>(ulong)Session("Selected")! == mine,"real roster arrow");await ExitPid(originalPid);
            await Wait(()=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("local").ValueKind==JsonValueKind.Number&&Child().GetProperty("local").GetUInt64()==mine&&Get(Mirror,"SourceHash") as string==Hash(state),"new observation identity");
            Check(LocalContext.NetId==mine&&Child().GetProperty("local").GetUInt64()==mine,"switch changes replica identity and preserves original player");
            await Wait(()=>Child().GetProperty("portraits").ValueKind==JsonValueKind.Array&&Foreground(Child(),mine),"switched player's native portrait is foremost");
            Check(Foreground(Child(),mine),"source switch retains all portraits with selected player foremost");
            Static("LiveSharingController","SelectMultiplayerSource",(Host?2UL:1UL).ToString());
            await Synced(state,"switch-back");
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState!.Hand.Cards.OfType<Prepared>().Single(),null));
            await Wait(()=>NPlayerHand.Instance.CurrentMode==NPlayerHand.Mode.SimpleSelect,"Prepared two-card selection");await Contract("Hand","multi-hand");
            var two=new[]{NPlayerHand.Instance.ActiveHolders.First(h=>h.CardModel is DefendIronclad),NPlayerHand.Instance.ActiveHolders.First(h=>h.CardModel is not TrueGrit and not Armaments and not Discovery and not Headbutt and not Prepared and not DefendIronclad)};
            var twoCards=two.Select(h=>h.CardModel!).ToArray();foreach(var h in two)h.EmitSignal(NCardHolder.SignalName.Pressed,h);
            await Wait(()=>Choice().GetProperty("Selected").GetArrayLength()==2,"two selected highlights");await Barrier("two-highlight");
            Check(Choice().GetProperty("Max").GetInt32()==2&&Child().GetProperty("completed").ValueKind==JsonValueKind.False,"two-card native selector remains pending until source confirmation");
            // Cancel/reselect within one metadata interval: same set, reversed row.
            Call(Get(NPlayerHand.Instance,"_selectedHandCardContainer"),"DeselectCard",twoCards[0]);
            var reorder=NPlayerHand.Instance.ActiveHolders.First(h=>h.CardModel==twoCards[0]);reorder.EmitSignal(NCardHolder.SignalName.Pressed,reorder);
            Write(Role+"-selection-order.json",new{order=twoCards.Reverse().Select(c=>me.PlayerCombatState!.Hand.Cards.ToList().IndexOf(c)).ToArray()});await Barrier("two-reordered");
            var expectedOrder=ReadPath(Path.Combine(Root,(Host?"client":"host")+"-selection-order.json")).GetProperty("order").GetRawText();
            await Wait(()=>Child().GetProperty("selectedRow").GetRawText()==expectedOrder,"native selected row follows source ordering");
            Check(Child().GetProperty("selectedRow").GetRawText()==expectedOrder,"deselect/reselect preserves native multi-selection row ordering");
            await Barrier("two-order-shown");((NClickableControl)Get(NPlayerHand.Instance,"_selectModeConfirmButton")!).ForceClick();
            await Wait(()=>players.All(p=>p.PlayerCombatState!.DiscardPile.Cards.Count==3)&&!RunManager.Instance.ActionExecutor.IsRunning,"Prepared source result");await Synced(state,"multi-hand-result");
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState!.Hand.Cards.OfType<DefendIronclad>().First(),null));
            await Wait(()=>players.All(p=>p.PlayerCombatState!.DiscardPile.Cards.Count>=4)&&!RunManager.Instance.ActionExecutor.IsRunning,"native discard setup");await Barrier("discard-setup");
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState.Hand.Cards.OfType<TrueGrit>().Single(),null));
            await Wait(()=>NPlayerHand.Instance.CurrentMode==NPlayerHand.Mode.SimpleSelect,"TrueGrit hand");await Contract("Hand");
            // Highlight and undo remain presentation metadata, no game result yet.
            NCardHolder holder=NPlayerHand.Instance.ActiveHolders.First(h=>h.CardModel is DefendIronclad);
            CardModel selectedCard=holder.CardModel!;
            holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);
            await Wait(()=>Choice().GetProperty("Selected").GetArrayLength()==1,"selected highlight replicated");await Barrier("hand-highlight");
            Check(Child().GetProperty("completed").ValueKind==JsonValueKind.False,"highlight does not complete native choice");
            object container=Get(NPlayerHand.Instance,"_selectedHandCardContainer")!;
            Call(container,"DeselectCard",selectedCard);
            await Wait(()=>Choice().GetProperty("Selected").GetArrayLength()==0,"deselection replicated");await Barrier("hand-undo");
            holder=NPlayerHand.Instance.ActiveHolders.First(h=>h.CardModel is DefendIronclad);holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);
            ((NClickableControl)Get(NPlayerHand.Instance,"_selectModeConfirmButton")!).ForceClick();
            await Wait(()=>players.All(p=>p.PlayerCombatState!.ExhaustPile.Cards.Count>=1)&&!RunManager.Instance.ActionExecutor.IsRunning,"native exhaust");
            await Synced(state,"hand-result");
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState.Hand.Cards.OfType<Armaments>().Single(),null));
            await Wait(()=>NPlayerHand.Instance.CurrentMode==NPlayerHand.Mode.UpgradeSelect,"Armaments upgrade");await Contract("Upgrade");
            holder=NPlayerHand.Instance.ActiveHolders.First(h=>h.CardModel is DefendIronclad);holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);
            await Wait(()=>Choice().GetProperty("Selected").GetArrayLength()==1,"upgrade preview selected");await Barrier("upgrade-selected");
            Check(Child().GetProperty("active").GetString()=="NPlayerHand","upgrade uses original hand and upgrade preview");
            // Resync while a private choice is pending.
            originalPid=Pid;((Button)Get(View!,"_refresh")!).EmitSignal(Button.SignalName.Pressed);await ExitPid(originalPid);await UiReady("Upgrade");
            await Wait(()=>Choice().GetProperty("Selected").GetArrayLength()==1,"resync restores source upgrade selection");
            Check(Child().GetProperty("completed").ValueKind==JsonValueKind.False,"resync restores pending private choice without deciding it");
            await Barrier("upgrade-resynced");
            ((NClickableControl)Get(NPlayerHand.Instance,"_selectModeConfirmButton")!).ForceClick();
            await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning&&NPlayerHand.Instance.CurrentMode==NPlayerHand.Mode.Play,"native upgrade completed");
            await Synced(state,"upgrade-result");
            var enemy=CombatManager.Instance.DebugOnlyGetState().Enemies.First();
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState.Hand.Cards.OfType<Headbutt>().Single(),enemy));
            await Wait(()=>NOverlayStack.Instance?.Peek() is NCombatPileCardSelectScreen,"Headbutt discard selector");await Contract("Pile");
            var pile=(Node)NOverlayStack.Instance.Peek()!;holder=Holders(pile).First();holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);
            if(Get(pile,"_confirmButton") is NClickableControl pileConfirm&&pileConfirm.IsEnabled)pileConfirm.ForceClick();
            await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning&&NOverlayStack.Instance.Peek() is not NCombatPileCardSelectScreen,"native discard selection result");
            await Synced(state,"pile-result");
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState.Hand.Cards.OfType<Discovery>().Single(),null));
            await Wait(()=>NOverlayStack.Instance?.Peek() is NChooseACardSelectionScreen,"Discovery choices");await Contract("Choose");
            var choose=(Node)NOverlayStack.Instance.Peek()!;holder=Holders(choose).First();holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);
            await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning&&NOverlayStack.Instance.Peek() is not NChooseACardSelectionScreen,"native Discovery result");
            await Synced(state,"choose-result");
            // Room messages may arrive before renderer hello, or after a ready scene.
            Static("LiveSharingController","SelectMultiplayerSource",mine.ToString());await Synced(state,"local-view");
            await RunManager.Instance.EnterRoomDebug(RoomType.Treasure,showTransition:false);await Barrier("left-combat");await Frames(18);
            Check(!(bool)Get(Proc,"DisplayReady")!&&((string)Get(Proc,"Phase")!).Contains("非战斗"),"room boundary hides old battle and states unsupported page");
            await Barrier("room-boundary");
            Static("LiveSharingController","Suspend");Static("MultiplayerMirrorSession","Stop");
            Write(Role+"-result.json",new{passed=true,checks,headless=true,scope="phase2 navigation and personal native selectors"});
            await Barrier("done");
        }
        catch(Exception e){exit=1;Write(Role+"-result.json",new{passed=false,checks,error=e.ToString()});GD.PrintErr("[MultiplayerPhase2] FAIL "+e);}
        finally{Static("LiveSharingController","Suspend");Static("MultiplayerMirrorSession","Stop");((SceneTree)Engine.GetMainLoop()).Quit(exit);}
    }
}
