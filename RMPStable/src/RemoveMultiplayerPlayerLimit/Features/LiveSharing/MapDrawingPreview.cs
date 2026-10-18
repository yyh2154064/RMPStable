using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// The original scene, Line2D, ink/eraser materials, drawing methods and cursor
// provide immediate presentation. This state is deliberately absent from the
// game's drawing collection: only source echoes enter saves and canonical state.
internal sealed class MapDrawingPreview : IDisposable
{
    private readonly NMapDrawings _drawing;
    private readonly Control _surface;
    private readonly object _state;
    private readonly TextureRect _canonicalTexture;
    private readonly bool _wasVisible;
    private readonly HashSet<string> _pending = new();
    private DrawingMode _cursorMode;
    private bool _active;
    internal bool IsActive => _active;
    internal bool Settled => !_active && _pending.Count == 0;
    internal int Pending => _pending.Count;
    internal object State => _state;
    internal ulong LastStrokeFrame { get; private set; }
    internal ulong LastMotionFrame { get; private set; }
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    internal MapDrawingPreview(NMapDrawings drawing, ulong player)
    {
        _drawing = drawing;
        var original = MultiplayerNoncombat.Invoke(drawing, "GetDrawingStateForPlayer", player)!;
        _canonicalTexture = (TextureRect)MultiplayerPersonalUi.Read(original, "drawingTexture")!;
        // A newly created native state hides its texture for three warmup
        // frames. Preserve the actual visibility preference, not that transient.
        _wasVisible = (bool)MultiplayerNoncombat.Invoke(drawing, "ShouldShowMapDrawing", original)!;
        _surface = PreloadManager.Cache.GetScene("res://scenes/screens/map/map_drawing.tscn").Instantiate<Control>();
        _surface.Name = "RmpNativeDrawingPreview";
        _surface.MouseFilter = Control.MouseFilterEnum.Ignore;
        drawing.AddChild(_surface);
        foreach (var control in LocalSpectatorSource.Descendants<Control>(_surface)) control.MouseFilter = Control.MouseFilterEnum.Ignore;
        var viewport = _surface.GetNode<SubViewport>("DrawViewport");
        var texture = _surface.GetNode<TextureRect>("DrawViewportTextureRect");
        viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        texture.Visible = _wasVisible;
        var canonicalViewport = (SubViewport)MultiplayerPersonalUi.Read(original, "drawViewport")!;
        foreach (var line in canonicalViewport.GetChildren().OfType<Line2D>().Where(l => !l.IsQueuedForDeletion()))
            viewport.AddChild(line.Duplicate());
        _state = Activator.CreateInstance(original.GetType(), nonPublic: true)!;
        Set(_state, "playerId", player);
        Set(_state, "drawViewport", viewport);
        Set(_state, "drawingTexture", texture);
        Set(_state, "drawingMode", drawing.GetLocalDrawingMode(false));
        _canonicalTexture.Visible = false;
    }

    internal void Begin(Vector2 local, DrawingMode mode, string origin)
    {
        LastStrokeFrame = Engine.GetProcessFrames();
        _pending.Add(origin);
        MultiplayerNoncombat.Invoke(_drawing, "BeginLine", _state, local, mode);
        _active = true; _cursorMode = mode;
        NGame.Instance.CursorManager._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Tick();
    }
    internal void Move(Vector2 local)
    {
        LastMotionFrame = Engine.GetProcessFrames();
        if (_active) MultiplayerNoncombat.Invoke(_drawing, "UpdateCurrentLinePosition", _state, local);
    }
    internal void End()
    {
        if (_active) MultiplayerNoncombat.Invoke(_drawing, "StopDrawingLine", _state);
        _active = false; _cursorMode = _drawing.GetLocalDrawingMode(false);
        NGame.Instance.CursorManager._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        Tick();
    }
    internal void Echo(MirrorOperation operation)
    {
        if (operation.Kind == "drawEnd") _pending.Remove(operation.DrawingOrigin);
    }
    internal bool Owns(string origin) => _pending.Contains(origin);
    internal void Tick()
    {
        if (GodotObject.IsInstanceValid(_canonicalTexture)) _canonicalTexture.Visible = false;
        var cursor = NGame.Instance.CursorManager;
        if (_cursorMode == DrawingMode.None) cursor.StopOverridingCursor();
        else
        {
            bool erase = _cursorMode == DrawingMode.Erasing;
            cursor.OverrideCursor(PreloadManager.Cache.GetAsset<Image>(erase ? NMapDrawings.erasingCursorTiltedPath : NMapDrawings.drawingCursorTiltedPath),
                PreloadManager.Cache.GetAsset<Image>(erase ? NMapDrawings.erasingCursorPath : NMapDrawings.drawingCursorPath),
                erase ? NMapDrawings.erasingCursorHotspot : NMapDrawings.drawingCursorHotspot);
        }
    }
    public void Dispose()
    {
        if (_active && NGame.Instance != null)
            NGame.Instance.CursorManager._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        if (GodotObject.IsInstanceValid(_canonicalTexture)) _canonicalTexture.Visible = _wasVisible;
        if (GodotObject.IsInstanceValid(_surface)) { _surface.GetParent()?.RemoveChild(_surface); _surface.QueueFree(); }
        if (GodotObject.IsInstanceValid(_drawing) && _drawing.IsInsideTree()) MultiplayerNoncombat.Invoke(_drawing, "UpdateLocalCursor");
    }
}
