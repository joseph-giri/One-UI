using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace OneUI.App.Native;

public sealed record MonitorBounds(
    string DeviceName,
    bool IsPrimary,
    double LeftDip,
    double TopDip,
    double WidthDip,
    double HeightDip,
    int PhysicalLeft,
    int PhysicalTop,
    int PhysicalWidth,
    int PhysicalHeight,
    double DpiScaleX,
    double DpiScaleY)
{
    public double RightDip => LeftDip + WidthDip;
    public double BottomDip => TopDip + HeightDip;
}

/// <summary>
/// Per-monitor DPI-aware display helper supporting arbitrary multi-monitor layouts
/// (including negative coordinates and mixed DPI scaling).
/// </summary>
public static class MonitorHelper
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Win32Api.POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public static List<MonitorBounds> GetAllMonitors()
    {
        var list = new List<MonitorBounds>();
        foreach (var screen in WinForms.Screen.AllScreens)
        {
            list.Add(FromScreen(screen));
        }
        return list;
    }

    public static MonitorBounds GetPrimaryMonitor()
    {
        var primary = WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
        return FromScreen(primary);
    }

    public static MonitorBounds GetMonitorAtCursor()
    {
        if (Win32Api.GetCursorPos(out var pt))
        {
            var screen = WinForms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
            return FromScreen(screen);
        }
        return GetPrimaryMonitor();
    }

    public static MonitorBounds GetMonitorForPhysicalRect(int left, int top, int right, int bottom)
    {
        int cx = left + Math.Max(1, (right - left) / 2);
        int cy = top + Math.Max(1, (bottom - top) / 2);
        var screen = WinForms.Screen.FromPoint(new System.Drawing.Point(cx, cy));
        return FromScreen(screen);
    }

    private static MonitorBounds FromScreen(WinForms.Screen screen)
    {
        var b = screen.Bounds;
        var (scaleX, scaleY) = GetDpiScaleForPoint(b.Left + b.Width / 2, b.Top + b.Height / 2);

        return new MonitorBounds(
            DeviceName: screen.DeviceName,
            IsPrimary: screen.Primary,
            LeftDip: b.Left / scaleX,
            TopDip: b.Top / scaleY,
            WidthDip: b.Width / scaleX,
            HeightDip: b.Height / scaleY,
            PhysicalLeft: b.Left,
            PhysicalTop: b.Top,
            PhysicalWidth: b.Width,
            PhysicalHeight: b.Height,
            DpiScaleX: scaleX,
            DpiScaleY: scaleY);
    }

    public static (double ScaleX, double ScaleY) GetDpiScaleForPoint(int x, int y)
    {
        try
        {
            var hMon = MonitorFromPoint(new Win32Api.POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
            if (hMon != nint.Zero && GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dx, out uint dy) == 0 && dx > 0 && dy > 0)
            {
                return (dx / 96.0, dy / 96.0);
            }
        }
        catch { }

        return (1.0, 1.0);
    }
}
