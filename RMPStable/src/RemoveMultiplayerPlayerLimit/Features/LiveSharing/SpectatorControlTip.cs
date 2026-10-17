using System;
using System.Linq;
using Godot;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView
{
    private Window? _controlTip;
    private Label? _controlTipText;
    private ulong _controlHoverAt;
    internal string ControlTipText => T("此操作将控制", "This will control ") +
        (_sourceOptions.FirstOrDefault(p => p.Id == _selectedSourceId)?.Name ?? T("队友", "your teammate")) + T("的游戏！", "'s game!");
    private void ProcessControlTip()
    {
        if (!_mirror.Multiplayer || !_overlay.Visible || !_controlToggle.GetGlobalRect().HasPoint(ShellPointer()))
        { _controlHoverAt = 0; _controlTip?.Hide(); return; }
        if (_controlHoverAt == 0) _controlHoverAt = Time.GetTicksMsec();
        if (Time.GetTicksMsec() - _controlHoverAt < 350) return;
        ShowControlTip();
    }
    private void ShowControlTip()
    {
        if (_controlTip == null)
        {
            // PopupPanel grabs popup input even when Unfocusable: the click on
            // the mode button only dismisses the tip. A passive native window
            // stays above the replica without consuming owner mouse input.
            _controlTip = new Window { Name = "SpectatorControlTip", ForceNative = true, Unfocusable = true, Transient = true,
                Borderless = true, MousePassthrough = true, PopupWindow = false, Visible = false };
            _overlay.AddChild(_controlTip);
            var background = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            background.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("182126"), BorderColor = new Color("b7c7ce"),
                BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 });
            _controlTip.AddChild(background);
            _controlTipText = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(12, 8) };
            _controlTipText.AddThemeFontSizeOverride("font_size", 18);
            _controlTip.AddChild(_controlTipText);
        }
        _controlTipText!.Text = ControlTipText;
        if (_controlTip.Visible) return;
        var viewport = _controlToggle.GetViewport();
        var transform = viewport.GetFinalTransform() * _controlToggle.GetGlobalTransformWithCanvas();
        var point = transform * new Vector2(0, _controlToggle.Size.Y + 6);
        var screen = new MirrorWin32.Point { X = (int)point.X, Y = (int)point.Y };
        if (DisplayServer.GetName() != "headless") MirrorWin32.ClientToScreen(MirrorWin32.OwnWindow, ref screen);
        _controlTip.ContentScaleFactor = Math.Max(.5f, viewport.GetFinalTransform().Scale.X);
        var tipSize = _controlTipText.GetCombinedMinimumSize() * _controlTip.ContentScaleFactor + new Vector2(24, 16);
        _controlTip.Position = new Vector2I(screen.X, screen.Y);
        _controlTip.Size = new Vector2I((int)tipSize.X, (int)tipSize.Y);
        _controlTip.Show();
        // An engine canvas layer cannot cover a separate OS game window.
        // A native, nonactivating popup is ordered above that owned window.
        if (DisplayServer.GetName() != "headless")
            MirrorWin32.SetWindowPos((nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, _controlTip.GetWindowId()), 0, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002);
    }
}
