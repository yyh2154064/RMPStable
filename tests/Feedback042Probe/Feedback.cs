using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer;

public static partial class MultiplayerVisibleProbe
{
    static bool FeedbackSingle => System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="feedbackSingle";
    static int feedbackHandled;
    static object FeedbackMirror => FeedbackSingle?SingleMirror:Mirror;
    static object FeedbackProc => Get(FeedbackMirror,"Process")!;
    static void Pointer(object renderer, Vector2 point)=>renderer.GetType().GetField("_pointer",Any)!.SetValue(renderer,point);
    static string Drawings()=>JsonSerializer.Serialize(NMapScreen.Instance.Drawings.GetSerializableMapDrawings().drawings.OrderBy(d=>d.playerId),new JsonSerializerOptions{IncludeFields=true});
    static async Task ObserveFeedback(object renderer,RunState state)
    {
        // Publish presentation state independently of gameplay readiness.
        WritePath(Path.Combine(Root,"feedback-state.json"),new{target=NTargetManager.Instance?.IsInSelection,arrow=NTargetManager.Instance?.GetNode<NTargetingArrow>("TargetingArrow").Visible,
            from=Get(null,"RestTarget",Rmp("MultiplayerPresentation042")),names=Descendants<NMultiplayerPlayerState>(NRun.Instance).Select(p=>new{id=p.Player.NetId,name=(Get(p,"_nameplateLabel") as Label)?.Text}),drawings=Drawings(),restFront=RestFront(state),ending=EndingSnapshot(),architect=ArchitectSnapshot()});
        var input=ReadPath(Path.Combine(Root,"feedback-input.json"));
        if(input.ValueKind!=JsonValueKind.Object||input.GetProperty("seq").GetInt32()==feedbackHandled)return;
        int requestSequence=input.GetProperty("seq").GetInt32();string kind=input.GetProperty("kind").GetString()!;
        var queue=(ConcurrentQueue<(uint,Vector2,long,long,float)>)Get(renderer,"_buttons")!;
        long generation=(long)Get(renderer,"_generation")!,epoch=(long)Get(renderer,"_epoch")!;
        bool success=true;object? detail=null;
        if(kind=="draw")
        {
            string tool=input.GetProperty("path").GetString()!;
            NMapScreen.Instance.Open();renderer.GetType().GetField("_localMapOpen",Any)!.SetValue(renderer,true);
            Static("LocalSpectatorSource","ScrollNative",NMapScreen.Instance,"up:9");await Frames(20);
            if(tool.Length>0)
            {
                var toolButton=Descendants<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>(NMapScreen.Instance).First(b=>b is NMapDrawButton&&tool=="pen"||b is NMapEraseButton&&tool=="erase");
                await NativeClick(renderer,NativePoint(toolButton));
                success=NMapScreen.Instance.Drawings.GetLocalDrawingMode(false)==(tool=="erase"?DrawingMode.Erasing:DrawingMode.Drawing);
            }
            var start=new Vector2(.23f,.47f);Pointer(renderer,start);
            var drawing=NMapScreen.Instance.Drawings;
            var local=drawing.GetGlobalTransformWithCanvas().AffineInverse()*(Vector2)Call(renderer,"ViewportPoint",start)!;
            // Vanilla stores Line2D points at half resolution in its drawing
            // subviewport; compare that serialized point, not a viewport pixel.
            var expected=(Vector2)Static("MapDrawing042","ToNet",drawing,local*.5f)!;
            ulong began=Time.GetTicksMsec();queue.Enqueue((tool.Length==0?0x204u:0x201u,start,generation,epoch,0));await Frames(2);
            for(int i=1;i<=45;i++){Pointer(renderer,start+new Vector2(i*.007f,(float)Math.Sin(i*.15f)*.045f));await Frames();}
            var end=start+new Vector2(45*.007f,(float)Math.Sin(45*.15f)*.045f);
            queue.Enqueue((tool.Length==0?0x205u:0x202u,end,generation,epoch,0));await Frames(18);
            var lines=drawing.GetSerializableMapDrawings().drawings.FirstOrDefault(p=>p.playerId==LocalContext.NetId)?.lines;
            success=success&&lines?.Count>0&&lines.Last().mapPoints.Count>10&&lines.Last().mapPoints[0].DistanceTo(expected)<.001f&&lines.Last().isEraser==(tool=="erase");
            detail=new{expectedX=expected.X,expectedY=expected.Y,points=lines?.LastOrDefault()?.mapPoints.Count,drawMs=Time.GetTicksMsec()-began};
            NMapScreen.Instance.Close(false);renderer.GetType().GetField("_localMapOpen",Any)!.SetValue(renderer,false);
        }
        else if(kind=="spectatorMouse")
        {
            long requests=(long)Get(renderer,"_request")!;
            Pointer(renderer,new Vector2(.9f,.2f));await NativeClick(renderer,new Vector2(.7f,.7f));await Frames(4);
            success=NTargetManager.Instance.IsInSelection==false&&requests==(long)Get(renderer,"_request")!;
            detail=Get(null,"RestTarget",Rmp("MultiplayerPresentation042"));
        }
        else if(kind=="mapLayer")
        {
            NMapScreen.Instance.Open();renderer.GetType().GetField("_localMapOpen",Any)!.SetValue(renderer,true);await Frames(8);
            success=RestFront(state)&&((IList)Get(NRun.Instance.RestSiteRoom,"_characterContainers")!).Cast<Control>().All(c=>c.ZIndex==0);
            using(var image=NGame.Instance.GetViewport().GetTexture().GetImage())image.SavePng(Path.Combine(Shared,Role+"-042-rest-map.png"));
            NMapScreen.Instance.Close(false);renderer.GetType().GetField("_localMapOpen",Any)!.SetValue(renderer,false);
        }
        else if(kind=="latencyClick")
        {
            string actionKind=input.GetProperty("actionKind").GetString()!,path=input.GetProperty("path").GetString()!;
            await Wait(()=>JsonSerializer.SerializeToElement(Get(renderer,"_battleAuthority")).GetProperty("Actions").EnumerateArray().Any(a=>a.GetProperty("Kind").GetString()==actionKind&&a.GetProperty("Enabled").GetBoolean()&&a.GetProperty("NativePath").GetString()!.Contains(path)),"042 native selector finishes entering");
            var authority=JsonSerializer.SerializeToElement(Get(renderer,"_battleAuthority"));
            var action=authority.GetProperty("Actions").EnumerateArray().First(a=>a.GetProperty("Kind").GetString()==actionKind&&a.GetProperty("Enabled").GetBoolean()&&a.GetProperty("NativePath").GetString()!.Contains(path));
            var node=(Control)Static("MirrorNativeUi","Resolve",action.GetProperty("NativePath").GetString()!,true)!;
            var hit=node is MegaCrit.Sts2.Core.Nodes.Cards.Holders.NCardHolder h?h.Hitbox:node;
            var point=NativePoint(hit);long before=(long)Get(renderer,"_request")!;
            queue.Enqueue((0x201,point,generation,epoch,0));await Frames(2);ulong released=Time.GetTicksMsec();queue.Enqueue((0x202,point,generation,epoch,0));
            await Wait(()=>(long)Get(renderer,"_request")!>before,"042 click dispatch");ulong sent=Time.GetTicksMsec();
            long request=(long)Get(renderer,"_request")!;
            await Wait(()=>(long)Get(renderer,"LastMultiplayerResult")!>=request,"042 source acknowledgement");
            success=Get(renderer,"LastMultiplayerAccepted") is true;
            detail=new{clickToSendMs=sent-released,roundTripMs=Time.GetTicksMsec()-sent,releaseToAckMs=Time.GetTicksMsec()-released,accepted=success};
        }
        feedbackHandled=requestSequence;
        WritePath(Path.Combine(Root,"feedback-output.json"),new{seq=feedbackHandled,success,detail});
    }
    static async Task<JsonElement> FeedbackInput(string kind,string actionKind="",string path="")
    {
        string dir=(string)Get(FeedbackProc,"DirectoryPath")!;int seq=++sequence;
        WritePath(Path.Combine(dir,"feedback-input.json"),new{seq,kind,actionKind,path});
        await Wait(()=>ReadPath(Path.Combine(dir,"feedback-output.json")).ValueKind==JsonValueKind.Object&&ReadPath(Path.Combine(dir,"feedback-output.json")).GetProperty("seq").GetInt32()==seq,"042 "+kind);
        var output=ReadPath(Path.Combine(dir,"feedback-output.json"));Write(Role+"-042-"+seq+"-"+kind+".json",output);
        Check(output.GetProperty("success").GetBoolean(),"042 native "+kind+" "+actionKind+" "+path);return output;
    }
    static JsonElement FeedbackState()=>ReadPath(Path.Combine((string)Get(FeedbackProc,"DirectoryPath")!,"feedback-state.json"));
    static bool SameDrawings(string actual,string expected)
    {
        using var a=JsonDocument.Parse(actual);using var b=JsonDocument.Parse(expected);
        var x=a.RootElement.EnumerateArray().ToArray();var y=b.RootElement.EnumerateArray().ToArray();
        if(x.Length!=y.Length)return false;
        for(int i=0;i<x.Length;i++)
        {
            if(x[i].GetProperty("playerId").GetUInt64()!=y[i].GetProperty("playerId").GetUInt64())return false;
            var xl=x[i].GetProperty("lines").EnumerateArray().ToArray();var yl=y[i].GetProperty("lines").EnumerateArray().ToArray();if(xl.Length!=yl.Length)return false;
            for(int j=0;j<xl.Length;j++)
            {
                if(xl[j].GetProperty("isEraser").GetBoolean()!=yl[j].GetProperty("isEraser").GetBoolean())return false;
                var xp=xl[j].GetProperty("mapPoints").EnumerateArray().ToArray();var yp=yl[j].GetProperty("mapPoints").EnumerateArray().ToArray();if(xp.Length!=yp.Length)return false;
                for(int k=0;k<xp.Length;k++)foreach(string axis in new[]{"X","Y"})if(Math.Abs(xp[k].GetProperty(axis).GetSingle()-yp[k].GetProperty(axis).GetSingle())>0.000002f)return false;
            }
        }
        return true;
    }
    static string ExpectedDrawing()=>FeedbackSingle?Drawings():ReadPath(Path.Combine(Root,SourceRole+"-042-drawing-source.json")).GetProperty("drawings").GetString()!;
    static async Task CheckDrawing(string tag,string tool="")
    {
        await FeedbackInput("draw",path:tool);
        Write(Role+"-042-drawing-source.json",new{drawings=Drawings()});
        if(!FeedbackSingle)await Barrier(tag+"-drawing-sources");
        await Wait(()=>FeedbackState().ValueKind==JsonValueKind.Object&&SameDrawings(FeedbackState().GetProperty("drawings").GetString()!,ExpectedDrawing()),tag+" canonical native drawing equality",30);
        Check(SameDrawings(FeedbackState().GetProperty("drawings").GetString()!,ExpectedDrawing()),tag+" selected source and replica drawing points match within float transform rounding despite independent map pan");
    }
    static async Task TestFeedbackMulti(RunState state)
    {
        await Enter(RoomType.RestSite,"042-rest");await Sync(state,"042-rest-entry");
        Check(RestFront(state),"source selected rest portrait ordered only within background siblings");await FeedbackInput("mapLayer");
        await Wait(()=>FeedbackState().ValueKind==JsonValueKind.Object,"042 presentation metadata");
        foreach(var name in FeedbackState().GetProperty("names").EnumerateArray())
            Check(name.GetProperty("name").GetString()=="玩家 "+name.GetProperty("id").GetUInt64(),"replica sidebar uses source name metadata for player "+name.GetProperty("id"));
        string tip=(string)Get(View,"ControlTipText")!;
        Check(tip.Contains("玩家 "+SourceId)&&tip.Contains("游戏"),"control tip uses selected source name");
        Call(View!,"ShowControlTip");
        var popup=(PopupPanel)Get(View,"_controlTip")!;
        Check(popup.Visible&&popup.ForceNative&&!popup.IsEmbedded()&&popup.Unfocusable,"control tip is a visible native nonactivating popup above game window");popup.Hide();
        // Everyone starts their own native Mend; every observed source waits
        // for its actual original choice, with no replica-local target task.
        var me=LocalContext.GetMe(state)!;
        var mend=RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(me).OfType<MendRestSiteOption>().Single();
        var mendTask=(Task)Call(NRun.Instance.RestSiteRoom.GetButtonForOption(mend)!,"SelectOption",mend)!;
        await Wait(()=>NTargetManager.Instance.IsInSelection,"source native Mend targeting");await Barrier("042-mend-targeting");await Frames(20);
        Check(!FeedbackState().GetProperty("target").GetBoolean()&&FeedbackState().GetProperty("arrow").GetBoolean(),"replica shows source arrow while no local target selection is running");
        await FeedbackInput("spectatorMouse");
        var target=state.Players.First(p=>p.NetId==Id%3+1);var character=NRun.Instance.RestSiteRoom.GetCharacterForPlayer(target)!;
        NTargetManager.Instance.OnNodeHovered(character);await Frames(10);await Barrier("042-target-hovered");
        NTargetManager.Instance._Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});await mendTask;await Sync(state,"042-mend-result");
        Check(!FeedbackState().GetProperty("target").GetBoolean(),"Mend completes from recorded original result without observer choice");
        await Enter(RoomType.RestSite,"042-map");await Sync(state,"042-map-entry");await Grant("042-map");
        Check(Session("_controlViewer") is ulong viewer&&viewer!=0&&Session("_pendingViewer") is ulong pending&&pending==0,"click immediately grants lease without source approval");
        await CheckDrawing("multiplayer-right");await CheckDrawing("multiplayer-pen","pen");await CheckDrawing("multiplayer-erase","erase");await Barrier("042-drawings");
        await Enter(RoomType.RestSite,"042-upgrade");await Sync(state,"042-upgrade-entry");await Grant("042-upgrade");
        await Remote("native","NRestSiteButton",1);await Wait(()=>ChoiceKind()=="DeckUpgrade"&&ChoiceMatches(),"042 real upgrade preview");
        await FeedbackInput("mapLayer");await Remote("select");await Frames(20);await Remote("native","NConfirmButton");await Sync(state,"042-upgrade-result");
        await Enter(RoomType.Event,"042-reward",ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.BrainLeech>());await Sync(state,"042-reward-entry");await Grant("042-reward");
        await Remote("native","NEventOptionButton",1);await Remote("native","NRewardButton");await Wait(()=>ChoiceKind()=="Reward"&&ChoiceMatches(),"042 reward candidates");
        var watch=System.Diagnostics.Stopwatch.StartNew();await FeedbackInput("latencyClick","select");await Sync(state,"042-card-result");
        Write(Role+"-042-card-settled.json",new{elapsedMs=watch.ElapsedMilliseconds});
        await Barrier("042-before-ancient");
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.AncientEvent<MegaCrit.Sts2.Core.Models.Events.Neow>(),showTransition:false);
        await Wait(()=>Get(Proc,"DisplayReady") is true&&Get(Mirror,"Verified") is true,"042 native ancient");await Barrier("042-ancient");await Grant("042-ancient");
        await Wait(()=>Authority().GetProperty("Actions").EnumerateArray().Any(a=>a.GetProperty("NativePath").GetString()!.Contains("NEventOptionButton")),"042 ancient options");
        await FeedbackInput("latencyClick","native","NEventOptionButton");
        await Wait(()=>ChoiceKind().Length>0||ReadPath(Path.Combine(Root,SourceRole+"-journal.json")).GetProperty("eventFinished").GetBoolean(),"042 ancient result or follow-up choice");
        for(int i=0;i<8&&ChoiceKind().Length>0;i++)
        {
            uint choice=Personal().GetProperty("Id").GetUInt32();
            await FeedbackInput("latencyClick","select");
            await Wait(()=>ChoiceKind()==""||Personal().GetProperty("Id").GetUInt32()!=choice,"042 native ancient follow-up completes");
        }
        await Sync(state,"042-ancient-result");
        Write(Role+"-result.json",new{passed=true,checks,scope="v0.4.2 five feedback fixes and native choice latency"});await Barrier("042-done");
    }
    static async Task TestFeedbackSingle()
    {
        RunManager.Instance.CleanUp();var player=Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),1);
        var state=RunState.CreateForNewRun(new[]{player},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMP042SINGLE");
        SaveManager.Instance.Progress.GetOrCreateCharacterStats(player.Character.Id);RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService(),disableCombatStateSync:false,shouldSave:false);
        state.ExtraFields.StartedWithNeow=true;RunManager.Instance.GenerateRooms();Get(state.Act,"_rooms")!.GetType().GetProperty("Ancient")!.SetValue(Get(state.Act,"_rooms"),ModelDb.AncientEvent<MegaCrit.Sts2.Core.Models.Events.Neow>());await PreloadManager.LoadRunAssets(new[]{player.Character});RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);
        await RunManager.Instance.EnterMapCoord(state.Map.StartingMapPoint.coord);await NGame.Instance.Transition.FadeIn(.03f);
        Static("LiveSharingController","Open",state);await Wait(()=>Get(SingleProc,"DisplayReady") is true&&Get(SingleMirror,"Verified") is true,"042 single replica");
        Call(View!,"SetControlEnabled",true);await Frames(20);await CheckDrawing("singleplayer-right");await CheckDrawing("singleplayer-pen","pen");await CheckDrawing("singleplayer-erase","erase");
        Write(Role+"-result.json",new{passed=true,checks,scope="v0.4.2 singleplayer native map coordinates and stroke streaming"});
    }
}
