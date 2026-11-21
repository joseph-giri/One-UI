using System.Diagnostics;
using OneUI.Core.Models;

namespace OneUI.App.Native;

/// <summary>
/// Tracks foreground window changes, geometry updates, and running top-level desktop apps
/// using Win32 SetWinEventHook and EnumWindows with smart diffing to prevent UI flicker.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private readonly List<nint> _hooks = [];
    private readonly Win32Api.WinEventDelegate _eventProc;
    private readonly int _currentProcessId = Environment.ProcessId;
    private string _lastRunningSignature = string.Empty;
    private DateTime _lastRunningCheckUtc = DateTime.MinValue;
    private bool _disposed;

    public event Action<WindowInfo?>? ForegroundWindowChanged;
    public event Action<bool>? WindowDragStateChanged;
    public event Action? RunningWindowsChanged;

    public WindowTracker()
    {
        _eventProc = OnWinEvent;
    }

    public void Start()
    {
        RegisterHook(Win32Api.EVENT_SYSTEM_FOREGROUND, Win32Api.EVENT_SYSTEM_FOREGROUND);
        RegisterHook(Win32Api.EVENT_SYSTEM_MOVESIZESTART, Win32Api.EVENT_SYSTEM_MOVESIZEEND);
        RegisterHook(Win32Api.EVENT_SYSTEM_MINIMIZESTART, Win32Api.EVENT_SYSTEM_MINIMIZEEND);
        RegisterHook(Win32Api.EVENT_OBJECT_LOCATIONCHANGE, Win32Api.EVENT_OBJECT_NAMECHANGE);
        RegisterHook(Win32Api.EVENT_OBJECT_CREATE, Win32Api.EVENT_OBJECT_DESTROY);
    }

    private void RegisterHook(uint minEvent, uint maxEvent)
    {
        var hook = Win32Api.SetWinEventHook(
            minEvent,
            maxEvent,
            nint.Zero,
            _eventProc,
            0,
            0,
            Win32Api.WINEVENT_OUTOFCONTEXT | Win32Api.WINEVENT_SKIPOWNPROCESS);

        if (hook != nint.Zero)
        {
            _hooks.Add(hook);
        }
    }

    private void OnWinEvent(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (idObject != 0 || idChild != 0 || hwnd == nint.Zero)
            return;

        if (eventType == Win32Api.EVENT_SYSTEM_MOVESIZESTART)
        {
            WindowDragStateChanged?.Invoke(true);
            return;
        }

        if (eventType == Win32Api.EVENT_SYSTEM_MOVESIZEEND)
        {
            WindowDragStateChanged?.Invoke(false);
            ForegroundWindowChanged?.Invoke(GetForegroundWindowInfo());
            return;
        }

        if (eventType is Win32Api.EVENT_SYSTEM_FOREGROUND
            or Win32Api.EVENT_SYSTEM_MINIMIZESTART
            or Win32Api.EVENT_SYSTEM_MINIMIZEEND)
        {
            ForegroundWindowChanged?.Invoke(GetForegroundWindowInfo());
            CheckIfRunningWindowsChanged(force: true);
        }
        else if (eventType is Win32Api.EVENT_OBJECT_LOCATIONCHANGE or Win32Api.EVENT_OBJECT_NAMECHANGE)
        {
            if (hwnd == Win32Api.GetForegroundWindow())
            {
                ForegroundWindowChanged?.Invoke(GetWindowInfo(hwnd));
            }
            if (eventType == Win32Api.EVENT_OBJECT_NAMECHANGE)
            {
                CheckIfRunningWindowsChanged(force: false);
            }
        }
        else if (eventType is Win32Api.EVENT_OBJECT_CREATE or Win32Api.EVENT_OBJECT_DESTROY)
        {
            CheckIfRunningWindowsChanged(force: false);
        }
    }

    private void CheckIfRunningWindowsChanged(bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - _lastRunningCheckUtc).TotalMilliseconds < 250)
            return;

        _lastRunningCheckUtc = now;
        var windows = GetRunningAppWindows();
        var sig = string.Join('|', windows.Select(w => $"{w.Handle}:{w.ProcessName}:{w.Title}"));
        if (!string.Equals(sig, _lastRunningSignature, StringComparison.Ordinal))
        {
            _lastRunningSignature = sig;
            RunningWindowsChanged?.Invoke();
        }
    }

    public static bool IsWindowsShellFlyoutActive()
    {
        var fg = Win32Api.GetForegroundWindow();
        if (fg == nint.Zero) return false;
        _ = Win32Api.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0) return false;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName is "StartMenuExperienceHost" or "SearchHost" or "ShellExperienceHost";
        }
        catch
        {
            return false;
        }
    }

    public WindowInfo? GetForegroundWindowInfo()
    {
        var fg = Win32Api.GetForegroundWindow();
        if (fg == nint.Zero) return null;
        return GetWindowInfo(fg);
    }

    public WindowInfo? GetWindowInfo(nint hwnd)
    {
        if (hwnd == nint.Zero || !Win32Api.IsWindow(hwnd)) return null;

        _ = Win32Api.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)_currentProcessId) return null;

        string className = Win32Api.GetWindowClassName(hwnd);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow")
            return null;

        if (!Win32Api.GetWindowRect(hwnd, out var rect))
            return null;

        // Unwrap UWP / Microsoft Store apps (WhatsApp, Settings, Photos, Calculator) hosted inside ApplicationFrameWindow
        uint effectivePid = pid;
        if (className == "ApplicationFrameWindow")
        {
            uint childPid = 0;
            Win32Api.EnumChildWindows(hwnd, (childHwnd, lParam) =>
            {
                _ = Win32Api.GetWindowThreadProcessId(childHwnd, out uint cPid);
                if (cPid != 0 && cPid != pid)
                {
                    childPid = cPid;
                    return false;
                }
                return true;
            }, nint.Zero);

            if (childPid != 0)
                effectivePid = childPid;
        }

        string title = Win32Api.GetWindowTitle(hwnd);
        string exePath = Win32Api.TryGetProcessPath(effectivePid);
        string procName = string.Empty;
        try
        {
            using var proc = Process.GetProcessById((int)effectivePid);
            procName = proc.ProcessName;
        }
        catch
        {
            if (!string.IsNullOrEmpty(exePath))
                procName = Path.GetFileNameWithoutExtension(exePath);
        }

        var mon = MonitorHelper.GetMonitorForPhysicalRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        bool isFullScreen = IsWindowFullScreenOnMonitor(hwnd, rect, mon);

        return new WindowInfo
        {
            Handle = hwnd,
            Title = title,
            ProcessName = procName,
            ExecutablePath = exePath,
            ProcessId = (int)pid,
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom,
            IsMinimized = Win32Api.IsIconic(hwnd),
            IsMaximized = Win32Api.IsZoomed(hwnd),
            IsFullScreen = isFullScreen
        };
    }

    internal static bool IsWindowFullScreenOnMonitor(nint hwnd, Win32Api.RECT fallbackRect, MonitorBounds mon)
    {
        if (Win32Api.IsIconic(hwnd)) return false;

        if (Win32Api.DwmGetWindowAttribute(
                hwnd,
                Win32Api.DWMWA_EXTENDED_FRAME_BOUNDS,
                out Win32Api.RECT r,
                System.Runtime.InteropServices.Marshal.SizeOf<Win32Api.RECT>()) != 0)
        {
            r = fallbackRect;
        }

        const int tol = 2;
        return r.Left <= mon.PhysicalLeft + tol &&
               r.Top <= mon.PhysicalTop + tol &&
               r.Right >= mon.PhysicalLeft + mon.PhysicalWidth - tol &&
               r.Bottom >= mon.PhysicalTop + mon.PhysicalHeight - tol;
    }

    public static bool IsMonitorFullscreen(MonitorBounds monitor)
    {
        var fg = Win32Api.GetForegroundWindow();
        if (fg == nint.Zero || !Win32Api.IsWindowVisible(fg) || Win32Api.IsIconic(fg))
            return false;

        _ = Win32Api.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
            return false;

        string cls = Win32Api.GetWindowClassName(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        if (!Win32Api.GetWindowRect(fg, out var rect))
            return false;

        var fgMon = MonitorHelper.GetMonitorForPhysicalRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (!string.Equals(fgMon.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase))
            return false;

        return IsWindowFullScreenOnMonitor(fg, rect, monitor);
    }

    public List<WindowInfo> GetRunningAppWindows()
    {
        var results = new List<WindowInfo>();

        Win32Api.EnumWindows((hwnd, _) =>
        {
            if (!IsTaskbarCandidate(hwnd))
                return true;

            var info = GetWindowInfo(hwnd);
            if (info is not null && !string.IsNullOrWhiteSpace(info.Title))
            {
                results.Add(info);
            }
            return true;
        }, nint.Zero);

        return results;
    }

    private bool IsTaskbarCandidate(nint hwnd)
    {
        if (!Win32Api.IsWindowVisible(hwnd))
            return false;

        if (Win32Api.GetWindow(hwnd, Win32Api.GW_OWNER) != nint.Zero)
            return false;

        int exStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
        if ((exStyle & Win32Api.WS_EX_TOOLWINDOW) != 0)
            return false;

        if (Win32Api.DwmGetWindowAttribute(hwnd, Win32Api.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        _ = Win32Api.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)_currentProcessId)
            return false;

        return Win32Api.GetWindowTextLength(hwnd) > 0;
    }

    public static void ActivateWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || !Win32Api.IsWindow(hwnd)) return;

        if (Win32Api.IsIconic(hwnd))
        {
            Win32Api.ShowWindow(hwnd, Win32Api.SW_RESTORE);
        }

        Win32Api.SwitchToThisWindow(hwnd, true);
        Win32Api.BringWindowToTop(hwnd);
        Win32Api.SetForegroundWindow(hwnd);
    }

    public static void FocusOrRestore(nint hwnd)
    {
        if (hwnd == nint.Zero || !Win32Api.IsWindow(hwnd)) return;

        if (Win32Api.IsIconic(hwnd))
        {
            ActivateWindow(hwnd);
            return;
        }

        if (Win32Api.GetForegroundWindow() == hwnd)
        {
            Win32Api.ShowWindow(hwnd, Win32Api.SW_MINIMIZE);
            return;
        }

        ActivateWindow(hwnd);
    }

    public static void MinimizeWindow(nint hwnd)
    {
        if (hwnd != nint.Zero && Win32Api.IsWindow(hwnd))
            Win32Api.ShowWindow(hwnd, Win32Api.SW_MINIMIZE);
    }

    public static void ToggleMaximizeWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || !Win32Api.IsWindow(hwnd)) return;
        Win32Api.ShowWindow(hwnd, Win32Api.IsZoomed(hwnd) ? Win32Api.SW_RESTORE : Win32Api.SW_SHOWMAXIMIZED);
        ActivateWindow(hwnd);
    }

    public static void SnapWindowHalf(nint hwnd, bool leftHalf)
    {
        if (hwnd == nint.Zero || !Win32Api.IsWindow(hwnd)) return;
        if (Win32Api.IsZoomed(hwnd))
            Win32Api.ShowWindow(hwnd, Win32Api.SW_RESTORE);

        if (!Win32Api.GetWindowRect(hwnd, out var rect)) return;
        var mon = MonitorHelper.GetMonitorForPhysicalRect(rect.Left, rect.Top, rect.Right, rect.Bottom);

        int topBarPx = (int)Math.Round(28 * mon.DpiScaleY);
        int dockReservePx = (int)Math.Round(76 * mon.DpiScaleY);
        int workTop = mon.PhysicalTop + topBarPx;
        int workHeight = Math.Max(300, mon.PhysicalHeight - topBarPx - dockReservePx);
        int halfWidth = mon.PhysicalWidth / 2;
        int x = leftHalf ? mon.PhysicalLeft : mon.PhysicalLeft + halfWidth;

        Win32Api.SetWindowPos(hwnd, nint.Zero, x, workTop, halfWidth, workHeight, Win32Api.SWP_NOZORDER);
        ActivateWindow(hwnd);
    }

    public static void MoveWindowToOtherMonitor(nint hwnd)
    {
        if (hwnd == nint.Zero || !Win32Api.IsWindow(hwnd)) return;
        var monitors = MonitorHelper.GetAllMonitors();
        if (monitors.Count < 2) return;

        if (!Win32Api.GetWindowRect(hwnd, out var rect)) return;
        var currentMon = MonitorHelper.GetMonitorForPhysicalRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        int idx = monitors.FindIndex(m => string.Equals(m.DeviceName, currentMon.DeviceName, StringComparison.OrdinalIgnoreCase));
        var nextMon = monitors[(idx + 1) % monitors.Count];

        bool wasMaximized = Win32Api.IsZoomed(hwnd);
        if (wasMaximized)
            Win32Api.ShowWindow(hwnd, Win32Api.SW_RESTORE);

        int topBarPx = (int)Math.Round(28 * nextMon.DpiScaleY);
        int width = Math.Min(rect.Right - rect.Left, nextMon.PhysicalWidth - 40);
        int height = Math.Min(rect.Bottom - rect.Top, nextMon.PhysicalHeight - topBarPx - 80);
        int x = nextMon.PhysicalLeft + (nextMon.PhysicalWidth - width) / 2;
        int y = nextMon.PhysicalTop + topBarPx + 24;

        Win32Api.SetWindowPos(hwnd, nint.Zero, x, y, width, height, Win32Api.SWP_NOZORDER);
        if (wasMaximized)
            Win32Api.ShowWindow(hwnd, Win32Api.SW_SHOWMAXIMIZED);

        ActivateWindow(hwnd);
    }

    public static bool ToggleAlwaysOnTop(nint hwnd)
    {
        if (hwnd == nint.Zero || !Win32Api.IsWindow(hwnd)) return false;
        int ex = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
        bool currentlyTop = (ex & Win32Api.WS_EX_TOPMOST) != 0;
        Win32Api.SetWindowPos(
            hwnd,
            currentlyTop ? Win32Api.HWND_NOTOPMOST : Win32Api.HWND_TOPMOST,
            0, 0, 0, 0,
            Win32Api.SWP_NOSIZE | 0x0002 /* SWP_NOMOVE */);
        ActivateWindow(hwnd);
        return !currentlyTop;
    }

    public static void CloseWindow(nint hwnd)
    {
        if (hwnd != nint.Zero && Win32Api.IsWindow(hwnd))
            Win32Api.PostMessage(hwnd, Win32Api.WM_CLOSE, nint.Zero, nint.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var hook in _hooks)
        {
            Win32Api.UnhookWinEvent(hook);
        }
        _hooks.Clear();
    }
}
