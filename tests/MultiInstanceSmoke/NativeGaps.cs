using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

public static partial class Smoke
{
    private static object GapProcess => Field(Static("_mirror")!, "Process")!;
    private static JsonElement GapStatus()
    {
        var path=Path.Combine((string)Field(GapProcess,"DirectoryPath")!,"feedback-state.json");
        if(!File.Exists(path)) return default;
        using var file=new FileStream(path,FileMode.Open,System.IO.FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var json=JsonDocument.Parse(file); return json.RootElement.Clone();
    }
    private static bool GapBool(string name) => GapStatus() is { ValueKind: JsonValueKind.Object } s && s.GetProperty(name).GetBoolean();
    private static string GapScreen() => GapStatus() is { ValueKind: JsonValueKind.Object } s ? s.GetProperty("Screen").GetString()! : "";
    private static void GapSend(uint message,float x,float y)
    {
        nint window=(nint)(long)Field(GapProcess,"Window")!;
        GetClientRect(window,out var rect); int px=(int)(x*rect.Right),py=(int)(y*rect.Bottom);
        Check(SendMessageTimeoutW(window,message,message==0x201?1u:0u,(nint)((py<<16)|(px&65535)),2,5000,out _)!=0,"native gap window input dispatched");
    }
    private static async Task GapClickRect(float[] rect)
    {
        float x=rect[0]+rect[2]/2,y=rect[1]+rect[3]/2;
        GapSend(0x200,x,y); GapSend(0x201,x,y); await Frames(2); GapSend(0x202,x,y); await Frames(30);
    }
    private static async Task GapKey(uint key)
    {
        nint window=(nint)(long)Field(GapProcess,"Window")!;
        Check(SendMessageTimeoutW(window,0x100,key,0,2,5000,out _)!=0,"replica modal key down dispatched"); await Frames(2);
        Check(SendMessageTimeoutW(window,0x101,key,0,2,5000,out _)!=0,"replica modal key up dispatched"); await Frames(30);
    }
    private static async Task GapClickHit(string kind)
    {
        object? hit=null;
        await Until(()=>((System.Collections.IEnumerable)Field(GapProcess,"LastHits")!).Cast<object>().Any(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! ==kind),kind+" native gap hit ready");
        hit=((System.Collections.IEnumerable)Field(GapProcess,"LastHits")!).Cast<object>().Last(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! ==kind);
        await GapClickRect((float[])hit.GetType().GetProperty("Rect")!.GetValue(hit)!);
    }
    private static async Task GapClickLocal(string name)
    {
        await Until(()=>GapStatus() is {ValueKind:JsonValueKind.Object} s && s.GetProperty("LocalControls").EnumerateArray().Any(c=>c.GetProperty("Name").GetString()==name),name+" original local control ready");
        var control=GapStatus().GetProperty("LocalControls").EnumerateArray().Last(c=>c.GetProperty("Name").GetString()==name);
        await GapClickRect(control.GetProperty("Rect").EnumerateArray().Select(v=>v.GetSingle()).ToArray());
    }
    private static async Task InspectThreeChoice()
    {
        await Frames(90);
        var operations=(System.Collections.IList)Type("MirrorJournal").GetField("Operations",Any)!.GetValue(null)!;
        int count=operations.Count;
        var hit=((System.Collections.IEnumerable)Field(GapProcess,"LastHits")!).Cast<object>().First(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! =="select");
        var r=(float[])hit.GetType().GetProperty("Rect")!.GetValue(hit)!;
        GapSend(0x200,r[0]+r[2]/2,r[1]+r[3]/2); GapSend(0x204,r[0]+r[2]/2,r[1]+r[3]/2); await Frames(2); GapSend(0x205,r[0]+r[2]/2,r[1]+r[3]/2);
        await Until(()=>GapBool("InspectOpen"),"three-choice right click opens original replica card inspector");
        Check(NGame.Instance.InspectCardScreen?.IsVisibleInTree()!=true && operations.Count==count,"inspection leaves source choice and journal untouched");
        await GapClickLocal("Upgrade");
        await Until(()=>GapBool("InspectUpgrade"),"original upgraded preview checkbox responds in replica");
        await GapClickLocal("Backstop");
        await Until(()=>!GapBool("InspectOpen") && Verified,"closing card inspection preserves pending three-choice task");
        Check(NOverlayStack.Instance?.Peek() is NCardRewardSelectionScreen or NChooseACardSelectionScreen,"right click never selects reward card");
    }
    private static async Task TestAbandonAndResults()
    {
        var operations=(System.Collections.IList)Type("MirrorJournal").GetField("Operations",Any)!.GetValue(null)!;
        int before=operations.Count;
        NRun.Instance.GlobalUi.TopBar.Pause.ForceClick(); await Frames(30);
        Descendants<NClickableControl>((Node)NCapstoneContainer.Instance.CurrentCapstoneScreen!).Single(b=>b.Name=="GiveUp").ForceClick(); await Frames(30);
        Descendants<NClickableControl>((Node)NModalContainer.Instance.OpenModal!).Single(b=>b.Name=="NoButton").ForceClick();
        NCapstoneContainer.Instance.Close(); await Frames(30);
        Check(operations.Count==before && Verified,"source abandon cancellation stays local and cannot poison replica journal");
        Call(Static("_view")!,"SetControlEnabled",true); await Frames(30);
        await GapClickHit("ui.pause"); await GapClickHit("ui.menu.GiveUp");
        await Until(()=>GapBool("AbandonOpen"),"replica GiveUp opens original confirmation inside replica");
        Check(NModalContainer.Instance?.OpenModal==null && !RunManager.Instance.IsAbandoned,"source has no abandon dialog before replica confirmation");
        await GapClickLocal("NoButton");
        await Until(()=>!GapBool("AbandonOpen") && Verified,"cancel GiveUp keeps run playable");
        await GapClickHit("ui.menu.GiveUp"); await GapKey(27);
        await Until(()=>!GapBool("AbandonOpen") && Verified,"Escape cancels replica abandon confirmation locally");
        await GapClickHit("ui.menu.GiveUp"); await GapClickLocal("YesButton");
        await Until(()=>Verified && NOverlayStack.Instance?.Peek() is NGameOverScreen && GapScreen()=="NGameOverScreen","confirmed replica abandon reaches original defeat results on both sides");
        Check(RunManager.Instance.IsAbandoned && !GapBool("FinalWin"),"only confirmed abandon sets authoritative abandoned outcome");
        await FinishResults();
    }
    private static async Task TestVictoryResults()
    {
        // Original fixture APIs advance to the final Architect and win flow.
        for(int i=0;i<3;i++) { await RunManager.Instance.EnterNextAct(); await Until(()=>Verified,"original finale transition remains synchronized"); await Frames(90); }
        Check(RunManager.Instance.DebugOnlyGetState()!.CurrentRoom!.IsVictoryRoom,"fixture reaches original final Architect room");
        await Until(()=>((System.Collections.IEnumerable)Field(GapProcess,"LastHits")!).Cast<object>().Any(h=>((string)h.GetType().GetProperty("NodeKey")!.GetValue(h)!).Contains("NEventOptionButton#",StringComparison.Ordinal)),"replica final Architect native options are available");
        Call(Static("_view")!,"SetControlEnabled",true); await Frames(30);
        var operations=(System.Collections.IList)Type("MirrorJournal").GetField("Operations",Any)!.GetValue(null)!;
        for(int option=0;option<15 && !RunManager.Instance.IsGameOver;option++)
        {
            int before=operations.Count;
            await GapClickNativeType("NEventOptionButton");
            await Until(()=>Verified && operations.Count>before,"final Architect option executes and converges"); await Frames(90);
        }
        await Until(()=>Verified && NOverlayStack.Instance?.Peek() is NGameOverScreen && GapScreen()=="NGameOverScreen" && GapBool("FinalWin"),"original victory results display in replica without source follow-up input");
        Controller.GetMethod("RecoverMirror",Any)!.Invoke(null,null);
        await Until(()=>Verified && GapScreen()=="NGameOverScreen" && GapBool("FinalWin"),"rebuilding at victory restores original results instead of replaying stale boss UI");
        Call(Static("_view")!,"SetControlEnabled",true); await Frames(30);
        await FinishResults();
    }
    private static async Task FinishResults()
    {
        await Until(()=>Verified && Descendants<NClickableControl>((Node)NOverlayStack.Instance.Peek()).Any(b=>b.IsEnabled && b.GetType().Name=="NGameOverContinueButton"),"native result Continue becomes interactive");
        await Frames(120); // Native results animate enabled controls into their final position.
        await GapClickNativeType("NGameOverContinueButton");
        await Until(()=>Verified && Descendants<NReturnToMainMenuButton>((Node)NOverlayStack.Instance.Peek()).Any(b=>b.IsEnabled),"native summary score animation completes on authority and replica");
        await Until(()=>GapStatus() is {ValueKind:JsonValueKind.Object} s && s.GetProperty("ProgressScore").GetInt32()==SaveManager.Instance.Progress.CurrentScore &&
            s.GetProperty("ProgressUnlocks").GetInt32()==SaveManager.Instance.Progress.TotalUnlocks,"replica native score and unlock progress match authority after summary");
        await GapClickNativeType("NReturnToMainMenuButton");
        await Until(()=>!RunManager.Instance.IsInProgress && Static("_view")==null,"result Main Menu finishes source run and closes replica panel");
    }
    private static async Task GapClickNativeType(string type)
    {
        object? hit=null;
        await Until(()=>((System.Collections.IEnumerable)Field(GapProcess,"LastHits")!).Cast<object>().Any(h=>
        {
            string key=(string)h.GetType().GetProperty("NodeKey")!.GetValue(h)!;
            return key.Contains(type+"#",StringComparison.Ordinal);
        }),type+" result hit ready");
        hit=((System.Collections.IEnumerable)Field(GapProcess,"LastHits")!).Cast<object>().Last(h=>((string)h.GetType().GetProperty("NodeKey")!.GetValue(h)!).Contains(type+"#",StringComparison.Ordinal));
        await GapClickRect((float[])hit.GetType().GetProperty("Rect")!.GetValue(hit)!);
    }
}
