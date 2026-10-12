using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static bool DeathCase=>System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")?.StartsWith("death")==true;
    static object DeathSnapshot()
    {
        var state=RunManager.Instance.DebugOnlyGetState();
        var room=NCombatRoom.Instance;
        var ending=NOverlayStack.Instance?.Peek() as NGameOverScreen;
        var visuals=ending==null?Array.Empty<object>():Descendants<NCreatureVisuals>((Node)Get(ending,"_creatureContainer")!).Where(v=>room==null||room.CreatureNodes.Any(c=>c.Entity.IsPlayer&&c.Visuals==v)).Select(v=>(object)new{animation=v.SpineAnimation.GetCurrentAnimationName(),visible=v.IsVisibleInTree(),alpha=v.Modulate.A}).ToArray();
        return new{local=LocalContext.NetId,ending=EndingSnapshot(),visuals,
            players=state?.Players.OrderBy(p=>p.NetId).Select(p=>{var c=room?.GetCreatureNode(p.Creature);return new{id=p.NetId,hp=p.Creature.CurrentHp,dead=p.Creature.IsDead,animation=c?.SpineAnimation.GetCurrentAnimationName(),deathTask=c?.IsPlayingDeathAnimation,filter=c?.Hitbox.MouseFilter.ToString(),focus=c?.Hitbox.FocusMode.ToString(),uiAlpha=c==null?null:(float?)(Get(c,"_stateDisplay") as Control)?.Modulate.A,visualParent=c?.Visuals.GetParent()?.GetType().Name};}).ToArray(),
            handVisible=NPlayerHand.Instance?.IsVisibleInTree(),handCount=NPlayerHand.Instance?.ActiveHolders.Count()};
    }
    static void PrepareDeathFixture(RunState state,Player[] players)
    {
        foreach(var p in players){p.Deck.Clear(true);for(int i=0;i<6;i++)p.Deck.AddInternal(state.CreateCard<Bloodletting>(p),silent:true);}
        players[1].Creature.SetCurrentHpInternal(1);
        if(System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="deathDefeat"){players[0].Creature.SetCurrentHpInternal(3);players[2].Creature.SetCurrentHpInternal(3);}
    }
    static int deathCaptureHandled;
    static void ObserveDeathVisuals()
    {
        if(!DeathCase)return;
        WritePath(Path.Combine(Root,"visible-death.json"),DeathSnapshot());
        var request=ReadPath(Path.Combine(Root,"visible-death-capture.json"));
        if(request.ValueKind==JsonValueKind.Object&&request.GetProperty("seq").GetInt32()!=deathCaptureHandled){
            RenderingServer.ForceDraw(true);
            using var image=NGame.Instance.GetViewport().GetTexture().GetImage();
            image.SavePng(Path.Combine(Shared,Role+"-death-"+request.GetProperty("tag").GetString()+"-replica.png"));
            deathCaptureHandled=request.GetProperty("seq").GetInt32();
            WritePath(Path.Combine(Root,"visible-death-captured.json"),new{seq=deathCaptureHandled});
        }
    }
    static JsonElement ChildDeath()=>ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"visible-death.json"));
    static async Task AssertDeathParity(string tag,bool final)
    {
        await Frames(75);Write(Role+"-death-"+tag+".json",DeathSnapshot());await Barrier("death-visual-"+tag);
        var source=ReadPath(Path.Combine(Root,SourceRole+"-death-"+tag+".json"));var child=ChildDeath();
        Check(child.ValueKind==JsonValueKind.Object&&child.GetProperty("local").GetUInt64()==SourceId,"death presentation is bound to selected source");
        var expected=source.GetProperty("players").EnumerateArray().ToArray();var actual=child.GetProperty("players").EnumerateArray().ToArray();
        Check(expected.Length==3&&actual.Length==3,"all three different characters remain present after death");
        foreach(var p in expected){var q=actual.Single(x=>x.GetProperty("id").GetUInt64()==p.GetProperty("id").GetUInt64());
            foreach(string field in new[]{"hp","dead","animation","deathTask","filter","focus","uiAlpha","visualParent"})Check(q.GetProperty(field).GetRawText()==p.GetProperty(field).GetRawText(),"native player "+p.GetProperty("id")+" death "+field+" matches source");}
        if(!final){Check(child.GetProperty("handVisible").GetRawText()==source.GetProperty("handVisible").GetRawText()&&child.GetProperty("handCount").GetRawText()==source.GetProperty("handCount").GetRawText(),"dead/alive selected source native hand UI matches original");
            Check(actual.Count(p=>p.GetProperty("dead").GetBoolean())==1&&actual.Single(p=>p.GetProperty("id").GetUInt64()==2).GetProperty("animation").GetString()=="die","one teammate is down while both surviving players retain native animation");}
        else{var a=child.GetProperty("visuals").EnumerateArray().ToArray();var b=source.GetProperty("visuals").EnumerateArray().ToArray();Check(a.Length==3&&b.Length==3&&a.All(v=>v.GetProperty("animation").GetString()=="die"&&v.GetProperty("visible").GetBoolean()&&v.GetProperty("alpha").GetSingle()==1)&&b.All(v=>v.GetProperty("animation").GetString()=="die"),"native source and replica ending actually render all three downed characters");
            foreach(string field in new[]{"win","score","character"})Check(child.GetProperty("ending").GetProperty(field).GetRawText()==source.GetProperty("ending").GetProperty(field).GetRawText(),"native final "+field+" matches source");}
        // Native popups use separate swapchains. Exclude our shell from the
        // original viewport capture so it cannot obscure the source artwork.
        var shell=(CanvasLayer)Get(View!,"_overlay")!;bool wasVisible=shell.Visible;shell.Visible=false;
        try{RenderingServer.ForceDraw(true);using var image=NGame.Instance.GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(Shared,Role+"-death-"+tag+"-original.png"));}
        finally{shell.Visible=wasVisible;}
        int capture=++sequence;WritePath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"visible-death-capture.json"),new{seq=capture,tag});
        await Wait(()=>ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"visible-death-captured.json")).ValueKind==JsonValueKind.Object&&ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"visible-death-captured.json")).GetProperty("seq").GetInt32()==capture,"actual settled native replica render captured");
    }
    static async Task TestDeathVisuals(RunState state,bool victory)
    {
        int pid=Pid;await Stage("后台死亡绘图专项 · 单名队员战斗内死亡");
        await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MegaCrit.Sts2.Core.Map.MapPointType.Monster).coord);
        await Wait(()=>CombatManager.Instance.IsInProgress&&!RunManager.Instance.ActionExecutor.IsRunning&&state.GetPlayer(2).PlayerCombatState!.Hand.Cards.OfType<Bloodletting>().Any()&&(bool)Get(Mirror,"Verified")!,"native lethal card fixture");await Barrier("before-native-single-death");
        if(Id==2)RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(state.GetPlayer(2).PlayerCombatState!.Hand.Cards.OfType<Bloodletting>().First(),null));
        await Wait(()=>state.GetPlayer(2).Creature.IsDead&&!RunManager.Instance.ActionExecutor.IsRunning,"source native card kills only its owner");await Sync(state,"single-native-death");await AssertDeathParity("single",false);
        int before=Pid;((Button)Get(View!,"_refresh")!).EmitSignal(Button.SignalName.Pressed);await Wait(()=>(bool)Get(Proc,"DisplayReady")!&&(bool)Get(Mirror,"Verified")!,"dead teammate clean native restore");await Barrier("single-death-reset");Check(Pid==before,"death state reset preserves exact resident process");await AssertDeathParity("reset",false);
        if(victory){await Enter(MegaCrit.Sts2.Core.Rooms.RoomType.Event,"death-victory-architect",MegaCrit.Sts2.Core.Models.ModelDb.Event<TheArchitect>());await Sync(state,"death-architect-entry");await Barrier("before-death-victory");await (Task)Call(RunManager.Instance,"WinRun")!;}
        else{
            foreach(ulong actor in new ulong[]{1,3}){
                await Barrier("before-native-death-"+actor);
                if(Id==actor)RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(state.GetPlayer(actor).PlayerCombatState!.Hand.Cards.OfType<Bloodletting>().First(),null));
                await Wait(()=>state.GetPlayer(actor).Creature.IsDead&&!RunManager.Instance.ActionExecutor.IsRunning,"native lethal card resolves player "+actor);await Barrier("after-native-death-"+actor);
            }
        }
        await Wait(()=>NOverlayStack.Instance?.Peek() is NGameOverScreen&&ChildDeath().ValueKind==JsonValueKind.Object&&ChildDeath().GetProperty("ending").GetProperty("screen").GetString()=="NGameOverScreen"&&(bool)Get(Mirror,"Verified")!,"native final death visuals ready");await Barrier("death-final-ready");
        await Stage(victory?"后台死亡绘图专项 · 胜利结算":"后台死亡绘图专项 · 全队失败结算");await AssertDeathParity("final",true);Check(ChildDeath().GetProperty("ending").GetProperty("win").GetBoolean()==victory,"original and replica final outcome matches requested branch");Check(Pid==pid,"single death/reset/final ending retain original resident PID");
        Write(Role+"-result.json",new{passed=true,checks,visible=false,actualRendering=true,scope="independent native single-player death, clean reset and "+(victory?"victory":"defeat")+" visual parity"});await Barrier("death-test-done");
    }
}
