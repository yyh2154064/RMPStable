using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Native personal divination is not part of CombatReplay. Preserve its original
// board/RNG and semantic cell/tool decisions, plus original remote reward packets.
internal static partial class MultiplayerNoncombat
{
    private static void InstallCrystal(Harmony harmony)
    {
        void Patch(Type type,string name,string prefix)=>harmony.Patch(type.GetMethod(name,Any)!,new HarmonyMethod(typeof(MultiplayerNoncombat).GetMethod(prefix,Any)!));
        Patch(typeof(NCrystalSphereScreen),"OnCellClicked",nameof(CrystalClicked));
        Patch(typeof(NCrystalSphereScreen),"SetBigDivination",nameof(CrystalBig));
        Patch(typeof(NCrystalSphereScreen),"SetSmallDivination",nameof(CrystalSmall));
        Patch(typeof(OneOffSynchronizer),"HandleCrystalSphereRewardsMessage",nameof(CrystalReceived));
    }
    private static NCrystalSphereScreen? CrystalScreen=>NOverlayStack.Instance?.Peek() as NCrystalSphereScreen;
    private static CrystalSphereMinigame? CrystalEntity=>CrystalScreen is { } screen?Read(screen,"_entity") as CrystalSphereMinigame:null;
    internal static string CrystalContext()
    {
        if(CrystalEntity is not { } entity)return "";
        return entity.DivinationCount+":"+entity.CrystalSphereTool+":"+string.Join("",entity.cells.Cast<CrystalSphereCell>().Select(c=>c.IsHidden?"1":"0"));
    }
    private static void CrystalClicked(NCrystalSphereScreen __instance,NCrystalSphereCell cell)
    {
        if(Recording&&Read(__instance,"_entity") is CrystalSphereMinigame { DivinationCount: >0 }&&cell.Entity.IsHidden)
            Add("crystalCell",LocalContext.NetId??0,cell.Entity.X+":"+cell.Entity.Y);
    }
    private static void CrystalBig()=>Add("crystalTool",LocalContext.NetId??0,"Big");
    private static void CrystalSmall()=>Add("crystalTool",LocalContext.NetId??0,"Small");
    private static void CrystalReceived(CrystalSphereRewardsMessage __0,ulong __1)=>Add("crystalRewards",__1,payload:MirrorState.Pack(__0));
    private static async Task ApplyCrystal(MirrorOperation operation,RunState state)
    {
        if(!ulong.TryParse(operation.NodeKey,out var id)||!LocalContext.IsMe(state.GetPlayer(id)))throw new InvalidOperationException("Divination decision does not belong to selected source");
        var game=NGame.Instance;
        for(int wait=0;CrystalScreen==null&&wait<300;wait++)await game.ToSignal(game.GetTree(),SceneTree.SignalName.ProcessFrame);
        var screen=CrystalScreen??throw new InvalidOperationException("Native divination screen is absent");
        if(operation.Kind=="crystalTool")
        {
            if(operation.Value is not ("Big" or "Small"))throw new InvalidOperationException("Unknown divination tool");
            Invoke(screen,operation.Value=="Big"?"SetBigDivination":"SetSmallDivination",Read(screen,operation.Value=="Big"?"_bigDivinationButton":"_smallDivinationButton"));
            return;
        }
        var value=operation.Value.Split(':');
        if(value.Length!=2||!int.TryParse(value[0],out int x)||!int.TryParse(value[1],out int y)||CrystalEntity is not { DivinationCount: >0 } entity||x<0||y<0||x>=entity.GridSize.X||y>=entity.GridSize.Y||!entity.cells[x,y].IsHidden)throw new InvalidOperationException("Invalid authoritative divination cell");
        var cell=LocalSpectatorSource.Descendants<NCrystalSphereCell>(screen).First(c=>c.Entity.X==x&&c.Entity.Y==y);
        await (Task)Invoke(screen,"OnCellClicked",cell)!;
    }
}
