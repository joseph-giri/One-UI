using System.Runtime.InteropServices;

namespace OneUI.App.Native;

/// <summary>
/// Reserves the Top Bar strip on every connected monitor using the documented
/// per-monitor Win32 SystemParametersInfo(SPI_SETWORKAREA) API so maximized windows
/// automatically sit cleanly below the Top Bar with their titlebars 100% visible.
/// Saves and restores each monitor's original work area on exit or crash.
/// </summary>
public static class AppBarHelper
{
    private const uint SPI_SETWORKAREA = 0x002F;
    private const uint SPIF_SENDCHANGE = 0x0002;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private static readonly Dictionary<string, Win32Api.RECT> OriginalWorkAreas = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public Win32Api.RECT rcMonitor;
        public Win32Api.RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref Win32Api.RECT pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Win32Api.POINT pt, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    public static void RegisterOrUpdateTopBar(nint hwnd, MonitorBounds monitor, int heightDip = 28, bool taskbarHidden = true)
    {
        if (hwnd == nint.Zero) return;

        int barHeightPx = (int)Math.Round(heightDip * monitor.DpiScaleY);

        // Position the Top Bar window only if its physical bounds differ (avoids DWM flicker)
        if (!Win32Api.GetWindowRect(hwnd, out var curRect) ||
            curRect.Left != monitor.PhysicalLeft ||
            curRect.Top != monitor.PhysicalTop ||
            (curRect.Right - curRect.Left) != monitor.PhysicalWidth ||
            (curRect.Bottom - curRect.Top) != barHeightPx)
        {
            Win32Api.SetWindowPos(
                hwnd,
                nint.Zero,
                monitor.PhysicalLeft,
                monitor.PhysicalTop,
                monitor.PhysicalWidth,
                barHeightPx,
                Win32Api.SWP_NOZORDER | Win32Api.SWP_NOACTIVATE);
        }

        // Save original monitor work area once so we can restore it cleanly on exit
        var origWork = GetMonitorWorkArea(monitor);
        if (!OriginalWorkAreas.ContainsKey(monitor.DeviceName))
        {
            OriginalWorkAreas[monitor.DeviceName] = origWork;
        }
        else
        {
            origWork = OriginalWorkAreas[monitor.DeviceName];
        }

        // Reserve the top barHeightPx strip and reclaim the bottom 48px when the Windows taskbar is hidden
        int fullBottom = monitor.PhysicalTop + monitor.PhysicalHeight;
        var reserved = new Win32Api.RECT
        {
            Left = monitor.PhysicalLeft,
            Top = monitor.PhysicalTop + barHeightPx,
            Right = monitor.PhysicalLeft + monitor.PhysicalWidth,
            Bottom = taskbarHidden ? fullBottom : origWork.Bottom
        };

        _ = SystemParametersInfo(SPI_SETWORKAREA, 0, ref reserved, 0);

        // Nudge any already-maximized windows on this monitor so they fill the updated work area immediately
        NudgeMaximizedWindowsOnMonitor(monitor, monitor.PhysicalTop + barHeightPx, reserved.Bottom);
    }

    public static void EnsureWorkAreaReserved(nint hwnd, MonitorBounds monitor, int heightDip = 28, bool taskbarHidden = true)
    {
        int barHeightPx = (int)Math.Round(heightDip * monitor.DpiScaleY);
        int expectedBottom = taskbarHidden
            ? monitor.PhysicalTop + monitor.PhysicalHeight
            : (OriginalWorkAreas.TryGetValue(monitor.DeviceName, out var orig) ? orig.Bottom : monitor.PhysicalTop + monitor.PhysicalHeight);

        var currentWork = GetMonitorWorkArea(monitor);
        if (currentWork.Top < monitor.PhysicalTop + barHeightPx - 2 ||
            Math.Abs(currentWork.Bottom - expectedBottom) > 4)
        {
            RegisterOrUpdateTopBar(hwnd, monitor, heightDip, taskbarHidden);
        }
    }

    private static void NudgeMaximizedWindowsOnMonitor(MonitorBounds monitor, int minTopPx, int targetBottomPx)
    {
        int ownPid = Environment.ProcessId;
        Win32Api.EnumWindows((hwnd, lParam) =>
        {
            if (!Win32Api.IsWindowVisible(hwnd) || !Win32Api.IsZoomed(hwnd))
                return true;

            _ = Win32Api.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)ownPid)
                return true;

            if (Win32Api.GetWindowRect(hwnd, out var rect))
            {
                var winMon = MonitorHelper.GetMonitorForPhysicalRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (string.Equals(winMon.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase) &&
                    (rect.Top < minTopPx - 8 || rect.Bottom < targetBottomPx - 12))
                {
                    Win32Api.ShowWindow(hwnd, Win32Api.SW_RESTORE);
                    Win32Api.ShowWindow(hwnd, Win32Api.SW_SHOWMAXIMIZED);
                }
            }
            return true;
        }, nint.Zero);
    }

    public static void Unregister(nint hwnd)
    {
        // Individual bar close handled via UnregisterAll when disabling Top Bar or exiting
    }

    public static void UnregisterAll()
    {
        foreach (var kv in OriginalWorkAreas.ToList())
        {
            var rect = kv.Value;
            _ = SystemParametersInfo(SPI_SETWORKAREA, 0, ref rect, 0);
        }
        OriginalWorkAreas.Clear();
    }

    private static Win32Api.RECT GetMonitorWorkArea(MonitorBounds monitor)
    {
        var hMon = MonitorFromPoint(
            new Win32Api.POINT
            {
                X = monitor.PhysicalLeft + monitor.PhysicalWidth / 2,
                Y = monitor.PhysicalTop + monitor.PhysicalHeight / 2
            },
            MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (hMon != nint.Zero && GetMonitorInfo(hMon, ref info))
        {
            return info.rcWork;
        }

        return new Win32Api.RECT
        {
            Left = monitor.PhysicalLeft,
            Top = monitor.PhysicalTop,
            Right = monitor.PhysicalLeft + monitor.PhysicalWidth,
            Bottom = monitor.PhysicalTop + monitor.PhysicalHeight
        };
    }
}
