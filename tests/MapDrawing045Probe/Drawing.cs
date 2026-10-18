using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

public static partial class MultiplayerVisibleProbe
{
    static bool holdMapSends;
    static readonly List<object> heldMapMessages=new();
    static int mapHandled;
    static void InstallMapFixture()
    {
        if(!Renderer)return;
        new Harmony("RMP.MapDrawing045.TransportDelayProbe").Patch(Rmp("MirrorWire").GetMethod("Send",Any)!,
            prefix:new HarmonyMethod(typeof(MultiplayerVisibleProbe).GetMethod(nameof(HoldMapSend),Any)!));
    }
    static bool HoldMapSend(object message,ref Task __result)
    {
        if(!holdMapSends||Get(message,"Model") is not "mapStroke")return true;
        heldMapMessages.Add(message);__result=Task.CompletedTask;return false;
    }
    static string DrawingJson()
    {
        var drawing=NMapScreen.Instance?.Drawings;var state=RunManager.Instance.DebugOnlyGetState();
        if(drawing==null||state==null)return "[]";
        foreach(var player in state.Players)Call(drawing,"GetDrawingStateForPlayer",player.NetId);
        return JsonSerializer.Serialize(drawing.GetSerializableMapDrawings().drawings.OrderBy(d=>d.playerId),new JsonSerializerOptions{IncludeFields=true});
    }
    static Type MapQuickType=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Features.QuickSl.QuickSlController")).First(t=>t!=null)!;
    static object MapSnapshot()=>new{drawings=DrawingJson(),map=NMapScreen.Instance?.IsOpen==true,fog=Get(null,"Enabled",Rmp("CrystalFog")),
        active=NMapScreen.Instance?.Drawings is {} drawing&&RunManager.Instance.DebugOnlyGetState()?.Players.Any(p=>drawing.IsDrawing(p.NetId))==true,
        pending=NMapScreen.Instance?.Drawings is {} sending&&Get(sending,"_queuedMessage")!=null,
        control=Renderer?Get(Get(null,"Active",Rmp("MirrorRenderer")),"_control"):null};
    static string SourceDrawing()=>ReadPath(Path.Combine(Shared,SourceRole+"-map-source.json")).GetProperty("drawings").GetString()!;
    static string ReplicaDrawing()=>ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"map045-state.json")).GetProperty("drawings").GetString()!;
    static bool SameDrawing(string a,string b)
    {
        using var left=JsonDocument.Parse(a);using var right=JsonDocument.Parse(b);
        bool Same(JsonElement x,JsonElement y)
        {
            if(x.ValueKind!=y.ValueKind)return false;
            if(x.ValueKind==JsonValueKind.Number)return Math.Abs(x.GetDouble()-y.GetDouble())<0.000002;
            if(x.ValueKind==JsonValueKind.Array){var xa=x.EnumerateArray().ToArray();var ya=y.EnumerateArray().ToArray();return xa.Length==ya.Length&&xa.Zip(ya).All(pair=>Same(pair.First,pair.Second));}
            if(x.ValueKind==JsonValueKind.Object)return x.EnumerateObject().All(p=>y.TryGetProperty(p.Name,out var q)&&Same(p.Value,q));
            return x.GetRawText()==y.GetRawText();
        }
        return Same(left.RootElement,right.RootElement);
    }
    [DllImport("user32.dll")]static extern nint SendMessageTimeoutW(nint window,uint message,nuint key,nint data,uint flags,uint timeout,out nuint result);
    static void NativeMapMessage(uint message,Vector2 pixels)
    {
        nint own=(nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        long value=((long)(short)(int)pixels.Y<<16)|((long)(int)pixels.X&65535);
        if(SendMessageTimeoutW(own,message,0,(nint)value,2,5000,out _)==0)throw new Exception("Owned native map message failed");
    }
    static async Task NativeMapKey(uint key)
    {
        nint own=(nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        SendMessageTimeoutW(own,0x100,(nuint)key,(nint)1,2,5000,out _);await Frames(3);
        SendMessageTimeoutW(own,0x101,(nuint)key,unchecked((nint)(1L<<31|1L<<30|1)),2,5000,out _);await Frames(3);
    }
    static object LocalDrawingState(NMapDrawings drawing)=>Call(drawing,"GetDrawingStateForPlayer",LocalContext.NetId!.Value)!;
    static Line2D LastLine(object drawingState)=>((SubViewport)Get(drawingState,"drawViewport")!).GetChildren().OfType<Line2D>().Last(l=>!l.IsQueuedForDeletion());
    static int LocalLineCount(NMapDrawings drawing)=>((SubViewport)Get(LocalDrawingState(drawing),"drawViewport")!).GetChildren().OfType<Line2D>().Count(l=>!l.IsQueuedForDeletion());
    static async Task<object> RunMapStroke(object renderer,string label,string tool,int count)
    {
        var drawing=NMapScreen.Instance.Drawings;
        Call(renderer,"SetLocalPenMode",DrawingMode.None);
        await Frames(5);
        int before=LocalLineCount(drawing);
        var size=DisplayServer.WindowGetSize();
        var points=Enumerable.Range(0,count+1).Select(i=>new Vector2(MathF.Round(size.X*(.20f+i*.009f)),MathF.Round(size.Y*(.48f+MathF.Sin(i*.38f)*.065f)))).ToArray();
        holdMapSends=true;heldMapMessages.Clear();
        ulong inputFrame=Engine.GetProcessFrames(), began=Time.GetTicksUsec();
        uint down=tool=="erase"?0x207u:0x204u,up=tool=="erase"?0x208u:0x205u;
        NativeMapMessage(down,points[0]);
        foreach(var p in points.Skip(1))NativeMapMessage(0x200,p);
        NativeMapMessage(up,points[^1]);
        object? preview=null;
        for(int frames=0;frames<5&&preview==null;frames++){await Frames();preview=Get(renderer,"_mapPreview");}
        if(preview==null)throw new Exception("No immediate native map preview");
        ulong startFrame=(ulong)Get(preview,"LastStrokeFrame")!;
        var line=LastLine(Get(preview,"State")!);
        var actual=line.Points;
        if(actual.Length!=points.Length+1)throw new Exception("Native motion points dropped: "+actual.Length+" expected "+(points.Length+1));
        float error=0;
        var transform=NGame.Instance.GetViewport().GetFinalTransform()*drawing.GetGlobalTransformWithCanvas();
        for(int i=0;i<points.Length;i++)
        {
            var projected=transform*(actual[i==0?0:i+1]*2);
            error=Math.Max(error,projected.DistanceTo(points[i]));
        }
        if(error>.001f)throw new Exception("Nib/ink coordinate mismatch: "+error+" client pixels");
        if(startFrame>inputFrame+1)throw new Exception("Drawing waits extra frames before local presentation");
        if(LocalLineCount(drawing)!=before)throw new Exception("Prediction entered canonical drawing state before source acceptance");
        ulong inkUsec=Time.GetTicksUsec()-began;
        await Frames(8); // Hold the entire transport for > 250ms at the visible 30fps.
        if(heldMapMessages.Count==0)throw new Exception("Transport delay fixture did not hold real source commands");
        if(LocalLineCount(drawing)!=before)throw new Exception("Canonical stroke changed while all its source commands were held");
        await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        using(var image=NGame.Instance.GetViewport().GetTexture().GetImage())image.SavePng(Path.Combine(Shared,Role+"-replica-"+label+"-before-network.png"));
        int held=heldMapMessages.Count;holdMapSends=false;
        var wire=Get(renderer,"_wire")!;
        foreach(var message in heldMapMessages)Call(wire,"Send",message);
        heldMapMessages.Clear();
        ulong released=Time.GetTicksMsec();
        for(int frames=0;Get(renderer,"_mapPreview")!=null&&frames<300;frames++)await Frames();
        if(Get(renderer,"_mapPreview")!=null)throw new Exception("Preview did not hand off after source drawEnd echo");
        if(LocalLineCount(drawing)!=before+1)throw new Exception("Authoritative handoff duplicated or lost the native stroke");
        var accepted=LastLine(LocalDrawingState(drawing));
        if(accepted.Points.Length!=actual.Length)throw new Exception("Accepted native point count differs from immediate preview");
        float handoffError=actual.Zip(accepted.Points).Max(p=>p.First.DistanceTo(p.Second));
        if(handoffError>.002f)throw new Exception("Native authoritative handoff moved ink: "+handoffError);
        return new{points=points.Length,storedPoints=actual.Length,maxClientPixelError=error,frameOffset=startFrame-inputFrame,inputToProbeMicroseconds=inkUsec,
            heldMessages=held,heldFrames=8,handoffPointError=handoffError,sourceEchoMilliseconds=Time.GetTicksMsec()-released,tool};
    }
    static async Task ObserveMapDrawing(object renderer,RunState state)
    {
        WritePath(Path.Combine(Root,"map045-state.json"),MapSnapshot());
        var input=ReadPath(Path.Combine(Root,"map045-input.json"));
        if(input.ValueKind!=JsonValueKind.Object||input.GetProperty("seq").GetInt32()==mapHandled)return;
        mapHandled=input.GetProperty("seq").GetInt32();
        try
        {
            object? result=null;string kind=input.GetProperty("kind").GetString()!;
            if(kind=="prepare")
            {
                SaveManager.Instance.PrefsSave.ShowMultiplayerDrawings=true;
                NMapScreen.Instance.Open();Rmp("MirrorRenderer").GetField("_localMapOpen",Any)!.SetValue(renderer,true);
                Static("LocalSpectatorSource","ScrollNative",NMapScreen.Instance,"up:"+input.GetProperty("amount").GetInt32());
                DrawingJson();await Frames(15);NMapScreen.Instance.Drawings.UpdateVisibilityFromSettings();result=new{prepared=true};
            }
            else if(kind=="stroke")result=await RunMapStroke(renderer,input.GetProperty("label").GetString()!,input.GetProperty("tool").GetString()!,40);
            else if(kind=="shortcuts")
            {
                bool old=(bool)Get(null,"Enabled",Rmp("CrystalFog"))!;
                await NativeMapKey(0x73);
                for(int frame=0;frame<120&&(bool)Get(null,"Enabled",Rmp("CrystalFog"))! ==old;frame++)await Frames();
                if((bool)Get(null,"Enabled",Rmp("CrystalFog"))! ==old)throw new Exception("F4 failed after streamed drawing requests");
                for(int frame=0;frame<120&&Get(renderer,"_waitingForAuthority") is true;frame++)await Frames();
                if(Get(renderer,"_waitingForAuthority") is true)throw new Exception("Map stream left shortcut response waiting");
                await NativeMapKey(0x74);
                for(int frame=0;frame<120&&Get(null,"ConfirmationOpen",MapQuickType) is not true;frame++)await Frames();
                if(Get(null,"ConfirmationOpen",MapQuickType) is not true)throw new Exception("F5 popup missing after drawing");
                await NativeMapKey(27);await Frames(6);
                if(Get(null,"ConfirmationOpen",MapQuickType) is true)throw new Exception("F5 cancel failed after drawing");
                result=new{f4=true,f5Cancel=true,waiting=false};
            }
            else if(kind=="readonly")
            {
                int before=LocalLineCount(NMapScreen.Instance.Drawings);long requests=(long)Get(renderer,"_request")!;
                var p=DisplayServer.WindowGetSize()/2;NativeMapMessage(0x204,new Vector2(p.X,p.Y));NativeMapMessage(0x200,new Vector2(p.X+50,p.Y));NativeMapMessage(0x205,new Vector2(p.X+50,p.Y));await Frames(4);
                if(Get(renderer,"_mapPreview")!=null||before!=LocalLineCount(NMapScreen.Instance.Drawings)||(long)Get(renderer,"_request")! !=requests)throw new Exception("Readonly drawing sent or presented input");
                result=new{blocked=true};
            }
            WritePath(Path.Combine(Root,"map045-state.json"),MapSnapshot());
            WritePath(Path.Combine(Root,"map045-output.json"),new{seq=mapHandled,passed=true,result});
        }
        catch(Exception e){holdMapSends=false;heldMapMessages.Clear();WritePath(Path.Combine(Root,"map045-output.json"),new{seq=mapHandled,passed=false,error=e.ToString()});}
    }
    static async Task<JsonElement> MapRequest(string kind,string label="",string tool="draw",int amount=0)
    {
        int seq=++sequence;string dir=(string)Get(Proc,"DirectoryPath")!;
        WritePath(Path.Combine(dir,"map045-input.json"),new{seq,kind,label,tool,amount});
        await Wait(()=>ReadPath(Path.Combine(dir,"map045-output.json")).ValueKind==JsonValueKind.Object&&ReadPath(Path.Combine(dir,"map045-output.json")).GetProperty("seq").GetInt32()==seq,"map drawing operation "+label,90);
        var output=ReadPath(Path.Combine(dir,"map045-output.json"));
        Check(output.GetProperty("passed").GetBoolean(),kind+" "+label+" "+(output.TryGetProperty("error",out var error)?error.GetString():"native input, immediate ink, precision and authoritative handoff"));
        Write(Role+"-"+label+"-metrics.json",output);return output;
    }
    static async Task ResizeMap(float factor)
    {
        var panel=(Control)Get(View,"_panel")!;var resize=panel.GetNode<Control>("SpectatorResize");var start=resize.GlobalPosition+resize.Size/2;float width=panel.Size.X;
        resize.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,GlobalPosition=start});
        resize.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseMotion{GlobalPosition=start+new Vector2(width*(factor-1),0)});
        resize.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,GlobalPosition=start});await Pace(1500);
    }
    static async Task TestMapCases(RunState state)
    {
        SaveManager.Instance.PrefsSave.ShowMultiplayerDrawings=true;NMapScreen.Instance.Open();DrawingJson();await Frames(15);
        Call(View,"SetPreferredControl",true);
        await Wait(()=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("control").GetBoolean()&&Child().GetProperty("inputReady").GetBoolean(),"native map control ready");
        int round=0;
        foreach(float scale in new[]{1f,.72f,1.35f})
        {
            string tag="map045-"+round;await Stage((Single?"单人":"多人")+"地图 · 缩放 "+scale+" · 原版即时笔迹/笔尖校验");await ResizeMap(scale);
            await MapRequest("prepare",tag+"-prepare",amount:3+round*2);
            foreach(string tool in new[]{"draw","erase"})
            {
                await MapRequest("stroke",tag+"-"+tool,tool);
                if(!Single)await Barrier(tag+"-"+tool+"-all-accepted");
                int stable=0;string matchedSource="",matchedReplica="";
                await Wait(()=>
                {
                    try
                    {
                        bool settled=Roles.All(role=>ReadPath(Path.Combine(Shared,role+"-map-source.json")) is var s&&s.ValueKind==JsonValueKind.Object&&!s.GetProperty("active").GetBoolean()&&!s.GetProperty("pending").GetBoolean());
                        string original=SourceDrawing(),replica=ReplicaDrawing();
                        stable=settled&&SameDrawing(replica,original)?stable+1:0;
                        if(stable>=6){matchedSource=original;matchedReplica=replica;return true;}return false;
                    }
                    catch{return false;}
                },"all stopped native drawing streams and canonical points converge",45);
                Check(stable>=6,tag+" "+tool+" original canonical map drawings equal source, with no duplicate prediction");
                Write(Role+"-"+tag+"-"+tool+"-canonical.json",new{source=matchedSource,replica=matchedReplica,stableSamples=stable});
                if(!Single)await Barrier(tag+"-"+tool);
            }
            round++;
        }
        await MapRequest("shortcuts","map045-post-drawing-f4-f5");
        Call(View,"SetPreferredControl",false);await Wait(()=>!Child().GetProperty("control").GetBoolean(),"readonly drawing mode");
        await MapRequest("readonly","map045-readonly");
        Check(RunManager.Instance.DebugOnlyGetState()==state,"map presentation never replaces source game state");
        Write(Role+"-result.json",new{passed=true,checks,scope="native right/middle mouse drawing, motion burst precision, three window sizes, independent map pan, delayed transport, source echo handoff, readonly rejection"});
        await Barrier("map045-done");
    }
    static async Task TestMapMulti(RunState state)
    {
        await Enter(RoomType.RestSite,"map045-room");await Sync(state,"map045-room");await Grant("map045-room");await TestMapCases(state);
    }
    static async Task TestMapSingle()
    {
        var player=Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),1);
        var state=RunState.CreateForNewRun(new[]{player},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPMAP045");
        RunManager.Instance.SetUpNewSingleplayer(state,true);await PreloadManager.LoadRunAssets(new[]{player.Character});RunManager.Instance.Launch();
        NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);
        Static("LiveSharingController","Open",state);Call(View,"SetPreferredControl",true);_ = MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(DumpSource());
        await Wait(()=>Get(Mirror,"Verified") is true,"singleplayer native replica ready");
        await TestMapCases(state);
    }
}
