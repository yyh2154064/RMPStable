using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
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
public static class NavigationLayoutProbe
{
    private const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static int _checks;
    private static string Output=>Environment.GetEnvironmentVariable("RMP_UI_EVIDENCE")!;
    private static Type Rmp(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing."+name)).First(t=>t!=null)!;
    private static object? Static(string name)=>Rmp("LiveSharingController").GetField(name,Any)!.GetValue(null);
    private static object Field(object obj,string name)=>obj.GetType().GetField(name,Any)!.GetValue(obj)!;
    private static void Call(string method)=>Rmp("LiveSharingController").GetMethod(method,Any)!.Invoke(null,null);
    private static async Task Frames(int count){var tree=(SceneTree)Engine.GetMainLoop();for(int i=0;i<count;i++)await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    private static async Task Wait(Func<bool> condition,string label){ulong end=Time.GetTicksMsec()+90000;while(Time.GetTicksMsec()<end){if(condition())return;await Frames(1);}throw new Exception("Timeout "+label);}
    private static void Check(bool result,string name){File.AppendAllText(Path.Combine(Output,"checks.jsonl"),JsonSerializer.Serialize(new{name,passed=result})+"\n");if(!result)throw new Exception(name);_checks++;}
    private static Rect2 Rect(Control node)=>new(node.GlobalPosition,node.Size*node.GetGlobalTransform().Scale);
    private static void Sources(object view,int count)
    {
        var type=Rmp("ParticipantSnapshot");var list=(IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(type))!;
        for(int i=0;i<count;i++){var entry=Activator.CreateInstance(type)!;type.GetProperty("Id")!.SetValue(entry,i.ToString());type.GetProperty("Name")!.SetValue(entry,(i+1).ToString());list.Add(entry);}
        view.GetType().GetMethod("UpdateSources",Any)!.Invoke(view,new object[]{list,"0"});
    }
    private static async Task Image(string name){RenderingServer.ForceDraw(true);await Frames(2);using var image=NGame.Instance.GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(Output,name+".png"));}
    public static void Initialize(){if(Environment.GetEnvironmentVariable("RMP_MULTI_ROLE")!="renderer"&&Environment.GetEnvironmentVariable("RMP_MULTI_UI_TEST")=="1")_ = TaskHelper.RunSafely(Run());}
    private static async Task Run()
    {
        int exit=0;try
        {
            await Wait(()=>NGame.Instance?.MainMenu!=null,"main menu");await Frames(120);SaveManager.Instance.SetFtuesEnabled(false);
            var character=ModelDb.Character<Ironclad>();var state=RunState.CreateForNewRun(new[]{Player.CreateForNewRun(character,SaveManager.Instance.GenerateUnlockStateFromProgress(),1)},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPUI14");
            RunManager.Instance.SetUpNewSingleplayer(state,true);await PreloadManager.LoadRunAssets(new[]{character});RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);
            Call("Suspend");
            string prefs=ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath("rmp-spectator.json"));
            File.WriteAllText(prefs,"{\"HasLayout\":true,\"X\":200,\"Y\":300,\"Width\":900,\"DockEdge\":0,\"Pinned\":false,\"ControlMode\":true}");
            var preferences=Rmp("SpectatorPreferences");preferences.GetField("_path",Any)!.SetValue(null,"");
            preferences.GetMethod("LoadProfile",Any)!.Invoke(null,new object[]{(Action)(()=>Call("Suspend"))});
            object current=preferences.GetProperty("Current",Any)!.GetValue(null)!;
            Check((int)preferences.GetProperty("LayoutRevision")!.GetValue(current)! ==14&&!(bool)preferences.GetProperty("HasLayout")!.GetValue(current)!,"old layout migrates once to .14 defaults");
            Rmp("LiveSharingController").GetMethod("Open",Any)!.Invoke(null,new object[]{state});
            var view=Static("_view")!;var panel=(Control)Field(view,"_panel");var content=(Control)Field(view,"_content");
            await Wait(()=>Static("_mirror") is {} mirror&&(bool)Field(Field(mirror,"Process"),"DisplayReady"),"native view ready");await Frames(60);
            var bounds=panel.GetViewport().GetVisibleRect().Size;
            Check(Math.Abs(panel.Size.Y-bounds.Y/3)<1,"whole panel height equals one third of viewport");
            Check(Math.Abs(panel.Position.X+panel.Size.X-bounds.X)<1&&(int)Field(view,"_dockEdge")==2,"panel docks flush right");
            float relicBottom=Descendants(NRun.Instance.GlobalUi.RelicInventory).OfType<Control>().Where(n=>n.GetType().Name=="NRelicInventoryHolder"&&n.IsVisibleInTree()).Select(n=>n.GetGlobalRect().End.Y).DefaultIfEmpty(0).Max();
            File.WriteAllText(Path.Combine(Output,"initial-geometry.json"),JsonSerializer.Serialize(new{panelY=panel.Position.Y,relicBottom,topBarBottom=NRun.Instance.GlobalUi.TopBar.GetGlobalRect().End.Y,hpBottom=NRun.Instance.GlobalUi.TopBar.Hp.GetGlobalRect().End.Y,pauseBottom=NRun.Instance.GlobalUi.TopBar.Pause.GetGlobalRect().End.Y,defaultTop=preferences.GetProperty("DefaultTop",Any)!.GetValue(null)}));await Image("initial-layout");
            float statusBottom=Descendants(NRun.Instance.GlobalUi.TopBar).OfType<Control>().Where(n=>n.IsVisibleInTree()&&n.GetType().Name is "NTopBarHp" or "NTopBarGold" or "NTopBarPortrait" or "NTopBarDeckButton" or "NTopBarMapButton" or "NTopBarPauseButton").Select(n=>n.GetGlobalRect().End.Y).DefaultIfEmpty(80).Max();
            Check(panel.Position.Y>=Math.Max(relicBottom,statusBottom)&&panel.Position.Y<Math.Max(relicBottom,statusBottom)+20,"status bar and relic row remain exposed");
            Check(!(bool)view.GetType().GetProperty("ControlEnabled",Any)!.GetValue(view)!&&(bool)Field(view,"_pinned")&&!(bool)Field(view,"_dockHidden"),"default watch and pinned expanded");
            var mode=(Button)Field(view,"_controlToggle");var reset=(Button)Field(view,"_refresh");var pin=(Button)Field(view,"_pin");var close=panel.GetNode<Button>("SpectatorClose");
            var m=Rect(mode);var r=Rect(reset);var p=Rect(pin);var c=Rect(close);
            Check(m.End.X<r.Position.X&&r.End.X<p.Position.X&&p.End.X<c.Position.X,"toolbar order watch/control, reset, pin, close");
            Check(r.Size.IsEqualApprox(p.Size)&&p.Size.IsEqualApprox(c.Size)&&Math.Abs(r.GetCenter().Y-p.GetCenter().Y)<.1&&Math.Abs(p.GetCenter().Y-c.GetCenter().Y)<.1,"three icon buttons equal size and aligned");
            Check(new[]{reset,pin,close}.All(b=>b.GetNode("Icon") is Node2D&&b.GetThemeStylebox("normal") is StyleBoxEmpty),"three icon buttons share vector style and empty background");
            Check(m.Position.Y>panel.Position.Y+2&&m.End.Y<panel.Position.Y+content.Position.Y-2,"mode toggle has top and bottom padding");
            await Image("default-layout");
            Rmp("LiveSharingController").GetField("_captureTimer",Any)!.SetValue(null,100d); // Isolate shell paging from the real one-source snapshot.
            Sources(view,7);var previous=(Button)Field(view,"_sourcePrevious");var next=(Button)Field(view,"_sourceNext");var left=previous.Position;var right=next.Position;
            Check(!previous.Visible&&previous.Disabled&&next.Visible&&!next.Disabled,"first page hides unavailable previous arrow");
            next.EmitSignal(Button.SignalName.Pressed);Check(previous.Visible&&next.Visible,"middle page shows both available arrows");await Image("middle-page");
            next.EmitSignal(Button.SignalName.Pressed);Check(previous.Visible&&!next.Visible&&next.Disabled,"last page hides unavailable next arrow");
            Check(left.IsEqualApprox(previous.Position)&&right.IsEqualApprox(next.Position),"arrow positions stay fixed on short last page");
            previous.EmitSignal(Button.SignalName.Pressed);Check(previous.Visible&&next.Visible,"remaining arrow still navigates backwards");
            Sources(view,1);Check(!previous.Visible&&!next.Visible,"single source hides both arrows without moving slots");
            Sources(view,7);next.EmitSignal(Button.SignalName.Pressed);var arrow=next.GetNode<TextureRect>("Icon");
            Check(arrow.Material is ShaderMaterial material&&Math.Abs(material.GetShaderParameter("s").AsSingle()-1)<.01,"available native arrows retain colored appearance");
            reset.EmitSignal(Control.SignalName.MouseEntered);await Image("reset-hover");var hint=panel.GetNode<Label>("RebuildMirrorHint");Check(hint.Visible&&Rect(hint).End.Y<Rect(content).Position.Y,"reset hint remains above native content");reset.EmitSignal(Control.SignalName.MouseExited);
            preferences.GetMethod("RememberLayout",Any)!.Invoke(null,new object[]{new Vector2(300,220),700f});
            preferences.GetField("_path",Any)!.SetValue(null,"");preferences.GetMethod("LoadProfile",Any)!.Invoke(null,new object[]{(Action)(()=>{})});current=preferences.GetProperty("Current",Any)!.GetValue(null)!;
            Check((bool)preferences.GetProperty("HasLayout")!.GetValue(current)!&&(float)preferences.GetProperty("Width")!.GetValue(current)! ==700,".14 saved custom layout survives reload");
            File.WriteAllText(Path.Combine(Output,"geometry.json"),JsonSerializer.Serialize(new{viewport=new[]{bounds.X,bounds.Y},panel=new[]{panel.Position.X,panel.Position.Y,panel.Size.X,panel.Size.Y},mode=new[]{m.Position.X,m.Position.Y,m.Size.X,m.Size.Y},reset=new[]{r.Position.X,r.Position.Y,r.Size.X,r.Size.Y},pin=new[]{p.Position.X,p.Position.Y,p.Size.X,p.Size.Y},close=new[]{c.Position.X,c.Position.Y,c.Size.X,c.Size.Y},relicBottom}));
            File.WriteAllText(Path.Combine(Output,"result.json"),JsonSerializer.Serialize(new{passed=true,checks=_checks,scope="navigation and default layout only"}));GD.Print("[NavigationLayout] RESULT checks="+_checks);
        }
        catch(Exception e){exit=1;GD.Print("[NavigationLayout] FAIL "+e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());}
        Call("Suspend");((SceneTree)Engine.GetMainLoop()).Quit(exit);
    }
    private static System.Collections.Generic.IEnumerable<Node> Descendants(Node node){yield return node;foreach(Node child in node.GetChildren())foreach(var entry in Descendants(child))yield return entry;}
}

