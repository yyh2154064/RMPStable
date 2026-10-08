using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Godot;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// HWNDs are authenticated by process ownership. No pixels cross the pipe.
// Subclass only windows owned by this process, never the other game's HWND.
internal static class MirrorWin32
{
    internal const int Style = -16, ExStyle = -20, WndProc = -4;
    internal const long Child = 0x40000000, Popup = 0x80000000, ClipChildren = 0x02000000, ClipSiblings = 0x04000000;
    internal const long Decorations = 0x00CF0000, AppWindow = 0x00040000;
    internal const uint Move = 0x200, LeftDown = 0x201, LeftUp = 0x202, CaptureChanged = 0x215;
    internal const uint KeyDown = 0x100, KeyUp = 0x101, Char = 0x102, SysKeyDown = 0x104, SysKeyUp = 0x105;
    internal const uint MouseActivate = 0x21;
    [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate nint Procedure(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint window);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint window, nint parent);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern nint SetLong(nint window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] internal static extern nint Call(nint previous, nint window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint Create(uint extended, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint window, ref Point point);
    [DllImport("user32.dll")] internal static extern nint SetCapture(nint window);
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern nint GetCapture();
    [DllImport("user32.dll")] internal static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll")] internal static extern int SetWindowRgn(nint window, nint region, bool redraw);
    [DllImport("gdi32.dll")] internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] internal static extern int CombineRgn(nint destination, nint a, nint b, int mode);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint value);
    internal static nint OwnWindow => DisplayServer.GetName() == "headless" ? 0 : (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
    internal static bool Owned(nint window, int process) => window != 0 && IsWindow(window) && GetWindowThreadProcessId(window, out uint owner) != 0 && owner == process;
    internal static void RequireOwned(nint window, int process)
    { if (!Owned(window, process)) throw new InvalidOperationException("Native mirror window owner mismatch"); }
    internal static bool Keyboard(uint message) => message is KeyDown or KeyUp or Char or SysKeyDown or SysKeyUp or 0x103 or 0x106 or 0x107 or 0x10D or 0x10E or 0x10F;
    internal static bool Button(uint message) => message >= LeftDown && message <= 0x20E;
    internal static Vector2 Pointer(nint window)
    {
        if (!GetCursorPos(out var point) || !ScreenToClient(window, ref point) || !GetClientRect(window, out var rect)) return new Vector2(-1,-1);
        return new Vector2(point.X / (float)Math.Max(1, rect.Right), point.Y / (float)Math.Max(1, rect.Bottom));
    }
    internal static void AttachOwn(nint parent, int owner)
    {
        var window = OwnWindow; RequireOwned(window, System.Environment.ProcessId); RequireOwned(parent, owner);
        long style = (long)GetLong(window, Style);
        SetLong(window, Style, (nint)((style & ~(Popup | Decorations)) | Child | ClipSiblings));
        SetLong(window, ExStyle, (nint)((long)GetLong(window, ExStyle) & ~AppWindow));
        Marshal.SetLastPInvokeError(0);
        if (SetParent(window, parent) == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (GetParent(window) != parent) throw new InvalidOperationException("Native mirror parent was not applied");
        GetClientRect(parent, out var size);
        if (!SetWindowPos(window, 0, 0, 0, Math.Max(1,size.Right), Math.Max(1,size.Bottom), 0x0010 | 0x0020 | 0x0040))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}

internal sealed class MirrorWindowHook : IDisposable
{
    private readonly nint _window, _previous;
    private readonly MirrorWin32.Procedure _procedure;
    private readonly Func<uint, nuint, nint, bool> _intercept;
    private readonly bool _noActivate;
    private bool _disposed;
    internal MirrorWindowHook(nint window, Func<uint, nuint, nint, bool> intercept, bool noActivate = false)
    {
        MirrorWin32.RequireOwned(window, System.Environment.ProcessId);
        _window = window; _intercept = intercept; _procedure = Dispatch; _noActivate = noActivate;
        Marshal.SetLastPInvokeError(0);
        _previous = MirrorWin32.SetLong(window, MirrorWin32.WndProc, Marshal.GetFunctionPointerForDelegate(_procedure));
        if (_previous == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    private nint Dispatch(nint window, uint message, nuint wparam, nint lparam)
    {
        // No exception may unwind into user32. Input fails closed.
        try { if (!_disposed && _intercept(message, wparam, lparam)) return 0; }
        catch { if (MirrorWin32.Button(message) || MirrorWin32.Keyboard(message)) return 0; }
        if (_noActivate && message == MirrorWin32.MouseActivate) return 3;
        return MirrorWin32.Call(_previous, window, message, wparam, lparam);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (MirrorWin32.IsWindow(_window)) MirrorWin32.SetLong(_window, MirrorWin32.WndProc, _previous);
        GC.KeepAlive(_procedure);
    }
}

internal sealed class MirrorWindowHost : IDisposable
{
    internal nint Container { get; }
    private readonly nint _parent, _child;
    private readonly int _childProcess;
    private readonly MirrorWindowHook _keys;
    private readonly bool _hadClipChildren;
    private (int X, int Y, int Width, int Height) _last;
    private bool _visible, _escape, _disposed;
    private readonly System.Collections.Generic.HashSet<nuint> _forwardKeys = new();
    internal bool Attached => MirrorWin32.Owned(_child, _childProcess) && MirrorWin32.GetParent(_child) == Container;
    internal bool Visible => Attached && MirrorWin32.IsWindowVisible(Container) && MirrorWin32.IsWindowVisible(_child);
    internal MirrorWindowHost(nint child, int childProcess)
    {
        _parent = MirrorWin32.OwnWindow; _child = child; _childProcess = childProcess;
        MirrorWin32.RequireOwned(_parent, System.Environment.ProcessId); MirrorWin32.RequireOwned(child, childProcess);
        long style = (long)MirrorWin32.GetLong(_parent, MirrorWin32.Style);
        _hadClipChildren = (style & MirrorWin32.ClipChildren) != 0;
        MirrorWin32.SetLong(_parent, MirrorWin32.Style, (nint)(style | MirrorWin32.ClipChildren));
        Container = MirrorWin32.Create(0, "STATIC", "RMP native mirror", (uint)(MirrorWin32.Child | MirrorWin32.ClipChildren | MirrorWin32.ClipSiblings), 0,0,1,1,_parent,0,0,0);
        if (Container == 0) { int error = Marshal.GetLastPInvokeError(); RestoreStyle(); throw new Win32Exception(error); }
        var keys = new byte[256];
        if (MirrorWin32.GetKeyboardState(keys)) for (uint key = 0; key < keys.Length; key++) if ((keys[key] & 0x80) != 0) _forwardKeys.Add(key);
        try { _keys = new MirrorWindowHook(_parent, InterceptSourceKey); }
        catch { MirrorWin32.DestroyWindow(Container); RestoreStyle(); throw; }
    }
    internal bool PointerInside
    {
        get
        {
            var source = MirrorWin32.Pointer(_parent); var child = MirrorWin32.Pointer(_child);
            return _visible && Attached && source.X >= 0 && source.X < 1 && source.Y >= 0 && source.Y < 1 && child.X >= 0 && child.X < 1 && child.Y >= 0 && child.Y < 1;
        }
    }
    private bool InterceptSourceKey(uint message, nuint key, nint _)
    {
        if (!MirrorWin32.Keyboard(message)) return false;
        // A key held before crossing into the child must still be released in
        // the source, otherwise blocking key-up leaves a stuck Godot key state.
        if (message is MirrorWin32.KeyUp or MirrorWin32.SysKeyUp && _forwardKeys.Remove(key)) return false;
        if (!PointerInside)
        {
            if (message is MirrorWin32.KeyDown or MirrorWin32.SysKeyDown) _forwardKeys.Add(key);
            return false;
        }
        // Keep the configured spectator hotkey handled by the source; all
        // gameplay keys over the replica are blocked before Godot sees them.
        if ((uint)key == LiveSharingController.NativeHotkey) { _forwardKeys.Add(key); return false; }
        if (message == MirrorWin32.KeyDown && key == 0x1B) _escape = true;
        return true;
    }
    internal bool TakeEscape() { bool value = _escape; _escape = false; return value; }
    internal void Layout(Control content, bool show)
    {
        if (!MirrorWin32.IsWindow(Container)) throw new InvalidOperationException("Native mirror container disappeared");
        var rect = content.GetGlobalRect();
        // Canvas coordinates -> OS screen -> source client, including DPI,
        // letterboxing, window position and CanvasLayer transforms.
        var screen = content.GetViewport().GetScreenTransform();
        var a = screen * rect.Position; var b = screen * rect.End;
        var origin = new MirrorWin32.Point { X = (int)Math.Round(a.X), Y = (int)Math.Round(a.Y) };
        MirrorWin32.ScreenToClient(_parent, ref origin);
        var bounds = (origin.X, origin.Y, Math.Max(1,(int)Math.Round(b.X-a.X)), Math.Max(1,(int)Math.Round(b.Y-a.Y)));
        if (_last != bounds)
        {
            _last = bounds;
            if (!MirrorWin32.SetWindowPos(Container, 0, bounds.Item1,bounds.Item2,bounds.Item3,bounds.Item4, 0x0010)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (Attached) MirrorWin32.SetWindowPos(_child,0,0,0,bounds.Item3,bounds.Item4,0x0010 | 0x0004 | 0x4000);
            var region = MirrorWin32.CreateRectRgn(0,0,bounds.Item3,bounds.Item4);
            int cornerWidth = Math.Max(1,(int)Math.Ceiling(16 * bounds.Item3 / content.Size.X));
            int cornerHeight = Math.Max(1,(int)Math.Ceiling(13 * bounds.Item4 / content.Size.Y));
            var corner = MirrorWin32.CreateRectRgn(Math.Max(0,bounds.Item3-cornerWidth),Math.Max(0,bounds.Item4-cornerHeight),bounds.Item3,bounds.Item4);
            MirrorWin32.CombineRgn(region,region,corner,4); MirrorWin32.DeleteObject(corner);
            if (MirrorWin32.SetWindowRgn(Container,region,true) == 0) { MirrorWin32.DeleteObject(region); throw new InvalidOperationException("Native mirror clipping failed"); }
        }
        show &= content.IsVisibleInTree() && !MirrorWin32.IsIconic(_parent);
        if (_visible != show)
        {
            _visible = show; MirrorWin32.ShowWindow(Container,show ? 8 : 0);
            GD.Print("[RMP:Mirror:Window] container=" + (show ? "visible" : "hidden") + " attached=" + Attached + " bounds=" + _last);
        }
    }
    private void RestoreStyle()
    {
        if (!_hadClipChildren && MirrorWin32.IsWindow(_parent))
            MirrorWin32.SetLong(_parent, MirrorWin32.Style, (nint)((long)MirrorWin32.GetLong(_parent, MirrorWin32.Style) & ~MirrorWin32.ClipChildren));
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _keys.Dispose();
        // Hide before destroying the clipping host. Only the owning MirrorProcess
        // may terminate the child; never detach it into a visible desktop window.
        MirrorWin32.ShowWindow(Container,0); MirrorWin32.DestroyWindow(Container); RestoreStyle();
    }
}
