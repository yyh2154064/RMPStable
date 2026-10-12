using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static int shopFailures,shopWiggles,shopTalks;
    static void InstallShopFeedback()
    {
        if(System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")!="shopFeedback")return;
        var h=new Harmony("RMP.Test.ShopFeedback19");
        h.Patch(typeof(NMerchantSlot).GetMethod("OnPurchaseFailed",Any)!,postfix:new HarmonyMethod(typeof(MultiplayerVisibleProbe).GetMethod(nameof(ShopFailed),Any)!));
        h.Patch(typeof(NMerchantSlot).GetMethod("WiggleAnimation",Any)!,postfix:new HarmonyMethod(typeof(MultiplayerVisibleProbe).GetMethod(nameof(ShopWiggled),Any)!));
        h.Patch(typeof(NMerchantDialogue).GetMethod("ShowForPurchaseAttempt",Any)!,postfix:new HarmonyMethod(typeof(MultiplayerVisibleProbe).GetMethod(nameof(ShopTalked),Any)!));
    }
    static void ShopFailed(NMerchantSlot __instance,PurchaseStatus status){if(status==PurchaseStatus.FailureGold&&Get(__instance,"_purchaseFailedTween") is Tween)shopFailures++;}
    static void ShopWiggled(NMerchantSlot __instance){var visual=Get(__instance,"Visual");float x=visual is Control c?c.Position.X:visual is Node2D n?n.Position.X:0;if(Get(__instance,"_originalVisualPosition") is float original&&Math.Abs(x-original)>.1f)shopWiggles++;}
    static void ShopTalked(NMerchantDialogue __instance,PurchaseStatus status){if(status==PurchaseStatus.FailureGold&&Get(Get(__instance,"_label"),"Text") is string text&&text.Length>0&&Get(__instance,"_tween") is Tween)shopTalks++;}
    static object ShopSnapshot()=>new{failures=shopFailures,wiggles=shopWiggles,talks=shopTalks};
    static void ObserveShopFeedback(){if(System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="shopFeedback")WritePath(Path.Combine(Root,"visible-shop-feedback.json"),ShopSnapshot());}
    static JsonElement ChildShopFeedback()=>ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"visible-shop-feedback.json"));
    static async Task TestShopFeedback(RunState state)
    {
        int pid=Pid;foreach(var p in state.Players)p.Gold=0;
        await Barrier("shop-no-money-fixture");await Enter(RoomType.Shop,"no-money-shop");await Sync(state,"no-money-shop-entry");
        foreach(var inventory in Descendants<NMerchantInventory>(NRun.Instance.MerchantRoom))inventory.Open();await Frames(20);await Grant("no-money-shop");
        await Stage("后台商店专项 · 没钱时原版动画与老板对话");var me=LocalContext.GetMe(state)!;
        foreach(string category in new[]{"NMerchantCard#","NMerchantRelic","NMerchantPotion","NMerchantCardRemoval"}){
            int before=shopFailures;var childBefore=ChildShopFeedback();int replicaBefore=childBefore.GetProperty("failures").GetInt32(),wiggleBefore=childBefore.GetProperty("wiggles").GetInt32(),talkBefore=childBefore.GetProperty("talks").GetInt32();
            string inventory=Inventory((MerchantRoom)state.CurrentRoom!,me);int deck=me.Deck.Cards.Count,relics=me.Relics.Count,potions=me.Potions.Count();
            await Barrier("before-no-money-"+category);await Remote("buy",category,0);
            await Wait(()=>shopFailures==before+1&&ChildShopFeedback().GetProperty("failures").GetInt32()==replicaBefore+1&&ChildShopFeedback().GetProperty("wiggles").GetInt32()>wiggleBefore&&ChildShopFeedback().GetProperty("talks").GetInt32()==talkBefore+1,"native failure wiggle/dialogue "+category);
            Check(me.Gold==0&&me.Deck.Cards.Count==deck&&me.Relics.Count==relics&&me.Potions.Count()==potions&&Inventory((MerchantRoom)state.CurrentRoom!,me)==inventory,"unaffordable "+category+" preserves gold/stock/deck/relics/potions");
            Check(shopTalks==shopFailures&&shopWiggles>0,"source original unaffordable attempt creates real merchant speech and item movement");
            Check(ChildShopFeedback().GetProperty("talks").GetInt32()==ChildShopFeedback().GetProperty("failures").GetInt32(),"replica original unaffordable attempt creates real merchant speech and item movement");
            await Sync(state,"no-money-result-"+category);
        }
        Check(Pid==pid,"merchant feedback retains exact native replica process");Write(Role+"-result.json",new{passed=true,checks,visible=false,actualRendering=true,scope="independent three-player unaffordable card/relic/potion/removal native wiggle and merchant dialogue"});await Barrier("shop-feedback-done");
    }
}
