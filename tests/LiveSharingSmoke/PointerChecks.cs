using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Runs;

public static partial class Smoke
{
	private static async Task CheckSourcePointer(RunState state)
	{
		var view = Field("_view")!; var type = view.GetType();
		var pointer = (Node2D)type.GetField("_remotePointer", Instance)!.GetValue(view)!;
		string before = Fingerprint(state.Players[0]);
		await Pointer(new Vector2(50, 400)); await Frames(15);
		var dataNow = type.GetField("_pointerState", Instance)!.GetValue(view)!;
		GD.Print("[LiveSharingSmoke] native pointer texture=" + Prop(dataNow, "Texture"));
		Check(pointer.Visible && pointer.Position.DistanceTo(new Vector2(50, 400)) < 2, "source pointer appears at normalized game coordinates");
		var nativeImage = (Image)Game.CursorManager.GetType().GetField("_lastSetCursor", Instance)!.GetValue(Game.CursorManager)!;
		var renderedImage = pointer.GetNode<TextureRect>("NativeCursor").Texture.GetImage();
		Check((string)Prop(dataNow, "Texture") == nativeImage.ResourcePath && renderedImage.GetWidth() == nativeImage.GetWidth() && renderedImage.GetHeight() == nativeImage.GetHeight(), "spectator cursor reuses exact native game image and dimensions");
		await Pointer(new Vector2(50, 400), true); await Seconds(0.1);
		var pressedImage = (Image)Game.CursorManager.GetType().GetField("_lastSetCursor", Instance)!.GetValue(Game.CursorManager)!;
		Check((string)Prop(type.GetField("_pointerState", Instance)!.GetValue(view)!, "Texture") == pressedImage.ResourcePath && pressedImage.ResourcePath != nativeImage.ResourcePath, "source mouse press follows native tilted cursor image");
		await Pointer(new Vector2(50, 400), false); await Seconds(0.1);
		ulong id = pointer.GetInstanceId();
		await Pointer(new Vector2(90, 450)); await Frames(15);
		Check(pointer.GetInstanceId() == id && pointer.Position.DistanceTo(new Vector2(90, 450)) < 2, "source pointer movement retains one graphic node and converges smoothly");
		await SaveFrame("source-pointer", 0, true);
		var data = type.GetField("_pointerState", Instance)!.GetValue(view)!;
		var stale = Activator.CreateInstance(data.GetType())!;
		foreach (var property in data.GetType().GetProperties()) property.SetValue(stale, property.GetValue(data));
		data.GetType().GetProperty("Sequence")!.SetValue(stale, (long)Prop(data, "Sequence") - 1);
		data.GetType().GetProperty("X")!.SetValue(stale, 0.9f);
		type.GetMethod("UpdatePointer", Instance)!.Invoke(view, new[] { stale });
		Check(ReferenceEquals(data, type.GetField("_pointerState", Instance)!.GetValue(view)), "older pointer sequence cannot replace newer location");
		var panel = (Control)type.GetField("_panel", Instance)!.GetValue(view)!;
		await Pointer(panel.GetGlobalRect().GetCenter()); await Frames(10);
		Check(!pointer.Visible, "spectator's own browsing pointer is excluded from source tracking");
		await Pointer(new Vector2(50, 400)); await Frames(10);
		Check(pointer.Visible && before == Fingerprint(state.Players[0]), "source pointer returns outside panel without changing live game");
	}
}
