using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

public static partial class MultiplayerVisibleProbe
{
    static bool FogEnabled => (bool)Get(null,"Enabled",Rmp("CrystalFog"))!;
    static object FogSnapshot()
    {
        var mask=DivinationScreen==null?null:Descendants<NCrystalSphereMask>(DivinationScreen).FirstOrDefault();
        return new{enabled=FogEnabled,mask=mask!=null,shader=mask?.Material is ShaderMaterial mat&&mat.GetShaderParameter("rmp_crystal_preview").AsBool(),
            cells=DivinationScreen==null?0:Descendants<NCrystalSphereCell>(DivinationScreen).Count(c=>c.Entity.IsHidden&&c.MouseFilter==Control.MouseFilterEnum.Stop),slPopup=(bool)Get(null,"ConfirmationOpen",QuickType)!,crystal=CrystalSnapshot()};
    }
    static JsonElement FogChild()=>ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"fog-state.json"));
    static async Task ObserveFog()
    {
        WritePath(Path.Combine(Root,"visible-crystal.json"),CrystalSnapshot());
        WritePath(Path.Combine(Root,"fog-state.json"),FogSnapshot());
        var request=ReadPath(Path.Combine(Root,"fog-capture.json"));
        if(request.ValueKind==JsonValueKind.Object&&request.GetProperty("label").GetString() is {} label&&label!=fogCaptured)
        {fogCaptured=label;await Frames(2);using var image=NGame.Instance.GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(Shared,Role+"-replica-"+label+".png"));}
    }
    static string fogCaptured="";
    static async Task FogCapture(string label)
    {
        await Pace(1500);using(var image=NGame.Instance.GetViewport().GetTexture().GetImage())image.SavePng(Path.Combine(Root,Role+"-source-"+label+".png"));
        WritePath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"fog-capture.json"),new{label});
        Write(Role+"-"+label+"-fog.json",new{source=FogSnapshot(),replica=FogChild()});await Pace(1500);
    }
    static bool ObservedFog(bool expected)=>FogChild().ValueKind==JsonValueKind.Object&&FogChild().GetProperty("enabled").GetBoolean()==expected;
    static async Task ReplicaKey(uint key)
    {
        var window=Get(Mirror,"Window")!;
        Check((bool)Call(window,"RouteKeyboard",0x100u,(nuint)key,(nint)1,true)!,"source window routes key into selected native replica "+key);
        await Pace(350);
        Call(window,"RouteKeyboard",0x100u,(nuint)key,unchecked((nint)(1L<<30|1)),true);await Pace(150);
        Call(window,"RouteKeyboard",0x101u,(nuint)key,unchecked((nint)(1L<<31|1L<<30|1)),true);await Pace(500);
    }
    static async Task FogF4(bool expected,bool remote=false)
    {
        if(remote)await ReplicaKey(0x73);
        else
        {
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.F4,Pressed=true});await Pace(300);
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.F4,Pressed=true,Echo=true});await Pace(150);
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.F4,Pressed=false});
        }
        await Wait(()=>remote?ReadPath(Path.Combine(Root,SourceRole+"-fog-source.json")).GetProperty("enabled").GetBoolean()==expected&&ObservedFog(expected):FogEnabled==expected,"F4 selected-source preference propagated");
        Check(remote?ObservedFog(expected):FogEnabled==expected,"F4 hold and echo toggle exactly once at correct source");
    }
    static async Task FogSourceClick(Control node)
    {
        var viewport=NGame.Instance.GetViewport();var p=node.GetGlobalRect().GetCenter();
        viewport.PushInput(new InputEventMouseMotion{Position=p,GlobalPosition=p},true);await Frames(3);
        viewport.PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=p,GlobalPosition=p},true);await Frames(3);
        viewport.PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=p,GlobalPosition=p},true);await Pace(1200);
    }
    static async Task TestFogMulti(RunState state)
    {
        Check(!FogEnabled&&ObservedFog(false),"new multiplayer profile defaults to opaque curtain");
        if(Id==1)await FogF4(true);await Barrier("one-player-preview");
        Check(FogEnabled==(Id==1),"preview preference stays local and does not alter other peers");
        await Wait(()=>ObservedFog(SourceId==1),"viewer reflects selected source preference independently");
        Check(ObservedFog(SourceId==1),"viewer follows selected source rather than viewer own preference");
        if(Id!=1)await FogF4(true);await Barrier("all-preview-on");await Wait(()=>ObservedFog(true),"all observed previews on");
        foreach(int count in new[]{3,6})
        {
            string tag="fog-"+count;await Enter(RoomType.Event,tag,ModelDb.Event<CrystalSphere>());await Sync(state,tag+"-entry");await Grant(tag);
            await Stage("F4 灰雾 · 多人原版 "+count+" 次占卜");await Remote("native","NEventOptionButton",count==3?0:1);
            await Wait(()=>Divination?.DivinationCount==count&&CrystalMatches(),"all native boards ready");await Pace(2500);
            string original=JsonSerializer.Serialize(CrystalSnapshot());
            Check(JsonSerializer.SerializeToElement(FogSnapshot()).GetProperty("shader").GetBoolean()&&FogChild().GetProperty("shader").GetBoolean(),"original and replica masks are translucent gray");await FogCapture(tag+"-enabled");await Pace(8000);
            await FogF4(false,true);await Barrier(tag+"-remote-f4-off");await Wait(()=>!FogEnabled&&ObservedFog(false),"all selected remote sources toggled off");await FogCapture(tag+"-disabled");
            Check(!JsonSerializer.SerializeToElement(FogSnapshot()).GetProperty("shader").GetBoolean()&&!FogChild().GetProperty("shader").GetBoolean(),"F4 restores native opaque mask in both windows");
            await FogF4(true,true);await Barrier(tag+"-preview-restored");await Wait(()=>FogEnabled&&ObservedFog(true),"all remote preview preferences restored");
            Check(original==JsonSerializer.Serialize(CrystalSnapshot()),"F4 does not change board hidden cells rewards or count");
            Check(JsonSerializer.SerializeToElement(FogSnapshot()).GetProperty("cells").GetInt32()>0,"hidden cells retain original clickable hitboxes");
            await Remote("native","="+ChildCrystal().GetProperty("small").GetString());await Wait(()=>ChildCrystal().GetProperty("tool").GetString()=="Small"&&CrystalMatches(),"small tool remains interactive through fog");
            await Remote("native","="+ChildCrystal().GetProperty("big").GetString());await Wait(()=>ChildCrystal().GetProperty("tool").GetString()=="Big"&&CrystalMatches(),"big tool remains interactive through fog");
            for(int step=0;step<count;step++)
            {
                await Stage("灰雾交互 · "+count+" 次路线第 "+(step+1)+" 次 · "+(step%2==0?"实况窗口":"本体"));
                if(step%2==0)
                {
                    var snapshot=ChildCrystal();var cards=snapshot.GetProperty("cards").EnumerateArray().ToArray();var card=cards[step%cards.Length];int slot=card.GetProperty("x").GetInt32()*11+card.GetProperty("y").GetInt32();
                    string fog=snapshot.GetProperty("fog").GetString()!;if(fog[slot]!='1')slot=fog.IndexOf('1');await Remote("native","=NCrystalSphereCell#"+slot);
                }
                else
                {
                    var target=Divination!.Items.OfType<MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems.CrystalSphereCardReward>().FirstOrDefault(i=>Divination.cells[i.Position.X,i.Position.Y].IsHidden);
                    var cell=Descendants<NCrystalSphereCell>(DivinationScreen!).First(c=>c.Entity.IsHidden&&(target==null||c.Entity.X==target.Position.X&&c.Entity.Y==target.Position.Y));
                    await FogSourceClick(cell);
                }
                await Barrier(tag+"-click-"+step);await Wait(()=>Divination?.DivinationCount==count-step-1&&ChildCrystal().GetProperty("count").GetInt32()==count-step-1&&CrystalMatches(),"one native click consumes exactly one divination");
                Check(CrystalMatches(),"fog preserves source/replica count and hidden-cell state step "+step);await Sync(state,tag+"-step-"+step);
                if(step==0)await FogCapture(tag+"-revealed");
            }
            int picked=0;
            for(int attempt=0;attempt<30;attempt++)
            {
                await Frames(15);
                if(ChoiceKind()=="Reward"){Check(ChoiceMatches(),"native divination reward choices match");await Remote("select");picked++;await Wait(()=>ChoiceKind()!="Reward","card reward claimed");continue;}
                var reward=ChildCrystal().GetProperty("rewards").EnumerateArray().FirstOrDefault(r=>r.GetProperty("type").GetString()!="PotionReward");
                if(reward.ValueKind==JsonValueKind.Object){await Remote("native","="+reward.GetProperty("path").GetString());continue;}
                var actions=Authority().GetProperty("Actions").EnumerateArray();
                if(actions.Any(a=>a.GetProperty("NativePath").GetString()!.Contains("NProceedButton"))){await Remote("native","NProceedButton");break;}
                await Pace(500);
            }
            await Sync(state,tag+"-rewards");Check(picked>0&&RunManager.Instance.EventSynchronizer.GetLocalEvent().IsFinished,"fog interaction completes original rewards and event");await FogCapture(tag+"-finished");
        }
        string prefs=ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath("rmp-spectator.json"));
        Check(JsonDocument.Parse(File.ReadAllText(prefs)).RootElement.GetProperty("CrystalFogPreview").GetBoolean(),"multiplayer preference saved in local profile");
        await TestFogShortcuts(state);Write(Role+"-result.json",new{passed=true,checks});
    }
}
