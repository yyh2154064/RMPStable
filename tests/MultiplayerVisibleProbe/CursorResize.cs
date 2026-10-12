using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static int cursorHandled;
    static async Task ObserveCursor(object renderer)
    {
        var request=ReadPath(Path.Combine(Root,"visible-cursor-request.json"));
        if(request.ValueKind!=System.Text.Json.JsonValueKind.Object||request.GetProperty("seq").GetInt32()==cursorHandled)return;
        cursorHandled=request.GetProperty("seq").GetInt32();
        var holder=Descendants<NRelicInventoryHolder>(NRun.Instance).First(h=>h.IsEnabled&&h.IsVisibleInTree());var point=NativePoint(holder);
        Rmp("MirrorRenderer").GetField("_pointer",Any)!.SetValue(renderer,point);Call(renderer,"PushSettingsMotion",point);await Frames(15);
        var original=(IDictionary)Get(null,"Original",Rmp("MirrorCursor"))!;
        var value=original[Input.CursorShape.Help];var image=value==null?null:Get(value,"Item1") as Image;
        var scaled=(IDictionary)Get(null,"Scaled",Rmp("MirrorCursor"))!;var resized=image==null?null:scaled[image.GetInstanceId()] as Image;
        WritePath(Path.Combine(Root,"visible-cursor-output.json"),new{seq=cursorHandled,scale=Get(null,"_scale",Rmp("MirrorCursor")),viewportScale=NGame.Instance.GetViewport().GetFinalTransform().Scale.X,originalWidth=image?.GetWidth(),scaledWidth=resized?.GetWidth(),originalHeight=image?.GetHeight(),scaledHeight=resized?.GetHeight()});
    }
    static async Task TestCursorResize(RunState state)
    {
        await Stage("独立窗口缩放专项 · 遗物放大镜尺寸");int pid=Pid;var panel=(Control)Get(View!,"_panel")!;var size=panel.Size;float initialScale=0,smallScale=0;
        foreach(float factor in new[]{1f,.8f,1.3f,1f}){
            var resize=panel.GetNode<Control>("SpectatorResize");var start=resize.GlobalPosition+resize.Size/2;
            using(var press=new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,GlobalPosition=start})resize.EmitSignal(Control.SignalName.GuiInput,press);
            using(var motion=new InputEventMouseMotion{GlobalPosition=start+new Vector2(size.X*factor-panel.Size.X,0)})resize.EmitSignal(Control.SignalName.GuiInput,motion);
            using(var release=new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,GlobalPosition=start})resize.EmitSignal(Control.SignalName.GuiInput,release);
            await Pace(2000);int seq=++sequence;string dir=(string)Get(Proc,"DirectoryPath")!;WritePath(Path.Combine(dir,"visible-cursor-request.json"),new{seq});
            await Wait(()=>ReadPath(Path.Combine(dir,"visible-cursor-output.json")).ValueKind==System.Text.Json.JsonValueKind.Object&&ReadPath(Path.Combine(dir,"visible-cursor-output.json")).GetProperty("seq").GetInt32()==seq,"native cursor scaling sample");
            var sample=ReadPath(Path.Combine(dir,"visible-cursor-output.json"));Write(Role+"-cursor-"+seq+".json",sample);
            float scale=sample.GetProperty("viewportScale").GetSingle();
            if(seq==1)initialScale=scale;
            if(factor==.8f){smallScale=scale;Check(scale<initialScale*.95f,"native client viewport actually shrinks");}
            if(factor==1.3f)Check(scale>smallScale*1.1f,"native client viewport actually enlarges");
            Check(sample.GetProperty("originalWidth").ValueKind==System.Text.Json.JsonValueKind.Number&&sample.GetProperty("scaledWidth").GetInt32()==Math.Clamp((int)Math.Round(sample.GetProperty("originalWidth").GetInt32()*scale),1,256)&&sample.GetProperty("scaledHeight").GetInt32()==Math.Clamp((int)Math.Round(sample.GetProperty("originalHeight").GetInt32()*scale),1,256),"native Help magnifier bitmap tracks window scale "+factor);
        }
        await Sync(state,"cursor-resize");Check(Pid==pid,"cursor resize preserves resident process");Write(Role+"-result.json",new{passed=true,checks,visible=true,scope="independent native Help cursor bitmap resizing"});await Barrier("cursor-done");
    }
}
