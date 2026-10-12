using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static int potionFeedbackHandled;
    static Vector2 NativePoint(Control control)
    {
        // Embedded native child size can precede Godot's WindowGetSize cache.
        // Invert the renderer's actual client-to-canvas mapping, used by clicks.
        var renderer=Get(null,"Active",Rmp("MirrorRenderer"))!;
        var zero=(Vector2)Call(renderer,"ViewportPoint",Vector2.Zero)!;
        var one=(Vector2)Call(renderer,"ViewportPoint",Vector2.One)!;
        return (control.GetGlobalTransformWithCanvas()*(control.Size/2)-zero)/(one-zero);
    }
    static async Task NativeClick(object renderer,Vector2 point,uint down=0x201,uint up=0x202)
    {
        var queue=(System.Collections.Concurrent.ConcurrentQueue<(uint,Vector2,long,long,float)>)Get(renderer,"_buttons")!;
        long generation=(long)Get(renderer,"_generation")!,epoch=(long)Get(renderer,"_epoch")!;
        queue.Enqueue((down,point,generation,epoch,0));await Frames(3);queue.Enqueue((up,point,generation,epoch,0));await Frames(8);
    }
    static async Task ObservePotionFeedback(object renderer,RunState state)
    {
        var input=ReadPath(Path.Combine(Root,"visible-potion-feedback.json"));
        if(input.ValueKind!=JsonValueKind.Object||input.GetProperty("seq").GetInt32()==potionFeedbackHandled||Get(renderer,"MultiplayerInputReady") is not true)return;
        potionFeedbackHandled=input.GetProperty("seq").GetInt32();long requests=(long)Get(renderer,"_request")!;
        var me=LocalContext.GetMe(state)!;
        var holder=Descendants<NPotionHolder>(NRun.Instance.GlobalUi.TopBar).First(h=>h.Potion?.Model is BlockPotion);
        await NativeClick(renderer,NativePoint(holder));
        bool opened=Get(renderer,"PotionPopup") is NPotionPopup;
        await NativeClick(renderer,new Vector2(.86f,.62f));
        bool leftCancelled=Get(renderer,"PotionPopup")==null&&!NTargetManager.Instance.IsInSelection;
        await NativeClick(renderer,NativePoint(holder));
        var popup=Get(renderer,"PotionPopup") as NPotionPopup;
        if(popup==null)throw new Exception("Potion popup did not reopen after cancellation");
        await NativeClick(renderer,NativePoint((NClickableControl)Get(popup,"_useButton")!));
        bool targeting=NTargetManager.Instance.IsInSelection;
        var ally=NRun.Instance.CombatRoom.CreatureNodes.First(n=>n.Entity.IsPlayer&&n.Entity.Player!.NetId!=me.NetId);
        var buttons=(System.Collections.Concurrent.ConcurrentQueue<(uint,Vector2,long,long,float)>)Get(renderer,"_buttons")!;
        buttons.Enqueue((0x201,NativePoint(ally.Hitbox),(long)Get(renderer,"_generation")!,(long)Get(renderer,"_epoch")!,0));await Frames(3);
        var enemy=NRun.Instance.CombatRoom.CreatureNodes.First(n=>n.Entity.IsEnemy&&n.Entity.IsAlive);
        await NativeClick(renderer,NativePoint(enemy.Hitbox));
        bool invalidIgnored=(long)Get(renderer,"_request")! ==requests&&me.Potions.OfType<BlockPotion>().Any();
        // Native ClickMouseToTarget ends selection on an invalid click.
        // The invariant is no use request or consumption, not a persistent arrow.
        if(!NTargetManager.Instance.IsInSelection){await NativeClick(renderer,NativePoint(holder));var retry=Get(renderer,"PotionPopup") as NPotionPopup;if(retry==null)throw new Exception("Cannot retarget potion after invalid target");await NativeClick(renderer,NativePoint((NClickableControl)Get(retry,"_useButton")!));}
        await NativeClick(renderer,new Vector2(.86f,.62f),0x204,0x205);
        bool rightCancelled=!NTargetManager.Instance.IsInSelection;
        await NativeClick(renderer,NativePoint(holder));popup=Get(renderer,"PotionPopup") as NPotionPopup;
        if(popup==null)throw new Exception("Potion popup did not reopen after targeting cancellation");
        await NativeClick(renderer,NativePoint((NClickableControl)Get(popup,"_useButton")!));
        var keys=(System.Collections.Concurrent.ConcurrentQueue<(uint,uint,long,long,long)>)Get(renderer,"_keyboard")!;
        keys.Enqueue((0x100,27,0,(long)Get(renderer,"_generation")!,(long)Get(renderer,"_epoch")!));await Frames(3);
        keys.Enqueue((0x101,27,0,(long)Get(renderer,"_generation")!,(long)Get(renderer,"_epoch")!));await Frames(8);
        WritePath(Path.Combine(Root,"visible-potion-feedback-output.json"),new{seq=potionFeedbackHandled,opened,leftCancelled,targeting,invalidIgnored,rightCancelled,escapeCancelled=!NTargetManager.Instance.IsInSelection,unchanged=(long)Get(renderer,"_request")! ==requests&&me.Potions.OfType<BlockPotion>().Any()});
    }
    static async Task TestPotionTargets(RunState state)
    {
        await Stage("多人药水专项 · 取消、非法目标、逐个为队友使用");
        int seq=++sequence;string directory=(string)Get(Proc,"DirectoryPath")!;
        WritePath(Path.Combine(directory,"visible-potion-feedback.json"),new{seq});
        await Wait(()=>ReadPath(Path.Combine(directory,"visible-potion-feedback-output.json")).ValueKind==JsonValueKind.Object,"native potion cancellation feedback");
        var output=ReadPath(Path.Combine(directory,"visible-potion-feedback-output.json"));
        foreach(string check in new[]{"opened","leftCancelled","targeting","invalidIgnored","rightCancelled","escapeCancelled","unchanged"})Check(output.GetProperty(check).GetBoolean(),"multiplayer native potion "+check);
        await Barrier("potion-cancellation");
        var authority=Authority();var action=authority.GetProperty("Actions").EnumerateArray().First(a=>a.GetProperty("Kind").GetString()=="potion"&&a.GetProperty("CardId").GetString()!.Contains("BLOCK_POTION"));
        var enemy=authority.GetProperty("Targets").EnumerateArray().First(t=>t.GetProperty("Enemy").GetBoolean());int rejected=(int)Session("RejectedControlRequests")!;
        // Each request is sent to the next source. All three must capture
        // their own receiver baseline before any peer sends its forged target.
        await Barrier("before-invalid-potion-source-request");
        Forward(JsonSerializer.SerializeToElement(new{Epoch=authority.GetProperty("Epoch").GetInt64(),Request=2001,Generation=Get(Mirror,"LocalGeneration"),Events=Get(Proc,"LastEvents"),NodeKey=action.GetProperty("Id").GetString(),Value=authority.GetProperty("Context").GetString(),TargetKey=enemy.GetProperty("Id").GetString()}));
        await Barrier("invalid-potion-source-request");await Wait(()=>(int)Session("RejectedControlRequests")!>rejected,"source rejects enemy as ally potion target");
        Check(LocalContext.GetMe(state)!.Potions.OfType<BlockPotion>().Any(),"invalid ally potion request does not consume source potion");
        // This injected malformed request uses an artificial high sequence.
        // Establish a fresh native grant before normal renderer-generated IDs.
        Static("MultiplayerMirrorSession","RevokeControl");await Barrier("after-invalid-potion-revoke");await Wait(()=>!Child().GetProperty("control").GetBoolean(),"malformed request grant closed");await GrantBattle("after-invalid-potion-regrant");
        for(ulong actor=1;actor<=3;actor++)
        {
            await Barrier("before-ally-potion-"+actor);var before=state.Players.ToDictionary(p=>p.NetId,p=>p.Creature.Block);
            if(SourceId==actor)await RemoteBattle("potion","BlockPotion",target:-3);
            await Barrier("issued-ally-potion-"+actor);await Wait(()=>!state.Players.First(p=>p.NetId==actor).Potions.OfType<BlockPotion>().Any(),"ally potion source consumed "+actor);
            await Sync(state,"ally-potion-"+actor);
            Check(state.Players.All(p=>p.Creature.Block==before[p.NetId]+(p.NetId==actor%3+1?12:0)),"actor "+actor+" gives exactly 12 block only to intended teammate "+(actor%3+1));
        }
    }
}
