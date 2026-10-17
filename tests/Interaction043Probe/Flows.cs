using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static async Task CompletePile(RunState state,string tag,int count=1,bool manual=false,string model="")
    {
        await Wait(()=>ChoiceKind()=="Pile"&&ChoiceMatches(),tag+" complete pile contract");
        Check(ChoiceMatches(),tag+" complete candidates and constraints match");
        if(count==0&&manual)
        {
            for(int i=0;i<2;i++)Check((await InteractionInput("selectNext")).GetProperty("success").GetBoolean(),tag+" optional multi-select "+i);
            for(int i=0;i<2;i++)Check((await InteractionInput("deselect")).GetProperty("success").GetBoolean(),tag+" deselect clears native highlight "+i);
        }
        for(int i=0;i<count;i++)Check((await InteractionInput("selectNext",model)).GetProperty("success").GetBoolean(),tag+" selected distinct native card "+i);
        if(manual)await RemoteBattle("choice");
        await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning&&ChoiceKind()=="",tag+" original queue resumes");
        await Sync(state,tag+"-result");
    }
    static async Task Tutor(RunState state,string model,string id,string tag)
    {
        var player=state.GetPlayer(SourceId);
        if(!player.PlayerCombatState!.Hand.Cards.Any(c=>c.GetType().Name==model))
        {
            await RemoteBattle("play","Wish");await Wait(()=>ChoiceKind()=="Pile",tag+" Wish tutor opens");
            Check((await InteractionInput("selectNext",id)).GetProperty("success").GetBoolean(),tag+" native tutor selection acknowledged");
            await Wait(()=>player.PlayerCombatState.Hand.Cards.Any(c=>c.GetType().Name==model)&&ChoiceKind()=="",tag+" tutor returns card");
        }
        await Sync(state,tag+"-tutored");
    }
    static async Task ShellClick()
    {
        var button=(Button)Get(View!,"_controlToggle")!;
        var point=button.GetGlobalTransformWithCanvas()*(button.Size/2);var viewport=button.GetViewport();
        viewport.PushInput(new InputEventMouseMotion{Position=point,GlobalPosition=point},true);
        viewport.PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point,GlobalPosition=point},true);
        await Frames(12);
        viewport.PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point,GlobalPosition=point},true);
        await Frames(8);
    }
    static async Task TestRemainingInteractions(RunState state)
    {
        int pid=Pid;
        for(int i=0;i<3;i++)
        {
            Call(View!,"ShowControlTip");var tip=(Window)Get(View!,"_controlTip")!;
            Check(!tip.PopupWindow&&tip.MousePassthrough&&tip.Unfocusable,"control tip is passive and cannot capture the mode click");
            await ShellClick();await Wait(()=>!Child().GetProperty("control").GetBoolean(),"expanded watch switch");
            Check(Get(Proc,"WindowVisible") is true&&Pid==pid,"watch switch preserves expanded native window");
            await ShellClick();await Wait(()=>Child().GetProperty("control").GetBoolean(),"expanded control switch");
            Check(Get(View!,"ControlEnabled") is true&&Pid==pid,"single expanded click reacquires control");
        }
        await Barrier("043-switches");
        await RemoteBattle("play","Wish");await Wait(()=>ChoiceKind()=="Pile"&&ChoiceMatches(),"eye choice");
        Check((await InteractionInput("scrollContract")).GetProperty("success").GetBoolean(),"local pile scrolling cannot change choice contract");
        Check((await InteractionInput("eye")).GetProperty("success").GetBoolean(),"eye hides native pile selector");
        Check((await InteractionInput("eye")).GetProperty("success").GetBoolean(),"eye restores native pile selector");
        // Keep a Wish available for later real card tutoring.
        Check((await InteractionInput("selectNext","WISH")).GetProperty("success").GetBoolean(),"eye-restored native choice acknowledged");await Sync(state,"043-eye-resume");
        foreach(bool control in new[]{true,false})
        {
            if(!control){Static("MultiplayerMirrorSession","RequestControl",false);await Wait(()=>!Child().GetProperty("control").GetBoolean(),"settings watch mode");}
            await WindowClick("pause");Check((await WindowClick("settings")).GetProperty("settings").GetBoolean(),"settings opens in control="+control);
            Check(!(await WindowClick("settingsBack")).GetProperty("settings").GetBoolean(),"visible settings back closes in control="+control);
            await WindowClick("resume");
        }
        await Grant("043-potions");
        for(int i=0;i<5;i++)
        {
            await Tutor(state,"DefendIronclad","DEFEND_IRONCLAD","defend-"+i);
            await RemoteBattle("play","DefendIronclad");await Sync(state,"defend-"+i);
        }
        int beforePotion=state.GetPlayer(SourceId).Potions.Count();
        await RemoteBattle("potion","LiquidMemories");await CompletePile(state,"043-liquid");
        Check(state.GetPlayer(SourceId).Potions.Count()==beforePotion-1,"liquid memories consumed once and original result applied");
        await RemoteBattle("potion","DropletOfPrecognition");await CompletePile(state,"043-droplet",model:"WISH");
        Check(state.GetPlayer(SourceId).Potions.Count()==beforePotion-2,"draw-pile potion consumed once");
        foreach(var spec in new[]{("SecretTechnique","SECRET_TECHNIQUE",1,false), ("SecretWeapon","SECRET_WEAPON",1,false), ("Charge","CHARGE",2,false), ("Seance","SEANCE",1,false), ("Hologram","HOLOGRAM",1,false), ("Dredge","DREDGE",3,false), ("NeowsFury","NEOWS_FURY",0,true)})
        {
            await Tutor(state,spec.Item1,spec.Item2,"043-"+spec.Item1);
            await RemoteBattle("play",spec.Item1,target:spec.Item1=="NeowsFury"?-2:-1);
            await CompletePile(state,"043-"+spec.Item1,spec.Item3,spec.Item4,spec.Item1=="SecretTechnique"?"SEANCE":spec.Item1=="SecretWeapon"?"NEOWS_FURY":"");
        }
        Check(Pid==pid,"reported interaction sequences retain resident native process");
    }
}
