using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

public static partial class Smoke
{
    private static async Task CheckHoverFixture(MegaCrit.Sts2.Core.Runs.RunState state)
    {
        await ToggleControl(true);
        var player = state.Players[0];
        var reward = new MegaCrit.Sts2.Core.Rewards.CardReward(new MegaCrit.Sts2.Core.Models.CardModel[] {
            state.CreateCard<MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad>(player), state.CreateCard<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>(player), state.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Bash>(player)
        }, MegaCrit.Sts2.Core.Runs.CardCreationSource.Other, player, null!);
        var task = reward.SelectUnsynchronized();
        await Until(() => MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance?.Peek() is MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen, "focused reward fixture opens native choice screen");
        await CheckRewardRetention((MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen)MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance!.Peek()!);
        var root = (Control)ControlView.GetType().GetField("_cards", Instance)!.GetValue(ControlView)!;
        Descendants(root).OfType<Control>().First(c => c.MouseFilter == Control.MouseFilterEnum.Stop).EmitSignal(Control.SignalName.MouseEntered);
        var preview = (Control)ControlView.GetType().GetField("_preview", Instance)!.GetValue(ControlView)!;
        var enlarged = Descendants(preview).OfType<Control>().First(c => c.Name == "SpectatorEnlargedCardHitbox");
        int deckSize = player.Deck.Cards.Count;
        enlarged.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        await Until(() => task.IsCompleted && player.Deck.Cards.Count == deckSize + 1, "enlarged reward card click forwards the same native choice");
        NMapScreen.Instance!.Open(); await Seconds(2); await CheckMapMotionAndBoss(NMapScreen.Instance!);
    }
    private static object JsonClone(object value) => System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(value), value.GetType())!;
    private static void CheckNativeDragArrow()
    {
        var view = ControlView;
        view.GetType().GetField("_dragStart", Instance)!.SetValue(view, new Vector2(800, 1000));
        var action = view.GetType().GetField("_dragAction", Instance)!.GetValue(view)!;
        var ids = ((IEnumerable)Prop(action, "TargetIds")).Cast<string>().ToArray();
        var target = ((IEnumerable)Prop(ControlData, "Targets")).Cast<object>().First(t => ids.Contains((string)Prop(t, "Id")) && (bool)Prop(t, "Enemy"));
        var r = (float[])Prop(target, "Rect");
        var page = (Control)view.GetType().GetField("_page", Instance)!.GetValue(view)!;
        var end = new Vector2(r[0] + r[2] / 2, r[1] + r[3] / 2) * page.Scale;
        view.GetType().GetMethod("UpdateDragArrow", Instance)!.Invoke(view, new object[] { end });
        var arrow = (Node2D)view.GetType().GetField("_dragArrow", Instance)!.GetValue(view)!;
        var sprites = arrow.GetChildren().OfType<Sprite2D>().ToArray();
        Check(sprites.Length == 20 && sprites.All(s => s.Texture != null), "native targeting textures render nineteen joints and one arrowhead");
        Check(arrow.Modulate == StsColors.targetingArrowEnemy, "legal enemy hover uses native red targeting color");
        var a = sprites[0].Position; var b = sprites[18].Position; var m = sprites[9].Position;
        float cross = (b - a).Cross(m - a);
        Check(Math.Abs(cross) > 100, "targeting joints form curved rather than straight trajectory");
        var cursorMode = Input.MouseMode;
        view.GetType().GetMethod("UpdateDragArrow", Instance)!.Invoke(view, new object[] { new Vector2(40, 40) });
        Check(arrow.Modulate == Colors.White && Input.MouseMode == cursorMode, "non-target hover restores native neutral color without changing global cursor mode");
    }
    private static async Task CheckBowlbugSkins()
    {
        var source = Field("_source")!;
        var snapshotType = ControlView.GetType().Assembly.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing.CreatureSnapshot")!;
        foreach (var model in new MonsterModel[] { ModelDb.Monster<BowlbugNectar>(), ModelDb.Monster<BowlbugRock>(), ModelDb.Monster<BowlbugEgg>(), ModelDb.Monster<BowlbugSilk>() })
        {
            var native = model.CreateVisuals(); Game.AddChild(native); native.SetUpSkin(model); await Frames(1);
            try
            {
                string skin = (string)source.GetType().GetMethod("CaptureSkin", Static)!.Invoke(null, new object[] { native })!;
                var snapshot = Activator.CreateInstance(snapshotType)!;
                snapshotType.GetProperty("VisualScene")!.SetValue(snapshot, native.SceneFilePath);
                snapshotType.GetProperty("Skin")!.SetValue(snapshot, skin);
                snapshotType.GetProperty("Animation")!.SetValue(snapshot, "idle_loop");
                snapshotType.GetProperty("BodyMaterial")!.SetValue(snapshot, source.GetType().GetMethod("CaptureBodyMaterial", Instance)!.Invoke(source, new object[] { native }));
                snapshot = JsonClone(snapshot);
                var list = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(snapshotType))!; list.Add(snapshot);
                var holder = new Node2D(); Game.AddChild(holder);
                try
                {
                    ControlView.GetType().GetMethod("DrawCreatureArt", Instance)!.Invoke(ControlView, new object[] { list, holder }); await Frames(2);
                    var copy = Descendants(holder).OfType<NCreatureVisuals>().First();
                    var copied = copy.SpineBody!.GetSkeleton()!.BoundObject.Call("get_skin").AsGodotObject();
                    Check(skin.Length > 0 && copied?.Call("get_name").AsString() == skin, "selected " + model.Id + " skin survives value-only capture and native visual recreation");
                    Check(copy.SpineBody.GetSkeleton()!.GetBounds().Size.Length() > 1, "recreated " + model.Id + " model has visible skeleton attachments");
                }
                finally { holder.QueueFree(); }
            }
            finally { native.QueueFree(); }
        }
    }
    private static async Task CheckMapMotionAndBoss(NMapScreen map)
    {
        await RefreshControl();
        var source = Field("_source")!; var view = ControlView;
        var capture = source.GetType().GetMethod("CaptureMapDrawings", Instance)!;
        var apply = view.GetType().GetMethod("UpdateMapDrawings", Instance)!;
        var theMap = map.GetNode<Control>("TheMap");
        var screenArt = (Control)view.GetType().GetField("_screenArt", Instance)!.GetValue(view)!;
        var layers = (IDictionary)view.GetType().GetField("_retainedArt", Instance)!.GetValue(view)!;
        var index = (IDictionary)Prop(layers[screenArt.GetInstanceId()]!, "Index");
        var node = index[theMap.GetInstanceId().ToString()]!;
        var holder = (Node2D)node.GetType().GetProperty("Holder")!.GetValue(node)!;
        var before = theMap.Position;
        var page = (Control)view.GetType().GetField("_page", Instance)!.GetValue(view)!;
        var drawing = map.Drawings;
        drawing.BeginLineLocal(new Vector2(500, 1500), DrawingMode.Drawing); drawing.UpdateCurrentLinePositionLocal(new Vector2(600, 1550)); drawing.StopLineLocal();
        try
        {
            var initial = theMap.GetGlobalTransform();
            for (int i = 1; i <= 5; i++)
            {
                theMap.Position = before + new Vector2(0, i * 20);
                var frame = JsonClone(capture.Invoke(source, null)!); apply.Invoke(view, new[] { frame });
                Check(holder.GetGlobalTransform().Origin.DistanceTo(theMap.GetGlobalTransform().Origin * page.Scale) < 0.1, "map geometry follows current scroll sample before a full page capture " + i);
                var surfaces = (IEnumerable)view.GetType().GetField("_drawingSurfaces", Instance)!.GetValue(view)!;
                var surface = surfaces.Cast<object>().First(); var ink = (Node2D)surface.GetType().GetField("Item1")!.GetValue(surface)!;
                var inkFrame = ((IEnumerable)Prop(frame, "Drawings")).Cast<object>().First(); var t = (float[])Prop(inkFrame, "Transform");
                Check(ink.GetGlobalTransform().Origin.DistanceTo(new Vector2(t[4], t[5]) * page.Scale) < 0.1, "ink and map share the same current scroll sample " + i);
            }
            view.GetType().GetMethod("Update", Instance)!.Invoke(view, new[] { CurrentSnapshot });
            Check(holder.GetGlobalTransform().Origin.DistanceTo(theMap.GetGlobalTransform().Origin * page.Scale) < 0.1, "older completed map snapshot cannot roll live map motion back");
        }
        finally { theMap.Position = before; apply.Invoke(view, new[] { capture.Invoke(source, null)! }); }
        var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance.DebugOnlyGetState()!;
        var savedBoss = run.Act.BossEncounter;
        NBossMapPoint fixture;
        try
        {
            run.Act.SetBossEncounter(ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.TheInsatiableBoss>());
            fixture = NBossMapPoint.Create(run.Map.BossMapPoint, map, run); Game.AddChild(fixture);
        }
        finally { run.Act.SetBossEncounter(savedBoss); }
        int testedBosses = 0;
        foreach (var boss in Descendants(map).OfType<NBossMapPoint>().Append(fixture))
        {
        var captured = source.GetType().GetMethod("CaptureBackground", Instance)!.Invoke(source, new object?[] { boss, true, null, false })!;
        var target = new Node2D(); Game.AddChild(target);
        try
        {
        var renderedNodes = ((IEnumerable)view.GetType().GetMethod("DrawArt", Instance)!.Invoke(view, new object?[] { target, JsonClone(captured), null, null })!).Cast<object>().ToArray();
        foreach (var spine in Descendants(boss).OfType<CanvasItem>().Where(c => c.GetClass() == "SpineSprite" && c.IsVisibleInTree()))
        {
            var material = new MegaSprite(spine).GetNormalMaterial() as ShaderMaterial;
            if (material == null) continue;
            var art = ((IEnumerable)captured).Cast<object>().FirstOrDefault(a => (string)Prop(a, "Key") == spine.GetInstanceId().ToString());
            if (art == null) continue;
            var rendered = renderedNodes.First(a => (string)Prop(a, "Key") == spine.GetInstanceId().ToString());
            var copy = (CanvasItem)rendered.GetType().GetProperty("Drawing")!.GetValue(rendered)!;
            var copyMaterial = new MegaSprite(copy).GetNormalMaterial() as ShaderMaterial;
            Check(copyMaterial != null && copyMaterial != material, "boss Spine material is a private instance");
            testedBosses++;
            foreach (string uniform in new[] { "map_color", "black_layer_color" })
                Check(copyMaterial!.GetShaderParameter(uniform).AsColor() == material.GetShaderParameter(uniform).AsColor(), "boss preserves native " + uniform + " instead of raw red/blue atlas colors");
        }
        }
        finally { target.QueueFree(); }
        }
        Check(testedBosses > 0, "native boss Spine material fixture is exercised even outside the scroll viewport");
        fixture.QueueFree();
    }
}
