using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

public static partial class Smoke
{
	private static async Task Seconds(double seconds)
	{
		await Game.ToSignal(Game.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
	}
	private static async Task CheckDocking(RunState state)
	{
		var view = Field("_view")!; var type = view.GetType();
		var panel = (Control)type.GetField("_panel", Instance)!.GetValue(view)!;
		var origin = panel.Position;
		var bounds = Game.GetViewport().GetVisibleRect().Size;
		string before = Fingerprint(state.Players[0]);
		async Task DragTo(Vector2 position)
		{
			float scale = panel.GetNode<Control>("SpectatorTitlebar").Scale.X;
			var title = panel.Position + new Vector2(panel.Size.X - 180 * scale, 20 * scale);
			var end = title + position - panel.Position;
			await Pointer(title); await Pointer(title, true); await Pointer(end); await Pointer(end, false);
		}
		foreach (int edge in new[] { 1, 2, 3, 4 })
		{
			Vector2 target = edge switch { 1 => new Vector2(0, 300), 2 => new Vector2(bounds.X - panel.Size.X, 300), 3 => new Vector2(600, 0), _ => new Vector2(600, bounds.Y - panel.Size.Y) };
			await DragTo(target);
			Check((int)type.GetField("_dockEdge", Instance)!.GetValue(view)! == edge, "pointer drag docks at edge " + edge);
			await Pointer(new Vector2(bounds.X / 2, bounds.Y / 2)); await Seconds(0.95);
			var visible = panel.GetGlobalRect().Intersection(Game.GetViewport().GetVisibleRect());
			Check(Math.Abs((edge <= 2 ? visible.Size.X : visible.Size.Y) - 10) < 1, "edge " + edge + " retracts leaving ten-pixel strip: " + panel.Position + " visible=" + visible.Size + " bounds=" + bounds + " hidden=" + type.GetField("_dockHidden", Instance)!.GetValue(view) + " leave=" + type.GetField("_dockLeaveTime", Instance)!.GetValue(view));
			await SaveFrame("dock-hidden-" + edge, 0);
			await Pointer(visible.GetCenter()); await Seconds(0.35);
			Check(panel.Position.DistanceTo(target) < 1, "edge " + edge + " hover slides out full panel");
			var pin = panel.GetNode<Button>("SpectatorPin"); await Click(pin.GetGlobalRect().GetCenter());
			await Pointer(new Vector2(bounds.X / 2, bounds.Y / 2)); await Seconds(0.95);
			Check(panel.Position.DistanceTo(target) < 1 && pin.ButtonPressed, "edge " + edge + " pin prevents retraction");
			if (edge == 4)
			{
				await Key(Godot.Key.F9); await Key(Godot.Key.F9); await Frames(35);
				view = Field("_view")!; panel = (Control)type.GetField("_panel", Instance)!.GetValue(view)!; pin = panel.GetNode<Button>("SpectatorPin");
				Check((int)type.GetField("_dockEdge", Instance)!.GetValue(view)! == edge && pin.ButtonPressed && panel.Position.DistanceTo(target) < 1, "reopening restores dock direction expanded position and pin state");
			}
			await Click(pin.GetGlobalRect().GetCenter());
			await DragTo(origin); await Frames(10);
			Check((int)type.GetField("_dockEdge", Instance)!.GetValue(view)! == 0, "drag away from edge undocks panel");
		}
		Check(before == Fingerprint(state.Players[0]), "docking and pinning leave player unchanged");
	}
	private static async Task CheckNativeRelicAndLootMap(RunState state, NRewardsScreen loot)
	{
		var relic = state.Players[0].Relics.First();
		var snapshot = CaptureNow(state);
		var owned = ((System.Collections.IEnumerable)Prop(snapshot, "Relics")).Cast<object>().First();
		var r = (float[])Prop(owned, "Rect");
		await Click(new Vector2(r[0] + r[2] / 2, r[1] + r[3] / 2)); await Frames(45);
		var native = NGame.Instance!.InspectRelicScreen!;
		Check(native?.IsVisibleInTree() == true, "real pointer on native game relic opens global inspection");
		snapshot = CaptureNow(state);
		Check(((System.Collections.IEnumerable)Prop(snapshot, "ModalLabels")).Cast<object>().Any(l => ((string)Prop(l, "Text")).Contains(relic.Title.GetFormattedText())), "native game relic inspection is mirrored outside NRun");
		await SaveFrame("native-relic-tracked", 0); native!.Close(); await Frames(30);
		NMapScreen.Instance!.Open(); await Frames(60);
		snapshot = CaptureNow(state);
		Check((string)Prop(snapshot, "Page") == "map", "map over remaining loot overlay is tracked as active page");
		Check(((System.Collections.IEnumerable)Prop(snapshot, "PageArt")).Cast<object>().Any(), "map following loot has native map art");
		await SaveFrame("loot-map-tracked", 0); NMapScreen.Instance.Close(false); await Frames(20);
	}
}
