using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OneUI.App.Native;
using OneUI.Core.Models;

namespace OneUI.App.Views;

public partial class TrafficLightOverlay : Window
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _visibilityGuard;
    private nint _targetHwnd;
    private bool _isDragging;

    public TrafficLightOverlay(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        Tag = Visibility.Collapsed;

        _visibilityGuard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _visibilityGuard.Tick += (_, _) => VerifyTargetStillVisible();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == nint.Zero) return;
            int ex = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
            ex |= Win32Api.WS_EX_TOOLWINDOW | Win32Api.WS_EX_NOACTIVATE;
            ex &= ~Win32Api.WS_EX_APPWINDOW;
            _ = Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, ex);
        };
    }

    private void VerifyTargetStillVisible()
    {
        if (!IsVisible)
        {
            _visibilityGuard.Stop();
            return;
        }

        if (_isDragging ||
            _targetHwnd == nint.Zero ||
            !Win32Api.IsWindow(_targetHwnd) ||
            !Win32Api.IsWindowVisible(_targetHwnd) ||
            Win32Api.IsIconic(_targetHwnd) ||
            Win32Api.GetForegroundWindow() != _targetHwnd ||
            (Win32Api.DwmGetWindowAttribute(_targetHwnd, Win32Api.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0))
        {
            HideOverlay();
        }
    }

    private void HideOverlay()
    {
        _targetHwnd = nint.Zero;
        _visibilityGuard.Stop();
        if (IsVisible) Hide();
    }

    public void SetDragging(bool isDragging)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _isDragging = isDragging;
            if (_isDragging && IsVisible)
            {
                Hide();
            }
        });
    }

    public void UpdateForWindow(WindowInfo? info)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_isDragging ||
                !_settings.EnableTrafficLights ||
                info is null ||
                info.Handle == nint.Zero ||
                info.IsMinimized ||
                info.IsFullScreen ||
                info.Width < 320 ||
                info.Height < 140 ||
                !Win32Api.IsWindowVisible(info.Handle) ||
                IsExcluded(info.ProcessName))
            {
                HideOverlay();
                return;
            }

            _targetHwnd = info.Handle;

            Win32Api.RECT frameRect;
            if (Win32Api.DwmGetWindowAttribute(
                    info.Handle,
                    Win32Api.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out frameRect,
                    Marshal.SizeOf<Win32Api.RECT>()) != 0)
            {
                frameRect = new Win32Api.RECT
                {
                    Left = info.Left,
                    Top = info.Top,
                    Right = info.Right,
                    Bottom = info.Bottom
                };
            }

            var mon = MonitorHelper.GetMonitorForPhysicalRect(frameRect.Left, frameRect.Top, frameRect.Right, frameRect.Bottom);

            // 156x32 DIPs: covers the 138px Windows caption cluster + 18px feathered left blend zone
            int coverWidthPx = (int)Math.Round(156 * mon.DpiScaleX);
            int coverHeightPx = (int)Math.Round(32 * mon.DpiScaleY);

            int xPx = frameRect.Right - coverWidthPx - (info.IsMaximized ? 0 : 1);
            int yPx = Math.Max(mon.PhysicalTop + (int)Math.Round(28 * mon.DpiScaleY), frameRect.Top + (info.IsMaximized ? 0 : 1));

            // Sample 9 pixels along the top 3px titlebar edge (where no app draws icons or text)
            // and take the median RGB so it never gets fooled by toolbar icons!
            int sampleBaseX = xPx - (int)Math.Round(6 * mon.DpiScaleX);
            int sampleY = yPx + (int)Math.Round(3 * mon.DpiScaleY);
            var titlebarColor = SampleMedianTitlebarColor(sampleBaseX, sampleY, (int)Math.Round(4 * mon.DpiScaleX));

            // Smooth horizontal left-edge alpha feather (0% -> 100% over left 12%) so there is zero hard seam
            var featheredBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
            featheredBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, titlebarColor.R, titlebarColor.G, titlebarColor.B), 0.0));
            featheredBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, titlebarColor.R, titlebarColor.G, titlebarColor.B), 0.12));
            featheredBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, titlebarColor.R, titlebarColor.G, titlebarColor.B), 1.0));

            CoverBorder.Background = featheredBrush;
            CoverBorder.CornerRadius = info.IsMaximized ? new CornerRadius(0) : new CornerRadius(0, 7, 0, 0);

            if (!IsVisible) Show();
            _visibilityGuard.Start();

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != nint.Zero)
            {
                Win32Api.SetWindowPos(
                    hwnd,
                    Win32Api.HWND_TOPMOST,
                    xPx,
                    yPx,
                    coverWidthPx,
                    coverHeightPx,
                    Win32Api.SWP_NOACTIVATE);
            }
        });
    }

    private static Color SampleMedianTitlebarColor(int startX, int y, int stepX)
    {
        nint hdc = Win32Api.GetDC(nint.Zero);
        if (hdc == nint.Zero) return Color.FromRgb(32, 32, 32);

        try
        {
            var rs = new List<byte>(9);
            var gs = new List<byte>(9);
            var bs = new List<byte>(9);

            for (int i = 0; i < 9; i++)
            {
                uint pixel = Win32Api.GetPixel(hdc, startX - (i * stepX), y);
                if (pixel == 0xFFFFFFFF) continue;
                rs.Add((byte)(pixel & 0xFF));
                gs.Add((byte)((pixel >> 8) & 0xFF));
                bs.Add((byte)((pixel >> 16) & 0xFF));
            }

            if (rs.Count == 0) return Color.FromRgb(32, 32, 32);

            rs.Sort();
            gs.Sort();
            bs.Sort();
            int mid = rs.Count / 2;
            return Color.FromRgb(rs[mid], gs[mid], bs[mid]);
        }
        finally
        {
            Win32Api.ReleaseDC(nint.Zero, hdc);
        }
    }

    private bool IsExcluded(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return true;
        return _settings.TrafficLightExcludedProcesses.Any(p =>
            p.Equals(processName, StringComparison.OrdinalIgnoreCase));
    }

    private void Overlay_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => Tag = Visibility.Visible;
    private void Overlay_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => Tag = Visibility.Collapsed;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_targetHwnd != nint.Zero)
        {
            WindowTracker.CloseWindow(_targetHwnd);
            Hide();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_targetHwnd != nint.Zero)
        {
            WindowTracker.MinimizeWindow(_targetHwnd);
            Hide();
        }
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (_targetHwnd != nint.Zero)
        {
            WindowTracker.ToggleMaximizeWindow(_targetHwnd);
        }
    }
}
