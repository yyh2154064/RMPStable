using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static int windowInputHandled;
    static Node? SettingsNode()=>Descendants<Node>(NGame.Instance).FirstOrDefault(n=>n.GetType().Name=="NSettingsScreen"&&n is Control c&&c.IsVisibleInTree());
    static async Task ObserveWindowInput(object renderer,RunState state)
    {
        var request=ReadPath(Path.Combine(Root,"visible-window-input.json"));
        if(request.ValueKind!=JsonValueKind.Object||request.GetProperty("seq").GetInt32()==windowInputHandled)return;
        windowInputHandled=request.GetProperty("seq").GetInt32();string kind=request.GetProperty("kind").GetString()!;
        Control? node=kind switch{
            "map"=>NRun.Instance.GlobalUi.TopBar.Map,"pause"=>NRun.Instance.GlobalUi.TopBar.Pause,"deck"=>NRun.Instance.GlobalUi.TopBar.Deck,
            "settings"=>Descendants<NClickableControl>((Node)NCapstoneContainer.Instance.CurrentCapstoneScreen).First(n=>n.Name=="Settings"&&n.IsVisibleInTree()),
            "resume"=>Descendants<NClickableControl>((Node)NCapstoneContainer.Instance.CurrentCapstoneScreen).First(n=>n.Name=="Resume"&&n.IsVisibleInTree()),
            "settingsBack"=>SettingsNode()==null?null:Descendants<NBackButton>(SettingsNode()!).FirstOrDefault(n=>n.IsEnabled&&n.IsVisibleInTree()),
            "back"=>NCapstoneContainer.Instance.CurrentCapstoneScreen is Node screen?Get(screen,"_backButton") as Control??Descendants<NBackButton>(screen).FirstOrDefault(n=>n.IsEnabled&&n.IsVisibleInTree()):null,
            "drawPile"=>Descendants<MegaCrit.Sts2.Core.Nodes.Combat.NDrawPileButton>(NRun.Instance).First(),
            "discardPile"=>Descendants<MegaCrit.Sts2.Core.Nodes.Combat.NDiscardPileButton>(NRun.Instance).First(),
            "exhaustPile"=>Descendants<MegaCrit.Sts2.Core.Nodes.Combat.NExhaustPileButton>(NRun.Instance).First(),
            "inspectCard"=>Descendants<NCardHolder>((Node)NCapstoneContainer.Instance.CurrentCapstoneScreen).First(h=>h.IsVisibleInTree()).Hitbox,
            "inspectCardBack"=>Get(NGame.Instance.InspectCardScreen,"_backstop") as Control,
            "settingsTab"=>SettingsNode()==null?null:Descendants<MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsTab>(SettingsNode()!).Where(t=>t.IsVisibleInTree()).ElementAt((int)request.GetProperty("id").GetUInt64()),
            "relic"=>Descendants<NRelicInventoryHolder>(NRun.Instance).First(n=>n.IsEnabled&&n.IsVisibleInTree()),
            "inspectBack"=>NGame.Instance.InspectRelicScreen?.IsVisibleInTree()==true?Get(NGame.Instance.InspectRelicScreen,"_backstop") as Control:null,
            "player"=>Descendants<NMultiplayerPlayerState>(NRun.Instance).First(n=>n.Player.NetId==request.GetProperty("id").GetUInt64()).Hitbox,
            _=>null};
        string localHit="",rectangle="";
        if(node!=null){var area=node is NBackButton&&Get(node,"_buttonImage") is Control image?image:node;var point=NativePoint(area);localHit=Call(renderer,"LocalUiAt",point)?.GetType().Name??"none";rectangle=node.GetGlobalRect().ToString();await NativeClick(renderer,point);}
        await Pace(1200);
        var capstone=NCapstoneContainer.Instance.CurrentCapstoneScreen;
        var player=capstone is NMultiplayerPlayerExpandedState expanded?Get(expanded,"_player") as MegaCrit.Sts2.Core.Entities.Players.Player:null;
        int selectedTab=SettingsNode()==null?-1:Descendants<MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsTab>(SettingsNode()!).Where(t=>t.IsVisibleInTree()).ToList().FindIndex(t=>Get(t,"_isSelected") is true);
        WritePath(Path.Combine(Root,"visible-window-output.json"),new{seq=windowInputHandled,kind,found=node!=null,localHit,rectangle,map=NMapScreen.Instance.IsOpen,settings=SettingsNode()!=null,selectedTab,capstone=capstone?.GetType().Name,inspectCard=NGame.Instance.InspectCardScreen?.IsVisibleInTree()==true,inspectRelic=NGame.Instance.InspectRelicScreen?.IsVisibleInTree()==true,player=player?.NetId,cards=player?.Deck.Cards.Count,relics=player?.Relics.Count,potions=player?.Potions.Count(),hash=Hash(state),control=Get(renderer,"_control")});
        using var capture=NGame.Instance.GetViewport().GetTexture().GetImage();capture.SavePng(Path.Combine(Shared,Role+"-window-"+windowInputHandled+"-"+kind+".png"));
    }
    static async Task<JsonElement> WindowClick(string kind,ulong id=0)
    {
        await Pace();int seq=++sequence;string dir=(string)Get(Proc,"DirectoryPath")!;
        WritePath(Path.Combine(dir,"visible-window-input.json"),new{seq,kind,id});
        await Wait(()=>ReadPath(Path.Combine(dir,"visible-window-output.json")).ValueKind==JsonValueKind.Object&&ReadPath(Path.Combine(dir,"visible-window-output.json")).GetProperty("seq").GetInt32()==seq,"window click "+kind);
        var output=ReadPath(Path.Combine(dir,"visible-window-output.json"));Write(Role+"-window-result-"+seq+".json",output);
        Check(output.GetProperty("found").GetBoolean(),"native window input finds "+kind);return output;
    }
    static async Task TestWindowInteractions(RunState state)
    {
        await Enter(MegaCrit.Sts2.Core.Rooms.RoomType.RestSite,"window-browse-fixture");await Sync(state,"window-browse-fixture");
        await Stage("窗口专项 · 地图按钮、三玩家信息、返回、遗物详情");int pid=Pid;
        var initial=Child().GetProperty("map").GetBoolean();var output=await WindowClick("map");Check(output.GetProperty("map").GetBoolean()!=initial,"top map button toggles map");
        output=await WindowClick("map");Check(output.GetProperty("map").GetBoolean()==initial,"top map button toggles back and remains stable after source presentation updates");
        foreach(var player in state.Players){output=await WindowClick("player",player.NetId);Check(output.GetProperty("player").GetUInt64()==player.NetId&&output.GetProperty("cards").GetInt32()==player.Deck.Cards.Count&&output.GetProperty("relics").GetInt32()==player.Relics.Count&&output.GetProperty("potions").GetInt32()==player.Potions.Count(),"sidebar opens correct original player inventory "+player.NetId);output=await WindowClick("back");Check(output.GetProperty("capstone").ValueKind==JsonValueKind.Null,"player info native back closes "+player.NetId);}
        output=await WindowClick("deck");Check(output.GetProperty("capstone").GetString()=="NDeckViewScreen","deck native screen opens");output=await WindowClick("back");Check(output.GetProperty("capstone").ValueKind==JsonValueKind.Null,"deck native back closes");
        await WindowClick("pause");output=await WindowClick("settings");Check(output.GetProperty("settings").GetBoolean(),"pause opens native settings");output=await WindowClick("settingsBack");Check(!output.GetProperty("settings").GetBoolean(),"settings back returns to pause");output=await WindowClick("resume");Check(output.GetProperty("capstone").ValueKind==JsonValueKind.Null,"pause resume returns to run");
        output=await WindowClick("relic");Check(output.GetProperty("inspectRelic").GetBoolean(),"top relic opens original enlarged inspection");output=await WindowClick("inspectBack");Check(!output.GetProperty("inspectRelic").GetBoolean(),"relic inspection native backstop closes");
        await Sync(state,"window-browsing");Check(Pid==pid&&!Child().GetProperty("control").GetBoolean(),"readonly window browsing leaves original state and resident process intact");
        Write(Role+"-result.json",new{passed=true,checks,visible=true,scope="independent .19 multiplayer window interactions"});await Barrier("window-done");
    }
}
