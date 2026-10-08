using System;
using System.Collections.Generic;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;
internal sealed partial class SpectatorView {
	private sealed class ControlInput : Node
	{
		private static readonly StringName InputMethod = new("_Input");
		internal Action<InputEvent>? Receive;
		public override void _Input(InputEvent input) => Receive?.Invoke(input);
		internal static List<Godot.Bridge.MethodInfo> GetGodotMethodList() => new()
		{
			new(InputMethod, new PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal,
				new List<PropertyInfo> { new(Variant.Type.Object, "input", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false) }, null)
		};
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == InputMethod && args.Count == 1) { _Input(VariantUtils.ConvertTo<InputEvent>(in args[0])); ret = default; return true; }
			return base.InvokeGodotClassMethod(in method, args, out ret);
		}
		protected override bool HasGodotClassMethod(in godot_string_name method) => method == InputMethod || base.HasGodotClassMethod(in method);
	}

    private void ReceiveLocal(InputEvent input)
    {
        if (input is InputEventMouse mouse)
        {
            bool inside = _panel.GetGlobalRect().HasPoint(mouse.Position);
            LiveSharingController.RouteMapInput(inside);
            if (!inside)
            {
                _mirror.Presentation(new MirrorMessage { Kind = "pointer", X = -1, Y = -1 });
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left }) CancelDrag();
            }
        }
        if (input is InputEventKey { Keycode: Key.Escape, Pressed: true, Echo: false } && _controlEnabled && _panel.GetGlobalRect().HasPoint(_panel.GetViewport().GetMousePosition()))
        {
            if (_dragAction != null) CancelDrag(); else SetPreferredControl(false);
            _overlay.GetViewport().SetInputAsHandled();
        }
    }
}
