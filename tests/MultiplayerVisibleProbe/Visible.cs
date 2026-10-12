using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using Env = System.Environment;

public static partial class MultiplayerVisibleProbe
{
    static string stage="准备三人可见测试";
    static Label? status;
    static ulong lastStatus;
    static int readonlyAttempt;
    static int readonlyHandled;
    static string capturedStage="";
    static int captures;
    static readonly System.Collections.Generic.List<string> latencyFailures=new();
    static bool networkPump=true;
    static async Task PumpNetwork(MegaCrit.Sts2.Core.Multiplayer.Game.INetGameService service)
    {while(networkPump){service.Update();await Frames();}}
    static string Shared => Env.GetEnvironmentVariable("RMP_VISIBLE_EVIDENCE")!;
    static string RoleName => Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="openingSingle"?"单人 · 故障机器人":Id==1?"我 · 铁甲战士":Id==2?"测试员 A · 静默猎手":"测试员 B · 故障机器人";
    static async Task Pace(int milliseconds=1600)
    {
        // These new silent entries wait for actual native readiness at every
        // operation. Long pauses intended for visible demonstrations add no
        // coverage; retain explicit warmup, heartbeat and playback waits.
        if(Env.GetEnvironmentVariable("RMP_VISIBLE_SILENT")=="1"&&Env.GetEnvironmentVariable("RMP_VISIBLE_CASE") is "rooms19" or "battleSwitch19"&&milliseconds is 1600 or 2200)milliseconds=250;
        ulong end=Time.GetTicksMsec()+(ulong)milliseconds;while(Time.GetTicksMsec()<end){await Frames();}
    }
    static async Task ConfigureVisible()
    {
        if(DisplayServer.GetName()=="headless")throw new Exception("Visible test must have an actual rendering window");
        var rect=DisplayServer.ScreenGetUsableRect();int width=(rect.Size.X-24)/3,height=Math.Min(rect.Size.Y-80,width*9/16);
        var window=((SceneTree)Engine.GetMainLoop()).Root;window.MinSize=new Vector2I(320,180);
        bool silent=Env.GetEnvironmentVariable("RMP_VISIBLE_SILENT")=="1";
        if(!silent)DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(new Vector2I(width,height));
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus,silent);
        DisplayServer.WindowSetPosition(silent?Vector2I.Zero:rect.Position+new Vector2I(8+((int)Id-1)*(width+8),36));
        DisplayServer.WindowSetTitle("RMP 三人可见测试 · "+RoleName);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        KeepIsolatedWindowHidden();
        Write(Role+"-window.json",new{pid=Env.ProcessId,width,height,screen=rect.ToString(),visible=!silent,actualRendering=true,role=RoleName});
        await VisibleStatus();await Pace(25000);await Barrier("graphics-warmed");
        using(var image=NGame.Instance.GetViewport().GetTexture().GetImage())image.SavePng(Path.Combine(Shared,Role+"-startup.png"));
    }
    static async Task VisibleStatus()
    {
        if(Time.GetTicksMsec()-lastStatus<500)return;lastStatus=Time.GetTicksMsec();
        if(!Renderer)KeepIsolatedWindowHidden();
        if(status==null||!GodotObject.IsInstanceValid(status))
        {
            var layer=new CanvasLayer{Layer=220};((SceneTree)Engine.GetMainLoop()).Root.AddChild(layer);
            status=new Label{Position=new Vector2(12,100),MouseFilter=Control.MouseFilterEnum.Ignore};
            status.AddThemeFontSizeOverride("font_size",22);status.AddThemeColorOverride("font_color",Colors.White);
            status.AddThemeColorOverride("font_outline_color",Colors.Black);status.AddThemeConstantOverride("outline_size",5);layer.AddChild(status);
        }
        if(Renderer)
        {
            var progress=ReadPath(Path.Combine(Shared,Role+"-progress.json"));
            string currentStage=progress.ValueKind==JsonValueKind.Object?progress.GetProperty("stage").GetString()!:stage;
            status.Text="窗口来源 / Source "+LocalContext.NetId+"\n"+currentStage;
            if(currentStage!=capturedStage&&LocalContext.NetId!=null&&Get(Get(null,"Active",Rmp("MirrorRenderer")),"_sceneReady") is true)
            {capturedStage=currentStage;using var image=NGame.Instance.GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(Shared,Role+"-replica-"+LocalContext.NetId+"-"+(++captures)+".png"));}
        }
        else
        {
            status.Text=RoleName+" · "+stage+"\nChecks: "+checks+"  Source: "+(Session("Selected")??"-");
            Write(Role+"-progress.json",new{stage,checks,source=Session("Selected"),pid=Env.ProcessId,time=Time.GetTicksMsec()});
        }
        // Do not overwrite the product's temporary hidden reconstruction FPS
        // cap. Visible gameplay and source processes retain the silent 30 FPS.
        if(!Renderer||Get(null,"Replica",Rmp("MirrorFastRestore")) is not true)Engine.MaxFps=30;await Task.CompletedTask;
    }
    static void KeepIsolatedWindowHidden()
    {
        if(Env.GetEnvironmentVariable("RMP_VISIBLE_SILENT")!="1"||Renderer)return;
        var own=(nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        var parent=(nint)long.Parse(Env.GetEnvironmentVariable("RMP_TEST_STARTUP_PARENT")!);
        Static("MirrorWin32","RequireOwned",own,Env.ProcessId);
        Static("MirrorWin32","RequireOwned",parent,int.Parse(Env.GetEnvironmentVariable("RMP_TEST_LAUNCHER")!));
        Static("MirrorWin32","ShowWindow",parent,0);
        Static("MirrorWin32","SetWindowPos",parent,nint.Zero,-30000,-30000,1280,720,0x0010u|0x0004u|0x0080u);
        var style=(long)(nint)Static("MirrorWin32","GetLong",own,-16)!;
        Static("MirrorWin32","SetLong",own,-16,(nint)((style&~0x80000000L)|0x40000000L));
        Static("MirrorWin32","SetParent",own,parent);
        if(Static("MirrorWin32","SetWindowPos",own,nint.Zero,0,0,0,0,0x0010u|0x0001u|0x0004u|0x0020u) is not true)throw new Exception("Cannot keep owned silent test child embedded");
        if(Static("MirrorWin32","IsWindowVisible",own) is true)throw new Exception("Silent source unexpectedly exposed on desktop");
    }
    static async Task Stage(string name)
    {
        stage=name;await VisibleStatus();await Pace(2500);
        if(NGame.Instance!=null&&DisplayServer.GetName()!="headless")
        {using var image=NGame.Instance.GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(Shared,Role+"-"+checks+"-scene.png"));}
    }
    static object[] Portraits()=>NRun.Instance?.CombatRoom is {} room?Descendants<NCreature>(room).Where(c=>c.Entity.IsPlayer).Select(c=>(object)new{id=c.Entity.Player!.NetId,character=c.Entity.Player.Character.GetType().Name,x=c.Position.X,index=c.GetIndex()}).ToArray():Array.Empty<object>();
    static bool Foreground(JsonElement portraits,ulong id)
    {if(portraits.ValueKind!=JsonValueKind.Array)return false;var all=portraits.EnumerateArray().ToArray();if(all.Length!=3)return false;var own=all.FirstOrDefault(p=>p.GetProperty("id").GetUInt64()==id);return own.ValueKind==JsonValueKind.Object&&all.All(p=>p.GetProperty("x").GetSingle()<=own.GetProperty("x").GetSingle()&&p.GetProperty("index").GetInt32()<=own.GetProperty("index").GetInt32());}
    static int[] SelectedRow(RunState state)=>NPlayerHand.Instance!=null&&Get(NPlayerHand.Instance,"_selectedHandCardContainer") is Node row&&LocalContext.GetMe(state)?.PlayerCombatState is {} combat?row.GetChildren().OfType<NSelectedHandCardHolder>().Select(h=>combat.Hand.Cards.ToList().IndexOf(h.CardModel!)).ToArray():Array.Empty<int>();
    static async Task Hotkey()
    {Input.ParseInputEvent(new InputEventKey{Keycode=Key.F8,Pressed=true});await Frames(3);Input.ParseInputEvent(new InputEventKey{Keycode=Key.F8,Pressed=false});await Pace();}
    static async Task ObserveReadonly(object renderer,RunState state)
    {
        var input=ReadPath(Path.Combine(Root,"visible-readonly.json"));if(input.ValueKind!=JsonValueKind.Object||input.GetProperty("seq").GetInt32()==readonlyHandled)return;
        if(Static("MultiplayerPersonalUi","Capture")==null)return;
        var view=Get(null,"_view",Rmp("MultiplayerPersonalUi")) as Node ?? NOverlayStack.Instance.Peek() as Node;
        if(view==null)return;
        var holder=Descendants<NCardHolder>(view).FirstOrDefault(h=>h.IsVisibleInTree());if(holder!=null)holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);
        if(Get(view,"_confirmButton") is NClickableControl confirm)confirm.ForceClick();
        if(Get(view,"_skipButton") is NClickableControl skip)skip.ForceClick();
        if(view is NPlayerHand hand&&Get(hand,"_selectModeConfirmButton") is NClickableControl button)button.ForceClick();
        readonlyHandled=input.GetProperty("seq").GetInt32();readonlyAttempt=readonlyHandled;await Frames(4);
    }
    static async Task ContractReadonly(string kind,string tag)
    {
        await Wait(()=>ChoiceKind()==kind&&ChoiceMatches(),"source and replica choice contract "+kind);
        Check(Personal().GetProperty("Cards").GetArrayLength()>0&&ChoiceMatches(),kind+" original candidate identities and constraints match selected source");
        int seq=++sequence;WritePath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"visible-readonly.json"),new{seq});
        await Wait(()=>Child().GetProperty("readonlyAttempt").GetInt32()==seq,"readonly original choice input");await Pace();
        Check(!Child().GetProperty("control").GetBoolean()&&ChoiceKind()==kind&&Static("MultiplayerPersonalUi","Capture")!=null,kind+" ungranted native click and confirmation leave source choice waiting");await Barrier(tag);
    }
    static async Task ChooseSource(ulong source,string tag,bool synchronize=true)
    {
        int previous=Pid;long generation=(long)Get(Mirror,"LocalGeneration")!;ulong began=Time.GetTicksMsec();bool changed=SourceId!=source;var buttons=(IList)Get(View!,"_sourceButtons")!;
        ((Button)Get(buttons[(int)source-1],"Item1")!).EmitSignal(Button.SignalName.Pressed);
        await Wait(()=>Session("Selected") is ulong id&&id==source,"real roster source selection");
        await Wait(()=>(bool)Get(Proc,"DisplayReady")!&&Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("local").ValueKind==JsonValueKind.Number&&Child().GetProperty("local").GetUInt64()==source,"new native source identity");
        await Wait(()=>ChoiceKind()!=""?ChoiceMatches():(bool)Get(Mirror,"Verified")!,"switched source settled state or original waiting choice synchronized");
        Check(LocalContext.NetId==Id&&Child().GetProperty("players").GetArrayLength()==3&&Child().GetProperty("local").GetUInt64()==source,"source "+source+" preserves own identity and all three original slots");
        if(changed){Check(Pid==previous,"source switch reuses exactly its authenticated native process");Check((long)Get(Mirror,"LocalGeneration")!>generation&&!Child().GetProperty("control").GetBoolean(),"source switch advances local generation and clears old permission");}
        ulong elapsed=Time.GetTicksMsec()-began;
        File.AppendAllText(Path.Combine(Root,Role+"-switch-timing.jsonl"),JsonSerializer.Serialize(new{source,milliseconds=elapsed,reused=Pid==previous,changed})+"\n");
        if(changed)
        {
            const string label="synchronized source switch completes within three seconds";
            if(elapsed>3000&&Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="battleSwitch19")
            {
                latencyFailures.Add(source+":"+elapsed+"ms");checks++;
                File.AppendAllText(Path.Combine(Root,Role+"-checks.jsonl"),JsonSerializer.Serialize(new{label,passed=false,milliseconds=elapsed})+"\n");
                GD.PrintErr("[MultiplayerVisible] PERFORMANCE FAIL "+source+" "+elapsed+"ms; continuing native functional checks");
            }
            else Check(elapsed<=3000,label);
        }
        Write(Role+"-replica-path.json",new{directory=Get(Proc,"DirectoryPath")});if(synchronize)await Barrier(tag);await Pace();
    }
    static async Task FinishVisible(int exit)
    {
        if(OpeningCase)OpeningPhase("cleanup");
        stage=exit==0?"全部专项完成 · 请检查结果":"发现失败 · 保留日志供检查";await VisibleStatus();
        Write(Role+"-finished.json",new{exit,checks});
        await Pace(Env.GetEnvironmentVariable("RMP_VISIBLE_SILENT")=="1"?100:exit==0?12000:20000);
        Static("LiveSharingController","Suspend");Static("MultiplayerMirrorSession","Stop");((SceneTree)Engine.GetMainLoop()).Quit(exit);
    }
}
