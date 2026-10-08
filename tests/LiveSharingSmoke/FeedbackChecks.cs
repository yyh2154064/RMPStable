using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

public static partial class Smoke
{
	private static void ViewerInput(InputEvent input) => ControlView.GetType().GetMethod("ControlInputEvent", Instance)!.Invoke(ControlView, new object[] { input });
	private static void SubmitStream(string id, string payload)
	{
		ControlView.GetType().GetMethod("SubmitControl", Instance)!.Invoke(ControlView, new object?[] { id, payload, null });
		var result = ControlView.GetType().GetField("_lastControlResult", Instance)!.GetValue(ControlView)!;
		Check((bool)Prop(result, "Accepted"), "continuous input accepted: " + payload);
	}
	private static async Task CheckRewardRetention(NCardRewardSelectionScreen reward)
	{
		await RefreshControl();
		var cardsRoot = (Control)ControlView.GetType().GetField("_cards", Instance)!.GetValue(ControlView)!;
		var hitbox = Descendants(cardsRoot).OfType<Control>().First(c => c.MouseFilter == Control.MouseFilterEnum.Stop);
		hitbox.EmitSignal(Control.SignalName.MouseEntered);
		var enlargedRect = (Rect2)ControlView.GetType().GetField("_hoverCardRect", Instance)!.GetValue(ControlView)!;
		var extended = enlargedRect.Position + new Vector2(8, 8);
		Check(!hitbox.GetGlobalRect().HasPoint(extended), "reward fixture enters enlarged-only hover area");
		await Pointer(SpectatorPoint(extended));
		GD.Print("[LiveSharingSmoke] enlarged hover expected=" + extended + " actual=" + ((SubViewport)ControlView.GetType().GetField("_viewport", Instance)!.GetValue(ControlView)!).GetMousePosition() + " slot=" + hitbox.IsInsideTree() + " page=" + Prop(CurrentSnapshot, "Page"));
		var preview = (Control)ControlView.GetType().GetField("_preview", Instance)!.GetValue(ControlView)!;
		var previewCard = Descendants(preview).OfType<NCard>().First();
		ulong[] ids = Descendants(cardsRoot).OfType<NCard>().Select(c => c.GetInstanceId()).ToArray();
		for (int i = 0; i < 3; i++)
		{
			var native = Descendants(reward).OfType<NCard>().First(); native.Position += new Vector2(0.5f, 0);
			await RefreshControl();
			Check(ids.SequenceEqual(Descendants(cardsRoot).OfType<NCard>().Select(c => c.GetInstanceId())), "reward transform refresh retains card and hover hitbox nodes");
			Check(GodotObject.IsInstanceValid(previewCard) && previewCard.IsInsideTree() && !previewCard.IsQueuedForDeletion(), "hover preview survives reward snapshot refresh");
		}
		hitbox.EmitSignal(Control.SignalName.MouseExited);
		ControlView.GetType().GetMethod("ProcessCardHover", Instance)!.Invoke(ControlView, new object[] { extended });
		Check(GodotObject.IsInstanceValid(previewCard) && !previewCard.IsQueuedForDeletion(), "leaving original reward hitbox inside enlarged card retains preview");
		await Pointer(SpectatorPoint(Vector2.One * 20));
		Check(preview.GetChildCount() == 0, "leaving both reward hover regions dismisses preview");
		var hud = (Control)ControlView.GetType().GetField("_hudHovers", Instance)!.GetValue(ControlView)!;
		var tip = Descendants(hud).OfType<Control>().First(c => c.MouseFilter == Control.MouseFilterEnum.Stop);
		tip.EmitSignal(Control.SignalName.MouseEntered); await Frames(2);
		Check(preview.GetChildCount() > 0, "HUD tooltip survives transfer of preview ownership from a card");
		hitbox.EmitSignal(Control.SignalName.MouseEntered);
		var newCard = Descendants(preview).OfType<NCard>().First();
		tip.EmitSignal(Control.SignalName.MouseExited);
		Check(GodotObject.IsInstanceValid(newCard) && !newCard.IsQueuedForDeletion(), "previous HUD tip exit cannot delete newly opened card preview");
		ControlView.GetType().GetMethod("ProcessCardHover", Instance)!.Invoke(ControlView, new object[] { Vector2.One * 20 });
	}
	private static async Task CheckCombatAnimations()
	{
		await RefreshControl();
		var node = NCombatRoom.Instance!.CreatureNodes.First(c => c.Entity.IsPlayer);
		var actorRoot = (Control)ControlView.GetType().GetField("_actorSprites", Instance)!.GetValue(ControlView)!;
		var art = Descendants(actorRoot).OfType<NCreatureVisuals>().First(); ulong id = art.GetInstanceId();
		var play = ControlActions.First(a => (string)Prop(a, "Kind") == "play" && (bool)Prop(a, "RequiresTarget") && (bool)Prop(a, "Enabled"));
		var cardRoot = (Control)ControlView.GetType().GetField("_cards", Instance)!.GetValue(ControlView)!;
		var slot = Descendants(cardRoot).OfType<Control>().First(c => c.MouseFilter == Control.MouseFilterEnum.Stop);
		ControlView.GetType().GetMethod("HandleControlCard", Instance)!.Invoke(ControlView, new object[] { slot, SnapshotCard((string)Prop(play, "CardId")), new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true } });
		var layer = (Control)ControlView.GetType().GetField("_controlLayer", Instance)!.GetValue(ControlView)!;
		Check(ControlView.GetType().GetField("_dragCard", Instance)!.GetValue(ControlView) != null && !layer.GetChildren().OfType<Panel>().Any(), "targeted card drag creates no visible target collision panels");
		CheckNativeDragArrow();
		ControlView.GetType().GetMethod("CancelControlDrag", Instance)!.Invoke(ControlView, null);
		foreach (string trigger in new[] { "Attack", "Hit", "PowerUp", "Cast" })
		{
			node.SetAnimationTrigger(trigger); await Frames(2);
			Check(node.Visuals.SpineAnimation.GetCurrentAnimationName() != "idle_loop", "native " + trigger + " fixture enters an action animation");
			var source = Field("_source")!;
			var frame = source.GetType().GetMethod("CaptureAnimations", Instance)!.Invoke(source, null)!;
			// Verify the serialized motion channel too; no live node travels to view.
			frame = System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(frame), frame.GetType())!;
			ControlView.GetType().GetMethod("UpdateAnimations", Instance)!.Invoke(ControlView, new[] { frame });
			Check(art.SpineAnimation.GetCurrentAnimationName() == node.Visuals.SpineAnimation.GetCurrentAnimationName(), "spectator follows original " + trigger + " animation");
			await RefreshControl();
			Check(GodotObject.IsInstanceValid(art) && art.GetInstanceId() == id && art.IsInsideTree(), "animation refresh retains native visual instance");
		}
		node.SetAnimationTrigger("Idle"); await Frames(3);
		var source2 = Field("_source")!;
		object Capture() => source2.GetType().GetMethod("CaptureAnimations", Instance)!.Invoke(source2, null)!;
		object Track(object frame) => ((IEnumerable)Prop(((IEnumerable)Prop(frame, "Creatures")).Cast<object>().First(c => (string)Prop(c, "EntityKey") == node.GetInstanceId().ToString()), "Tracks")).Cast<object>().First();
		string key = (string)Prop(Track(Capture()), "Key"); await Frames(3);
		Check(key == (string)Prop(Track(Capture()), "Key"), "idle sampling keeps animation generation stable rather than restarting every frame");
		node.SetAnimationTrigger("Attack"); await Frames(2); string attackKey = (string)Prop(Track(Capture()), "Key");
		node.SetAnimationTrigger("Attack"); await Frames(2);
		Check(attackKey != (string)Prop(Track(Capture()), "Key"), "repeated identical attack receives a new animation generation");
		node.Visuals.SpineAnimation.SetAnimation("cast", true, 1); await Frames(2);
		ControlView.GetType().GetMethod("UpdateAnimations", Instance)!.Invoke(ControlView, new[] { Capture() });
		Check(art.SpineAnimation.GetCurrentAnimationName(1) == "cast", "secondary ability animation track is mirrored");
		node.Visuals.SpineAnimation.GetAnimationState()!.BoundObject.Call("clear_track", 1); await Frames(2);
		ControlView.GetType().GetMethod("UpdateAnimations", Instance)!.Invoke(ControlView, new[] { Capture() });
		Check(art.SpineAnimation.GetCurrentTrack(1) == null, "ended secondary ability track clears from spectator");
		var enemy = NCombatRoom.Instance.CreatureNodes.First(c => c.Entity.IsEnemy && c.HasSpineAnimation);
		enemy.SetAnimationTrigger("Hit"); await Frames(2);
		ControlView.GetType().GetMethod("UpdateAnimations", Instance)!.Invoke(ControlView, new[] { Capture() });
		var enemyArt = Descendants(actorRoot).OfType<NCreatureVisuals>().First(v => v.SceneFilePath == enemy.Visuals.SceneFilePath);
		Check(enemyArt.SpineAnimation.GetCurrentAnimationName() == enemy.Visuals.SpineAnimation.GetCurrentAnimationName(), "enemy hurt animation follows original native track");
	}
	private static async Task CheckFeedbackMapAndEvents(RunState state)
	{
		await ToggleControl(true); await RefreshControl();
		var view = ControlView; var panel = (Control)view.GetType().GetField("_panel", Instance)!.GetValue(view)!;
		view.GetType().GetMethod("LocalControlInputEvent", Instance)!.Invoke(view, new object[] { new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Vector2.One } });
		Check((bool)view.GetType().GetField("_controlEnabled", Instance)!.GetValue(view)!, "local game click preserves preferred control mode");
		long epoch = (long)view.GetType().GetField("_controlEpoch", Instance)!.GetValue(view)!;
		await Key(Godot.Key.F8); await Key(Godot.Key.F8); await RefreshControl();
		Check((bool)ControlView.GetType().GetField("_controlEnabled", Instance)!.GetValue(ControlView)! && epoch != (long)ControlView.GetType().GetField("_controlEpoch", Instance)!.GetValue(ControlView)!, "reopening restores selected mode with a new permission epoch");
		var prefs = ControlView.GetType().Assembly.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing.SpectatorPreferences")!;
		string path = (string)prefs.GetField("_path", Static)!.GetValue(null)!;
		Check(System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path)).RootElement.GetProperty("ControlMode").GetBoolean(), "control selection is saved in isolated profile preferences");
		long recoveryEpoch = (long)ControlView.GetType().GetField("_controlEpoch", Instance)!.GetValue(ControlView)!;
		ControlView.GetType().GetMethod("ShowError", Instance)!.Invoke(ControlView, new object[] { "temporary capture fixture" });
		Check(!(bool)ControlView.GetType().GetField("_controlEnabled", Instance)!.GetValue(ControlView)!, "transient capture failure suspends command permission");
		await RefreshControl();
		Check((bool)ControlView.GetType().GetField("_controlEnabled", Instance)!.GetValue(ControlView)! && recoveryEpoch != (long)ControlView.GetType().GetField("_controlEpoch", Instance)!.GetValue(ControlView)!, "valid snapshot restores remembered mode with a fresh epoch after transient failure");
		var map = NMapScreen.Instance!;
		await CheckMapMotionAndBoss(map);
		_controller.GetMethod("RouteMapInput", Static)!.Invoke(null, new object[] { true });
		var before = map.Get("_targetDragPos").AsVector2();
		string scroll = (string)Prop(ControlActions.First(a => ((string)Prop(a, "Id")).StartsWith("scroll-map:")), "Id");
		for (int i = 0; i < 10; i++) SubmitStream(scroll, "up");
		Check(Math.Abs(map.Get("_targetDragPos").AsVector2().Y - before.Y - 400) < 0.1, "ten consecutive wheel ticks preserve native 400-pixel map distance");
		SubmitStream(scroll, "down:2.5");
		Check(Math.Abs(map.Get("_targetDragPos").AsVector2().Y - before.Y - 300) < 0.1, "fractional wheel factor preserved at source scale");
		await Seconds(0.6); await RefreshControl();
		var page = (Control)ControlView.GetType().GetField("_page", Instance)!.GetValue(ControlView)!;
		Vector2 p = new(700, 420), q = new(760, 460);
		Vector2 local = map.Drawings.GetGlobalTransform().AffineInverse() * p;
		ViewerInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = p * page.Scale });
		Check(map.Drawings.IsLocalDrawing(), "right button begins native map stroke through spectator input");
		ViewerInput(new InputEventMouseMotion { Position = q * page.Scale });
		ViewerInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = q * page.Scale });
		Check(!map.Drawings.IsLocalDrawing(), "right release completes native map stroke");
		var line = Descendants(map.Drawings).OfType<Line2D>().Last(l => l.Points.Length > 0);
		Check(line.Points[0].DistanceTo(local * 0.5f) < 0.1, "stroke starts at translated spectator point in native half-size ink viewport, independent of desktop pointer");
		var draw = Descendants(map).OfType<NMapDrawButton>().First(); await ClickNative(draw);
		Check(map.Drawings.GetLocalDrawingMode(false) == DrawingMode.Drawing, "native pencil button enables persistent drawing mode");
		ViewerInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p * page.Scale });
		ViewerInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = q * page.Scale });
		Check(!map.Drawings.IsLocalDrawing(), "pencil mode left stroke completes");
		var erase = Descendants(map).OfType<NMapEraseButton>().First(); await ClickNative(erase);
		Check(map.Drawings.GetLocalDrawingMode(false) == DrawingMode.Erasing, "native eraser button changes map tool");
		await ClickNative(Descendants(map).OfType<NMapClearButton>().First());
		Check(!Descendants(map.Drawings).OfType<Line2D>().Any(l => !l.IsQueuedForDeletion() && l.Points.Length > 0), "native clear button removes map strokes");
		ViewerInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = p * page.Scale });
		Check(map.Drawings.GetLocalDrawingMode(false) == DrawingMode.None, "right button exits persistent tool mode like original game");
		map.SetDebugTravelEnabled(true); await RefreshControl();
		var question = Descendants(map).OfType<NMapPoint>().First(n => n.Point.PointType == MapPointType.Unknown && n.IsVisibleInTree() && n.GetGlobalRect().Intersects(map.GetViewportRect()));
		var previous = state.MapLocation;
		bool entered = false; void OnEntered() => entered = true;
		RunManager.Instance.RoomEntered += OnEntered;
		try { await ClickNative(question); await Until(() => entered && !map.IsOpen && state.MapLocation != previous, "question-mark map point completes travel using original handler", 20); }
		finally { RunManager.Instance.RoomEntered -= OnEntered; }
		await Seconds(1);
		await RefreshControl();
		Check((bool)ControlView.GetType().GetField("_controlEnabled", Instance)!.GetValue(ControlView)!, "unknown-room tooltip transition leaves preferred control available");
		if (MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance?.Peek() is { } overlay) MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance.Remove(overlay);
		var canonical = ModelDb.Event<ThisOrThat>(); state.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, canonical.Id);
		await RunManager.Instance.EnterRoom(new EventRoom(canonical)); await Seconds(1.5); await RefreshControl();
		Check(Descendants(NEventRoom.Instance!).OfType<NEventOptionButton>().Any(b => b.Event is ThisOrThat), "normal event fixture is loaded before choosing its option");
		int gold = state.Players[0].Gold, hp = state.Players[0].Creature.CurrentHp;
		await ClickNative(Descendants(NEventRoom.Instance!).OfType<NEventOptionButton>().First(b => !b.Option.IsProceed && !b.Option.IsLocked));
		await Until(() => state.Players[0].Gold > gold && state.Players[0].Creature.CurrentHp < hp, "normal question event option executes original HP and gold decision");
		await ClickNative(Descendants(NEventRoom.Instance!).OfType<NEventOptionButton>().First(b => b.Option.IsProceed));
		await Until(() => map.IsOpen, "normal event proceed returns to native map"); await ToggleControl(false);
	}
}
