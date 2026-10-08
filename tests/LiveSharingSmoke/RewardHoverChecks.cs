using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;

public static partial class Smoke
{
    private static async Task CheckRewardHovers(RunState state)
    {
        var player = state.Players[0];
        string before = Fingerprint(player);
        var map = NMapScreen.Instance;
        bool mapWasOpen = map?.IsOpen == true;
        if (mapWasOpen) map!.Close(false);
        var potion = ModelDb.Potion<SkillPotion>().ToMutable();
        var stolen = state.CreateCard<Cleanse>(player); CardCmd.Upgrade(stolen);
        var returned = new SpecialCardReward(stolen, player);
        returned.SetCustomDescriptionEncounterSource(ModelDb.Encounter<ThievingHopperWeak>().Id);
        var rewards = new RewardsSet(player);
        rewards.Rewards.Add(new PotionReward(potion, player)); rewards.Rewards.Add(returned);
        var screen = NRewardsScreen.ShowScreen(rewards, false, state);
        try
        {
            await Seconds(2); await RefreshControl();
            Check((string)Prop(CurrentSnapshot, "Page") == "loot", "reward hover fixture uses the native loot screen");
            var potionButton = Descendants(screen).OfType<NRewardButton>().Single(b => b.Reward is PotionReward);
            var returnedButton = Descendants(screen).OfType<NRewardButton>().Single(b => b.Reward is SpecialCardReward);
            var preview = (Control)ControlView.GetType().GetField("_preview", Instance)!.GetValue(ControlView)!;
            object[] Hovers() => ((IEnumerable)Prop(CurrentSnapshot, "Hovers")).Cast<object>().ToArray();
            object HoverFor(Control button) => Hovers().Single(h =>
            {
                var r = (float[])Prop(h, "Rect"); var p = button.GetGlobalRect().Position;
                return Math.Abs(r[0] - p.X) < 1 && Math.Abs(r[1] - p.Y) < 1;
            });
            object[] Tips(object hover) => ((IEnumerable)Prop(hover, "Tips")).Cast<object>().ToArray();
            Vector2 Center(Control button) => button.GetGlobalRect().GetCenter() * ((Control)ControlView.GetType().GetField("_page", Instance)!.GetValue(ControlView)!).Scale;
            Check(Tips(HoverFor(potionButton)).Any(t => (string)Prop(t, "Title") == potion.Title.GetFormattedText() && ((string)Prop(t, "Description")).Length > 0), "skill potion reward captures original localized title and effect");
            var capturedCard = Tips(HoverFor(returnedButton)).Select(t => t.GetType().GetProperty("Card")!.GetValue(t)).Single(c => c != null)!;
            Check((bool)Prop(capturedCard, "Upgraded") && (string)Prop(capturedCard, "Title") == stolen.Title && ((string)Prop(capturedCard, "Description")).Length > 0, "returned stolen reward captures exact upgraded card data");
            foreach (bool mode in new[] { false, true })
            {
                await ToggleControl(mode); await RefreshControl();
                // Use the wire representation as the viewer's input, without a Reward or CardModel reference.
                var clone = JsonClone(CurrentSnapshot); ControlView.GetType().GetMethod("Update", Instance)!.Invoke(ControlView, new[] { clone });
                await Pointer(SpectatorPoint(Center(potionButton)));
                Check(Descendants(preview).OfType<MegaLabel>().Any(l => l.Text == potion.Title.GetFormattedText()), "skill potion tooltip rendered by actual viewer pointer, control=" + mode);
                Check(!Descendants(preview).OfType<NCard>().Any(), "potion hover does not retain another reward's card preview");
                var tip = preview.GetChild(0); await Frames(4); await RefreshControl();
                Check(GodotObject.IsInstanceValid(tip) && tip.IsInsideTree() && !tip.IsQueuedForDeletion(), "loot tooltip survives unchanged snapshot refresh, control=" + mode);
                await Pointer(SpectatorPoint(Center(returnedButton)));
                var card = Descendants(preview).OfType<NCard>().Single();
                Check(card.Model == null && card.GetNode<MegaLabel>("%TitleLabel").Text == stolen.Title, "returned upgraded card displays through native read-only NCard, control=" + mode);
                var nativeRect = returnedButton.GetGlobalRect(); var scale = ((Control)ControlView.GetType().GetField("_page", Instance)!.GetValue(ControlView)!).Scale;
                Check(card.GetGlobalTransform().Origin.X - 150 >= nativeRect.End.X * scale.X - 1, "native reward card preview sits to the right of its reward row");
                Check(Descendants(preview).OfType<Control>().All(c => c.MouseFilter == Control.MouseFilterEnum.Ignore), "reward hover visuals do not intercept reward button input");
                if (mode)
                {
                    var buttons = (IDictionary)ControlView.GetType().GetField("_actionButtons", Instance)!.GetValue(ControlView)!;
                    var action = ControlActions.Single(a => ((string)Prop(a, "Id")).EndsWith(":" + returnedButton.GetInstanceId()));
                    Check(buttons.Contains(Prop(action, "Id")) && !((Button)buttons[Prop(action, "Id")]!).Disabled, "native collection command remains available beneath card tooltip");
                    Check(((Button)buttons[Prop(action, "Id")]!).TooltipText.Length == 0, "reward command does not add a duplicate generic tooltip");
                }
                await Pointer(SpectatorPoint(Vector2.One * 20));
                Check(preview.GetChildCount() == 0, "leaving loot reward row removes tooltip, control=" + mode);
            }
            await Pointer(SpectatorPoint(Center(potionButton))); potionButton.Hide(); await RefreshControl();
            Check(Hovers().Length == 1 && !Hovers().SelectMany(Tips).Any(t => (string)Prop(t, "Title") == potion.Title.GetFormattedText()) &&
                !Descendants(preview).OfType<MegaLabel>().Any(l => l.Text == potion.Title.GetFormattedText()),
                "hidden or collected potion reward drops its hover data and visible tooltip");
            Check(Fingerprint(player) == before, "reward hover browsing never changes gold, deck, potions or combat state");
        }
        finally { NOverlayStack.Instance!.Remove(screen); if (mapWasOpen) map!.Open(); await RefreshControl(); }
    }
}
