using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

public static partial class MultiplayerVisibleProbe
{
    static Type QuickType=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Features.QuickSl.QuickSlController")).First(t=>t!=null)!;
    static bool QuickPopup=>(bool)Get(null,"ConfirmationOpen",QuickType)!;
    static bool transportStarted;
    static void InstallFogSlTransport()
    {
        if(Renderer)return;
        // Only the Steam discovery/reconnect boundary is replaced by localhost
        // ENet. Real F5, permission, confirmation, QuickSL begin/broadcast,
        // teardown, saved checkpoint, native loaded lobby and ready flow execute.
        var h=new Harmony("RMP.CrystalFog.LocalSlTransport");
        h.Patch(QuickType.GetMethod("ReturnAndReconnectClientAsync",Any)!,prefix:new HarmonyMethod(typeof(MultiplayerVisibleProbe).GetMethod(nameof(FogClientTransport),Any)!));
        var bootstrap=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Network.HostBootstrapModule")).First(t=>t!=null)!;
        h.Patch(bootstrap.GetMethod("StartQuickSlLoadedHostAsync",Any)!,prefix:new HarmonyMethod(typeof(MultiplayerVisibleProbe).GetMethod(nameof(FogHostTransport),Any)!));
    }
    static bool FogClientTransport(object __0,ref Task __result)
    {
        __0.GetType().GetField("ClientDeadline",Any)!.SetValue(__0,DateTime.UtcNow.AddMinutes(3));
        __result=FogReconnectClient();return false;
    }
    static async Task FogReconnectClient()
    {
        transportStarted=true;QuickType.GetField("_operationRunning",Any)!.SetValue(null,true);
        Write(Role+"-actual-sl-begin.json",new{nativeBeginReceived=true,transport="localhost ENet"});
        try{await NGame.Instance.ReturnToMainMenu();await FogLoadedTransport();}
        finally{QuickType.GetField("_operationRunning",Any)!.SetValue(null,false);}
    }
    static bool FogHostTransport(ref Task<bool> __result)
    {transportStarted=true;__result=FogLoadedTransport();return false;}
    static async Task<bool> FogLoadedTransport()
    {
        Write(Role+"-actual-sl-left.json",new{left=true});await Barrier("actual-sl-left");
        if(Host)
        {
            var saved=SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(Id);
            if(!saved.Success||saved.SaveData==null)throw new Exception("Actual native multiplayer checkpoint missing");
            Write("actual-sl-checkpoint.json",new{payload=Convert.ToBase64String((byte[])Static("MirrorState","Pack",saved.SaveData)!)});
        }
        await Barrier("actual-sl-checkpoint");
        var savedBytes=Convert.FromBase64String(ReadPath(Path.Combine(Root,"actual-sl-checkpoint.json")).GetProperty("payload").GetString()!);
        var save=(SerializableRun)Rmp("MirrorState").GetMethod("Unpack",Any)!.MakeGenericMethod(typeof(SerializableRun)).Invoke(null,new object[]{savedBytes})!;
        var service=await ConnectPlayers((ushort)(ushort.Parse(System.Environment.GetEnvironmentVariable("RMP_VISIBLE_PORT")!)+8),"fog-actual-sl");
        await JoinLoadedPlayers(service,save);
        // Production ProcessRecoveryLobby readies each client and the host last.
        await Wait(()=>RunManager.Instance.IsInProgress&&RunManager.Instance.DebugOnlyGetState()!=null&&NRun.Instance?.IsNodeReady()==true,"native automatic loaded-run entry",120);
        networkPump=false;Write(Role+"-actual-sl-entered.json",new{nativeReadyCompleted=true});return true;
    }
    static async Task DismissSourcePopup()
    {
        if(NModalContainer.Instance?.OpenModal is not Node popup)throw new Exception("Expected host SL request popup");
        var no=Descendants<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>(popup).First(c=>c.Name=="NoButton"&&c.IsEnabled);
        no.ForceClick();await Pace(1200);
    }
    static async Task TestFogShortcuts(RunState state)
    {
        await Stage("实况 F4/F5 · 权限和所选好友校验");await Grant("shortcuts");
        int pid=Pid;ulong selected=SourceId;
        // Releasing control blocks both shortcuts at the renderer before any
        // source command can be sent; F8 remains routed only to the local owner.
        Call(View,"SetPreferredControl",false);await Wait(()=>!Child().GetProperty("control").GetBoolean(),"readonly mode applied");
        bool before=FogEnabled;await ReplicaKey(0x73);await ReplicaKey(0x74);await Pace(1500);
        Check(FogEnabled==before&&!FogChild().GetProperty("slPopup").GetBoolean(),"readonly F4/F5 cannot mutate source or open SL confirmation");
        var window=Get(Mirror,"Window")!;
        Check(Call(window,"RouteKeyboard",0x100u,(nuint)0x77,(nint)1,true) is false,"F8 stays with local owner and is never a remote shortcut");
        Call(window,"RouteKeyboard",0x101u,(nuint)0x77,unchecked((nint)(1L<<31|1L<<30|1)),true);
        await Grant("shortcuts-regrant");
        if(Id==1)
        {
            // Viewer 1 observes client 2, while viewer 3 observes host 1.
            await ReplicaKey(0x73);await Wait(()=>ObservedFog(false),"remote F4 changes selected client only");
            Check(FogEnabled,"remote F4 does not change viewer's own local preference");
        }
        await Barrier("only-client-two-off");Check(FogEnabled==(Id!=2),"remote F4 changes only the selected friend");
        if(Id==1){await ReplicaKey(0x73);await Wait(()=>ObservedFog(true),"remote F4 restores selected client preference");}
        await Barrier("client-two-restored");await Wait(()=>FogEnabled&&ObservedFog(true),"all preview preferences settled");
        if(Id==1)
        {
            await ReplicaKey(0x74);await Wait(()=>FogChild().GetProperty("slPopup").GetBoolean(),"F5 asks within selected client's replica");
            await ReplicaKey(27);await Wait(()=>!FogChild().GetProperty("slPopup").GetBoolean(),"cancel remote SL");
            Check(ReferenceEquals(state,RunManager.Instance.DebugOnlyGetState()),"cancelled replica F5 does not reload viewer or friend");
            await ReplicaKey(0x74);await Wait(()=>FogChild().GetProperty("slPopup").GetBoolean(),"client F5 confirmation reopens");await ReplicaKey(13);
        }
        if(Host)
        {
            await Wait(()=>QuickPopup,"selected client sends native SL request to host");
            Check(NModalContainer.Instance?.OpenModal!=null,"remote client F5 follows original host approval flow");
            await DismissSourcePopup();
        }
        await Barrier("client-sl-request-declined");
        // The selected client receives the original rejection info popup.
        if(Id==2)
        {
            await Wait(()=>QuickPopup,"native SL rejection info reaches requesting friend");
            var popup=(Node)NModalContainer.Instance.OpenModal!;
            Descendants<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>(popup).First(c=>c.Name=="YesButton"&&c.IsEnabled).ForceClick();
        }
        await Barrier("client-sl-info-dismissed");await Sync(state,"before-host-remote-sl");
        if(SaveManager.Instance.CurrentRunSaveTask is {} saving)await saving;
        if(Host)
        {
            await SaveManager.Instance.SaveRun(null);
            var checkpoint=SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(Id);
            Check(checkpoint.Success&&checkpoint.SaveData!=null,"native host multiplayer checkpoint exists before F5");
        }
        await Barrier("native-host-checkpoint-saved");
        await Stage("实况 F5 · 操控好友房主 · 原版全队快速 SL");await Barrier("host-sl-ready");
        if(Id==3)
        {
            await ReplicaKey(0x74);await Wait(()=>FogChild().GetProperty("slPopup").GetBoolean(),"F5 within selected host replica");await ReplicaKey(13);
        }
        await Wait(()=>transportStarted&&!ReferenceEquals(state,RunManager.Instance.DebugOnlyGetState())&&RunManager.Instance.IsInProgress&&Session("IsRestoring") is false&&Get(Proc,"DisplayReady") is true,"actual host QuickSL restores all original peers",180);
        var restored=RunManager.Instance.DebugOnlyGetState()!;await Barrier("actual-sl-restored");
        Check(Pid==pid&&SourceId==selected,"remote F5 retains each native replica and selected friend");
        await Wait(()=>FogEnabled&&ObservedFog(true),"F4 preference survives native multiplayer SL");
        Check(restored.Players.Count==3,"original native loaded-run SL retains all three real players");await Sync(restored,"actual-host-f5-restored");await FogCapture("remote-f5-restored");
    }
}
