// Virtual Display Viewer 1.0                                                                             // program name and version
// Shows a monitor (by default the Virtual Display Driver monitor) in a movable,                          // what the program does
// always-on-top "picture in picture" window on a real screen.                                            // (continued)
// Copyright (c) 2026 P. H. Ewen (KO4WSC). MIT License - see LICENSE.                                     // copyright and license
using System.Runtime.InteropServices;                                                                      // DllImport, StructLayout and Marshal for Windows API calls

namespace VirtualDisplayViewer;                                                                            // every class in this program lives here

internal static class Program                                                                              // startup class
{                                                                                                          // start of Program
    [STAThread]                                                                                            // WinForms needs a single-threaded apartment
    private static void Main()                                                                             // the program starts here
    {                                                                                                      // start of Main
        ApplicationConfiguration.Initialize();                                                             // DPI awareness and visual styles from the .csproj
        Application.Run(new ViewerForm());                                                                 // show the viewer window until it is closed
    }                                                                                                      // end of Main
}                                                                                                          // end of Program

/// <summary>Windows API declarations the viewer needs.</summary>                                         // doc comment
internal static class Native                                                                               // thin wrappers over user32/gdi32
{                                                                                                          // start of Native
    [StructLayout(LayoutKind.Sequential)]                                                                  // same memory layout as the Windows POINT struct
    public struct POINT { public int X; public int Y; }                                                    // a screen position in pixels

    [StructLayout(LayoutKind.Sequential)]                                                                  // same layout as Windows CURSORINFO
    public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; } // size, showing flag, cursor handle, position

    [StructLayout(LayoutKind.Sequential)]                                                                  // same layout as Windows ICONINFO
    public struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; } // icon/cursor flag, hotspot, mask and colour bitmaps

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]                                       // same layout as Windows DISPLAY_DEVICEW
    public struct DISPLAY_DEVICE                                                                           // describes one display adapter output
    {                                                                                                      // start of DISPLAY_DEVICE
        public int cb;                                                                                     // size of this struct (must be set before the call)
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;                     // e.g. \\.\DISPLAY3
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;                  // adapter name, e.g. "Virtual Display Driver"
        public int StateFlags;                                                                             // attached-to-desktop, primary, etc.
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;                      // hardware id (unused)
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;                     // registry key (unused)
    }                                                                                                      // end of DISPLAY_DEVICE

    public const int CURSOR_SHOWING = 1;                                                                   // CURSORINFO flag: the cursor is visible
    public const int DI_NORMAL = 3;                                                                        // DrawIconEx: draw image and mask normally
    public const int WM_HOTKEY = 0x0312;                                                                   // message sent when a registered hot key is pressed
    public const int MOD_ALT = 1, MOD_CONTROL = 2;                                                         // hot-key modifier flags
    public const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 1;                                               // the output is part of the desktop

    [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO pci);                 // where the mouse cursor is and what it looks like
    [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);       // the cursor's hotspot (the "tip")
    [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, int step, IntPtr brush, int flags); // draws the cursor into our picture
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);                           // frees the bitmaps GetIconInfo hands back
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);                        // moves the mouse
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, int mods, int vk); // claims Ctrl+Alt+V system-wide
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);             // releases the hot key
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevicesW(string? device, int index, ref DISPLAY_DEVICE dd, int flags); // lists display outputs

    /// <summary>Returns display device names (\\.\DISPLAYn) mapped to their adapter description.</summary> // doc comment
    public static Dictionary<string, string> GetAdapterNames()                                             // used to find the virtual monitor by its adapter name
    {                                                                                                      // start of GetAdapterNames
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);                        // device name -> adapter name
        for (int i = 0; ; i++)                                                                             // walk outputs until Windows says there are no more
        {                                                                                                  // start of loop body
            var dd = new DISPLAY_DEVICE();                                                                 // empty record for this output
            dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();                                                      // Windows requires the size to be filled in
            if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;                                           // no output number i: done
            if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0) map[dd.DeviceName] = dd.DeviceString; // keep only outputs that are part of the desktop
        }                                                                                                  // end of loop body
        return map;                                                                                        // hand back the list
    }                                                                                                      // end of GetAdapterNames
}                                                                                                          // end of Native
