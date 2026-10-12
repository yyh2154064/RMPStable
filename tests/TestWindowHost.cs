using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Invisible containers for newly launched, isolated test games only. The
// original offscreen safeguard cannot expose a game embedded with --wid.
public static class RmpTestWindowHost
{
    static readonly HashSet<IntPtr> owned = new HashSet<IntPtr>();
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr Create(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
    public static IntPtr CreateHost()
    {
        var window=Create(0x08000080,"STATIC","RMP isolated rendering host",0x80000000,-30000,-30000,1280,720,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(window==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
        owned.Add(window);return window;
    }
    public static void Release(IntPtr window){if(owned.Remove(window))DestroyWindow(window);}
}
