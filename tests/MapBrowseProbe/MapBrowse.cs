using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

public static partial class MultiplayerVisibleProbe
{
    static bool MapSingle => System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="mapSingle";
    static int mapHandled;
    static string MapDigest(RunState state) => (string)Static("MirrorState",MapSingle?"Hash":"MultiplayerHash",state)!;
    static void Pointer(object renderer,Vector2 point)=>renderer.GetType().GetField("_pointer",Any)!.SetValue(renderer,point);
    static async Task ObserveMapBrowse(object renderer,RunState state)
    {
        var input=ReadPath(Path.Combine(Root,"map-browse-input.json"));
        if(input.ValueKind!=JsonValueKind.Object||input.GetProperty("seq").GetInt32()==mapHandled)return;
        mapHandled=input.GetProperty("seq").GetInt32();string kind=input.GetProperty("kind").GetString()!;
        var map=NMapScreen.Instance;string hash=MapDigest(state);long requests=(long)Get(renderer,"_request")!;
        bool found=true,hover=false;string localHit="";
        MapPointType highlighted=MapPointType.Unassigned;
        void OnHighlight(MapPointType type)=>highlighted=type;
        map.PointTypeHighlighted+=OnHighlight;
        Vector2 before=(Vector2)Get(map,"_targetDragPos")!;
        var queue=(ConcurrentQueue<(uint,Vector2,long,long,float)>)Get(renderer,"_buttons")!;
        long generation=(long)Get(renderer,"_generation")!,epoch=(long)Get(renderer,"_epoch")!;
        var blank=new Vector2(.24f,.5f);
        if(kind=="open"||kind=="back"||kind=="legend")
        {
            Control? node=kind=="open"?NRun.Instance.GlobalUi.TopBar.Map:kind=="back"?Get(map,"_backButton") as Control:
                Descendants<NMapLegendItem>(map).FirstOrDefault(n=>n.Name.ToString()==input.GetProperty("name").GetString());
            found=node is MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl b&&b.IsEnabled&&node.IsVisibleInTree();
            if(found)
            {
                var area=node is NBackButton&&Get(node,"_buttonImage") is Control image?image:node!;
                var point=NativePoint(area);Pointer(renderer,point);await Frames(4);
                localHit=Call(renderer,"LocalUiAt",point)?.GetType().Name??"none";
                await NativeClick(renderer,point);await Frames(12);
                if(kind=="legend")hover=Get(node,"_pointType") is MapPointType expected&&highlighted==expected&&Get(node,"_icon") is Control icon&&icon.Scale.X>1.1f&&
                    Descendants<MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet>(NGame.Instance).Any(n=>n.IsVisibleInTree());
            }
        }
        else if(kind=="up"||kind=="down")
        {
            Pointer(renderer,blank);queue.Enqueue((0x20A,blank,generation,epoch,kind=="up"?20:-20));await Frames(20);
        }
        else if(kind=="pan")
        {
            Pointer(renderer,blank);queue.Enqueue((0x201,blank,generation,epoch,0));await Frames(4);
            Pointer(renderer,blank+new Vector2(0,.15f));await Frames(5);
            queue.Enqueue((0x202,blank+new Vector2(0,.15f),generation,epoch,0));await Frames(15);
        }
        else if(kind=="escape")
        {
            var keys=(ConcurrentQueue<(uint,uint,long,long,long)>)Get(renderer,"_keyboard")!;
            keys.Enqueue((0x100,0x1B,0,generation,epoch));await Frames(12);
        }
        else if(kind=="travel")
        {
            var point=Descendants<NMapPoint>(map).FirstOrDefault(n=>n.IsVisibleInTree()&&n.GetGlobalRect().Intersects(map.GetViewportRect()));
            found=point!=null;if(found)await NativeClick(renderer,NativePoint(point!));
        }
        await Frames(10);
        map.PointTypeHighlighted-=OnHighlight;
        var boss=Get(map,"_bossPointNode") as Control;
        var eventOptions=Descendants<NEventOptionButton>(NRun.Instance).Where(b=>b.IsVisibleInTree()).ToArray();
        var output=new{seq=mapHandled,kind,found,localHit,hover,open=map.IsOpen,travel=map.IsTravelEnabled,
            before=before.Y,after=((Vector2)Get(map,"_targetDragPos")!).Y,bossVisible=boss!=null&&boss.GetGlobalRect().Intersects(map.GetViewportRect()),
            control=Get(renderer,"_control"),noRequests=requests==(long)Get(renderer,"_request")!,unchanged=hash==MapDigest(state),
            room=state.CurrentRoom?.RoomType.ToString(),floor=state.ActFloor,options=eventOptions.Length,ancient=(state.CurrentRoom as EventRoom)?.ModelId.ToString()};
        WritePath(Path.Combine(Root,"map-browse-output.json"),output);
        using var capture=NGame.Instance.GetViewport().GetTexture().GetImage();capture.SavePng(Path.Combine(Shared,Role+"-map-"+mapHandled+"-"+kind+".png"));
    }
    static async Task<JsonElement> MapInput(string kind,string name="")
    {
        string dir=(string)Get(MapSingle?SingleProc:Proc,"DirectoryPath")!;int seq=++sequence;
        WritePath(Path.Combine(dir,"map-browse-input.json"),new{seq,kind,name});
        await Wait(()=>ReadPath(Path.Combine(dir,"map-browse-output.json")).ValueKind==JsonValueKind.Object&&ReadPath(Path.Combine(dir,"map-browse-output.json")).GetProperty("seq").GetInt32()==seq,"map "+kind);
        var output=ReadPath(Path.Combine(dir,"map-browse-output.json"));Write(Role+"-map-result-"+seq+".json",output);
        Check(output.GetProperty("found").GetBoolean(),"original map input resolves "+kind+" "+name);
        Check(output.GetProperty("noRequests").GetBoolean()&&output.GetProperty("unchanged").GetBoolean(),"map "+kind+" is browsing without authoritative request or state mutation");
        return output;
    }
    static async Task VerifyMap(string tag)
    {
        var output=await MapInput("open");Check(output.GetProperty("open").GetBoolean(),tag+" opens map before ancient choice");
        Check(!output.GetProperty("travel").GetBoolean(),tag+" native ancient choice keeps travel disabled");
        Check(output.GetProperty("floor").GetInt32()==RunManager.Instance.DebugOnlyGetState()!.ActFloor,tag+" replica retains original act floor for map navigation");
        output=await MapInput("up");Check(output.GetProperty("after").GetSingle()>output.GetProperty("before").GetSingle()&&output.GetProperty("bossVisible").GetBoolean(),tag+" wheel scroll reaches native boss");
        output=await MapInput("down");Check(output.GetProperty("after").GetSingle()<output.GetProperty("before").GetSingle(),tag+" wheel returns down route");
        output=await MapInput("pan");Check(output.GetProperty("after").GetSingle()>output.GetProperty("before").GetSingle(),tag+" native route pans with left drag");
        foreach(string name in new[]{"EnemyLegendItem","RestSiteLegendItem","EliteLegendItem","UnknownLegendItem","MerchantLegendItem","TreasureLegendItem"})
        {output=await MapInput("legend",name);Check(output.GetProperty("hover").GetBoolean()&&output.GetProperty("localHit").GetString()=="NMapLegendItem",tag+" native legend hover/tooltip and local click "+name);}
        output=await MapInput("travel");Check(!output.GetProperty("travel").GetBoolean(),tag+" unchosen ancient cannot travel from browsing map");
        output=await MapInput("back");Check(!output.GetProperty("open").GetBoolean()&&output.GetProperty("options").GetInt32()>0,tag+" native back returns to unchosen ancient options");
        await MapInput("open");output=await MapInput("escape");Check(!output.GetProperty("open").GetBoolean(),tag+" escape returns from map");
    }
    static async Task TestMapSingle()
    {
        int pid=0;
        foreach(var ancient in ModelDb.AllAncients.Where(a=>System.Environment.GetEnvironmentVariable("RMP_MAP_NEOW_ONLY")!="1"||a.GetType().Name=="Neow").OrderBy(a=>a.GetType().Name=="Neow"?"":a.GetType().Name))
        {
        Static("LiveSharingController","Suspend");
        RunManager.Instance.CleanUp();var player=Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),1);
        var state=RunState.CreateForNewRun(new[]{player},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPMAPBROWSE");
        SaveManager.Instance.Progress.GetOrCreateCharacterStats(player.Character.Id);
        RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService(),disableCombatStateSync:false,shouldSave:false);
        state.ExtraFields.StartedWithNeow=true;
        RunManager.Instance.GenerateRooms();Get(state.Act,"_rooms")!.GetType().GetProperty("Ancient")!.SetValue(Get(state.Act,"_rooms"),ancient);
        await PreloadManager.LoadRunAssets(new[]{player.Character});RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);
        await RunManager.Instance.EnterMapCoord(state.Map.StartingMapPoint.coord);await NGame.Instance.Transition.FadeIn(.03f);
        Static("LiveSharingController","Open",state);await Wait(()=>Get(SingleProc,"DisplayReady") is true&&Get(SingleMirror,"Verified") is true,"singleplayer unchosen ancient replica");
            if(pid==0)pid=SinglePid;
            Check((state.CurrentRoom as EventRoom)?.ModelId==ancient.Id,"single native map actually enters "+ancient.Id);
            await Frames(20);
            string hash=MapDigest(state);await VerifyMap("single "+ancient.Id);
            if(ancient.GetType().Name=="Neow")
            {
                Call(View!,"SetControlEnabled",true);await Frames(20);
                await VerifyMap("single authorized "+ancient.Id);
                Call(View!,"SetControlEnabled",false);await Frames(20);
            }
            Check(hash==MapDigest(state)&&SinglePid==pid,"single "+ancient.Id+" preserves unchosen source and resident PID");
        }
        Write(Role+"-result.json",new{passed=true,checks,scope=System.Environment.GetEnvironmentVariable("RMP_MAP_NEOW_ONLY")=="1"?"final candidate singleplayer Neow map browsing in spectator and authorized modes":"singleplayer all ancient rooms unchosen original map browsing"});
    }
    static async Task TestMapMulti(RunState state)
    {
        int pid=Pid;
        await Barrier("neow-entry");await NMapScreen.Instance.TravelToMapCoord(state.Map.StartingMapPoint.coord);await Sync(state,"neow-choice");
        foreach(var ancient in ModelDb.AllAncients.OrderBy(a=>a.GetType().Name))
        {
            await Barrier("ancient-"+ancient.Id);if((state.CurrentRoom as EventRoom)?.ModelId!=ancient.Id)await RunManager.Instance.EnterRoomDebug(RoomType.Event,MapPointType.Ancient,ancient,showTransition:false);
            await Sync(state,"ancient-"+ancient.Id+"-entry");await Frames(20);
            string hash=MapDigest(state);await VerifyMap("multi "+ancient.Id);
            if(ancient.GetType().Name=="Neow")
            {
                await Grant("neow-map-browse");await VerifyMap("multi authorized "+ancient.Id);
                Static("MultiplayerMirrorSession","RequestControl",false);await Frames(20);
            }
            Check(hash==MapDigest(state)&&Pid==pid,"multi "+ancient.Id+" preserves unchosen source and resident PID");
            await Sync(state,"ancient-"+ancient.Id+"-browsed");
        }
        Write(Role+"-result.json",new{passed=true,checks,scope="three original multiplayer peers all ancient rooms unchosen original map browsing"});await Barrier("map-done");
    }
}
