using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Environment=System.Environment;

[ModInitializer(nameof(Initialize))]
public static class StartupDisplayProbe
{
    [DllImport("user32.dll")]private static extern nint GetWindow(nint window,uint relation);
    [StructLayout(LayoutKind.Sequential)]private struct WinRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint window,out WinRect rect);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetLong(nint window,int index);
    private static object Geometry(nint window){GetWindowRect(window,out var r);return new{left=r.Left,top=r.Top,right=r.Right,bottom=r.Bottom,style=(long)GetLong(window,-16),exstyle=(long)GetLong(window,-20),owner=(long)GetLong(window,-8)};}
    private static bool Above(nint window,nint owner){for(nint p=GetWindow(owner,2);p!=0;p=GetWindow(p,2))if(p==window)return false;return true;}
    private const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static Type Rmp(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing."+name)).First(t=>t!=null)!;
    private static object? Field(object obj,string name)=>obj.GetType().GetField(name,Any)!.GetValue(obj);
    private static object? Static(string name)=>Rmp("LiveSharingController").GetField(name,Any)!.GetValue(null);
    private static bool Renderer=>Environment.GetEnvironmentVariable("RMP_MULTI_ROLE")=="renderer";
    private static string Output=>Renderer?Environment.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")!:Environment.GetEnvironmentVariable("RMP_STARTUP_EVIDENCE")!;
    private static async Task Frames(int count){var tree=(SceneTree)Engine.GetMainLoop();for(int i=0;i<count;i++)await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    private static async Task Pause(int seconds){ulong end=Time.GetTicksMsec()+(ulong)seconds*1000;while(Time.GetTicksMsec()<end)await Frames(1);}
    private static async Task Wait(Func<bool> condition,string label){ulong end=Time.GetTicksMsec()+90000;while(Time.GetTicksMsec()<end){if(condition())return;await Frames(1);}throw new Exception("Timeout "+label);}
    private static Task Image(string label){using var image=NGame.Instance.GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(Output,label+".png"));return Task.CompletedTask;}
    public static void Initialize(){if(Renderer&&Environment.GetEnvironmentVariable("RMP_MULTI_RENDERER_TEST")=="1")_ = TaskHelper.RunSafely(Observe());else if(Environment.GetEnvironmentVariable("RMP_MULTI_STARTUP_TEST")=="1")_ = TaskHelper.RunSafely(Run());}
    private static async Task Observe(){while(true){await Frames(12);var request=Path.Combine(Output,"startup-capture.txt");if(NGame.Instance==null||!File.Exists(request))continue;string label=File.ReadAllText(request);File.Delete(request);await Image(label);}}
    private static async Task Run()
    {
        int exit=0;try{
            await Wait(()=>NGame.Instance?.MainMenu!=null,"main menu");await Pause(2);SaveManager.Instance.SetFtuesEnabled(false);
            var canvas=new CanvasLayer{Layer=120};NGame.Instance.AddChild(canvas);var label=new Label{Text="首次显示专项：不发送任何鼠标或键盘输入，静置观察",Position=new Vector2(20,75),MouseFilter=Control.MouseFilterEnum.Ignore};label.AddThemeFontSizeOverride("font_size",26);canvas.AddChild(label);
            var character=ModelDb.Character<Ironclad>();var state=RunState.CreateForNewRun(new[]{Player.CreateForNewRun(character,SaveManager.Instance.GenerateUnlockStateFromProgress(),1)},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPSTARTUP13");
            RunManager.Instance.SetUpNewSingleplayer(state,true);await PreloadManager.LoadRunAssets(new[]{character});RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);
            Rmp("LiveSharingController").GetMethod("Open",Any)!.Invoke(null,new object[]{state});var view=Static("_view")!;view.GetType().GetMethod("SetControlEnabled",Any)!.Invoke(view,new object[]{true});((Control)Field(view,"_panel")!).Position=new Vector2(1130,160);
            await Wait(()=>Static("_mirror") is {} m&&(bool)m.GetType().GetProperty("Verified",Any)!.GetValue(m)!,"verified");
            GD.Print("[StartupDisplay] READY; no input for 120 seconds");
            await Pause(20);object process=Field(Static("_mirror")!,"Process")!;string child=(string)Field(process,"DirectoryPath")!;
            File.WriteAllText(Path.Combine(child,"startup-capture.txt"),"replica-no-input");await Image("source-no-input");
            long initialFrames=(long)Field(process,"DrawFrames")!;bool aboveOwner=Above((nint)(long)Field(process,"Window")!,(nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle));
            File.WriteAllText(Path.Combine(Output,"geometry.json"),JsonSerializer.Serialize(new{source=Geometry((nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle)),replica=Geometry((nint)(long)Field(process,"Window")!)}));
            await Pause(100);long finalFrames=(long)Field(process,"DrawFrames")!;bool finalAbove=Above((nint)(long)Field(process,"Window")!,(nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle));
            bool passed=(bool)Field(process,"DisplayReady")!&&(bool)Field(process,"NativeAttached")!&&aboveOwner&&finalAbove&&initialFrames>0&&finalFrames>initialFrames;
            File.WriteAllText(Path.Combine(Output,"result.json"),JsonSerializer.Serialize(new{displayReady=Field(process,"DisplayReady"),attached=Field(process,"NativeAttached"),initialFrames,finalFrames,fps=Field(process,"Fps"),phase=Field(process,"Phase"),aboveOwner,finalAbove,noInjectedInput=true,passed}));
            if(Environment.GetEnvironmentVariable("RMP_STARTUP_EXPECT_FIXED")=="1"&&!passed)throw new Exception("Native display layer assertions failed");
            GD.Print("[StartupDisplay] RESULT aboveOwner="+aboveOwner+" finalAbove="+finalAbove+" draw="+initialFrames+"/"+finalFrames);
        }catch(Exception e){exit=1;GD.Print("[StartupDisplay] FAIL "+e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());}
        Rmp("LiveSharingController").GetMethod("Suspend",Any)!.Invoke(null,null);((SceneTree)Engine.GetMainLoop()).Quit(exit);
    }
}
