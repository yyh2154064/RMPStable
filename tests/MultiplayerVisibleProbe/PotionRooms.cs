using System;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static async Task TestPotionRooms(RunState state)
    {
        await Stage("独立药水专项 · 战斗自身、队友、敌人和取消");
        await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.Monster).coord);
        await Wait(()=>CombatManager.Instance.IsInProgress&&(bool)Get(Mirror,"Verified")!,"potion fixture native combat");await Barrier("potion-battle-ready");await GrantBattle("potion-battle");
        await TestPotionTargets(state);
        await Stage("独立药水专项 · 血药逐个治疗队友");
        for(ulong actor=1;actor<=3;actor++){
            string tag="blood-combat-ally-"+actor;await Barrier("before-"+tag);var before=state.Players.ToDictionary(p=>p.NetId,p=>p.Creature.CurrentHp);
            if(SourceId==actor)await RemoteBattle("potion","BloodPotion",target:-3);
            await Barrier("issued-"+tag);await Wait(()=>!state.Players.First(p=>p.NetId==actor).Potions.OfType<BloodPotion>().Any(),"combat blood potion source consumed "+actor);await Sync(state,tag);
            Check(state.Players.All(p=>p.Creature.CurrentHp==before[p.NetId]+(p.NetId==actor%3+1?Math.Min(p.Creature.MaxHp-before[p.NetId],Math.Ceiling(p.Creature.MaxHp*.2m)):0)),tag+" heals only the intended teammate by native amount");
        }
        for(ulong actor=1;actor<=3;actor++){
            await Barrier("before-fire-potion-"+actor);var enemies=CombatManager.Instance.DebugOnlyGetState().Enemies.ToArray();var before=enemies.Select(e=>e.CurrentHp).ToArray();
            if(SourceId==actor)await RemoteBattle("potion","FirePotion",target:-2);
            await Barrier("issued-fire-potion-"+actor);await Wait(()=>!state.Players.First(p=>p.NetId==actor).Potions.OfType<FirePotion>().Any(),"source fire potion consumed");await Sync(state,"fire-potion-"+actor);
            Check(enemies.Where((e,i)=>e.CurrentHp<before[i]).Count()==1,"fire potion damages exactly one intended enemy for source "+actor);
        }
        await PrepareNoncombat(state,state.Players.ToArray());
        foreach(var player in state.Players){player.AddPotionInternal(ModelDb.Potion<BloodPotion>().ToMutable(),silent:false);player.AddPotionInternal(ModelDb.Potion<BloodPotion>().ToMutable(),silent:false);player.AddPotionInternal(ModelDb.Potion<BlockPotion>().ToMutable(),silent:false);}
        await Barrier("potion-room-fixture-ready");await Enter(RoomType.Shop,"potion-outside-battle");await Sync(state,"potion-room-entry");await Grant("potion-room");
        Check(!Authority().GetProperty("Actions").EnumerateArray().Any(a=>a.GetProperty("Kind").GetString()=="potion"&&a.GetProperty("CardId").GetString()!.Contains("BLOCK_POTION")),"combat-only potion has no usable authority outside battle");
        await Stage("独立药水专项 · 战斗外原版自身用药及拒绝队友目标");
        var local=LocalContext.GetMe(state)!;var roomAuthority=Authority();var blood=roomAuthority.GetProperty("Actions").EnumerateArray().First(a=>a.GetProperty("Kind").GetString()=="potion");
        Check(blood.GetProperty("TargetIds").GetArrayLength()==1,"native noncombat blood potion exposes exactly its source owner target");
        var other=roomAuthority.GetProperty("Targets").EnumerateArray().First(t=>t.GetProperty("NativeIndex").GetInt32()!=state.Players.ToList().FindIndex(p=>p.NetId==SourceId));int rejected=(int)Session("RejectedControlRequests")!;string unchanged=Hash(state);
        Forward(System.Text.Json.JsonSerializer.SerializeToElement(new{Epoch=roomAuthority.GetProperty("Epoch").GetInt64(),Request=2001,Generation=Get(Mirror,"LocalGeneration"),Events=Get(Proc,"LastEvents"),NodeKey=blood.GetProperty("Id").GetString(),Value=roomAuthority.GetProperty("Context").GetString(),TargetKey=other.GetProperty("Id").GetString()}));
        await Barrier("outside-invalid-ally-request");await Wait(()=>(int)Session("RejectedControlRequests")!>rejected,"source rejects noncombat ally blood potion target");Check(Hash(state)==unchanged&&local.Potions.OfType<BloodPotion>().Count()==2,"rejected noncombat ally request changes no HP or inventory");
        Static("MultiplayerMirrorSession","RevokeControl");await Barrier("outside-invalid-ally-revoke");await Wait(()=>!Child().GetProperty("control").GetBoolean(),"invalid noncombat grant closed");await Grant("outside-invalid-ally-regrant");
        foreach(int dose in new[]{1,2})for(ulong actor=1;actor<=3;actor++){
            string tag="blood-self-"+dose+"-"+actor;await Barrier("before-"+tag);var before=state.Players.ToDictionary(p=>p.NetId,p=>p.Creature.CurrentHp);
            if(SourceId==actor)await RemoteBattle("potion","BloodPotion",target:-4);
            await Barrier("issued-"+tag);await Wait(()=>state.Players.First(p=>p.NetId==actor).Potions.OfType<BloodPotion>().Count()==2-dose,"source blood potion consumed "+tag);
            await Sync(state,tag);ulong target=actor;
            Check(state.Players.All(p=>p.Creature.CurrentHp==before[p.NetId]+(p.NetId==target?Math.Min(p.Creature.MaxHp-before[p.NetId],Math.Ceiling(p.Creature.MaxHp*.2m)):0)),tag+" heals exactly the selected player through original noncombat action");
        }
        Write(Role+"-result.json",new{passed=true,checks,visible=true,scope="independent multiplayer potion native clicks, self/ally/enemy, cancellation, invalid targets, combat and noncombat"});await Barrier("potion-done");
    }
}
