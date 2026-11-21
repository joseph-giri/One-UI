using System.Runtime.InteropServices;

namespace OneUI.App.Native;

/// <summary>
/// Pins Live Wallpaper and Desktop Widgets to the Windows 11 Desktop layer per-monitor:
/// - Desktop (Progman/WorkerW) -> owns LiveWallpaperWindow (at HWND_BOTTOM) -> owns DesktopWidgetWindow.
/// </summary>
public static class DesktopLayerHelper
{
    public const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int GWLP_HWNDPARENT = -8;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const uint SWP_NOMOVE = 0x0002;
    public static readonly nint HWND_BOTTOM = new(1);

    private static readonly Dictionary<string, nint> WallpapersByMonitor = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public nint hwnd;
        public nint hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    private static nint SetOwner(nint hwnd, nint ownerHwnd)
    {
        if (nint.Size == 8)
            return SetWindowLongPtr64(hwnd, GWLP_HWNDPARENT, ownerHwnd);
        return SetWindowLong32(hwnd, GWLP_HWNDPARENT, ownerHwnd.ToInt32());
    }

    public static nint GetDesktopOwnerWindow()
    {
        nint shellHost = nint.Zero;
        Win32Api.EnumWindows((topHandle, lParam) =>
        {
            if (Win32Api.FindWindowEx(topHandle, nint.Zero, "SHELLDLL_DefView", null) != nint.Zero)
            {
                shellHost = topHandle;
                return false;
            }
            return true;
        }, nint.Zero);

        if (shellHost != nint.Zero)
            return shellHost;

        return Win32Api.FindWindow("Progman", null);
    }

    public static void AttachToWallpaperLayer(nint hwnd, MonitorBounds monitor)
    {
        if (hwnd == nint.Zero) return;

        WallpapersByMonitor[monitor.DeviceName] = hwnd;

        int ex = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
        ex |= Win32Api.WS_EX_TOOLWINDOW | Win32Api.WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
        ex &= ~Win32Api.WS_EX_APPWINDOW;
        _ = Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, ex);

        var desktopOwner = GetDesktopOwnerWindow();
        if (desktopOwner != nint.Zero)
        {
            _ = SetOwner(hwnd, desktopOwner);
        }

        Win32Api.SetWindowPos(
            hwnd,
            HWND_BOTTOM,
            monitor.PhysicalLeft,
            monitor.PhysicalTop,
            monitor.PhysicalWidth,
            monitor.PhysicalHeight,
            Win32Api.SWP_NOACTIVATE);
    }

    public static void UnregisterWallpaper(string deviceName, nint hwnd)
    {
        if (WallpapersByMonitor.TryGetValue(deviceName, out var existing) && existing == hwnd)
        {
            WallpapersByMonitor.Remove(deviceName);
        }
    }

    public static void AttachWidgetToDesktop(nint hwnd)
    {
        if (hwnd == nint.Zero) return;

        // Find the exact monitor this widget sits on so we attach to THAT monitor's LiveWallpaperWindow!
        nint owner = nint.Zero;
        if (Win32Api.GetWindowRect(hwnd, out var rect))
        {
            var mon = MonitorHelper.GetMonitorForPhysicalRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (WallpapersByMonitor.TryGetValue(mon.DeviceName, out var wpHwnd) &&
                wpHwnd != nint.Zero &&
                Win32Api.IsWindow(wpHwnd))
            {
                owner = wpHwnd;
            }
        }

        if (owner == nint.Zero)
            owner = GetDesktopOwnerWindow();

        if (owner != nint.Zero)
        {
            _ = SetOwner(hwnd, owner);
        }

        SendToDesktopBottom(hwnd);
    }

    public static void SendToDesktopBottom(nint hwnd)
    {
        if (hwnd == nint.Zero) return;

        // Push widget below normal app windows...
        Win32Api.SetWindowPos(
            hwnd,
            HWND_BOTTOM,
            0, 0, 0, 0,
            SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOACTIVATE);

        // ...then push EVERY Live Wallpaper strictly below the widgets!
        foreach (var kv in WallpapersByMonitor.ToList())
        {
            var wpHwnd = kv.Value;
            if (wpHwnd == nint.Zero || wpHwnd == hwnd) continue;
            if (!Win32Api.IsWindow(wpHwnd))
            {
                WallpapersByMonitor.Remove(kv.Key);
                continue;
            }
            Win32Api.SetWindowPos(
                wpHwnd,
                HWND_BOTTOM,
                0, 0, 0, 0,
                SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOACTIVATE);
        }
    }
}
