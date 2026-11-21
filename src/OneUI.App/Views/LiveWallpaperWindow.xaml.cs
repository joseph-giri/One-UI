using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OneUI.App.Native;
using OneUI.Core.Models;

namespace OneUI.App.Views;

public partial class LiveWallpaperWindow : Window
{
    private readonly MonitorBounds _monitor;
    private readonly DispatcherTimer _pauseCheckTimer;
    private HwndSource? _hwndSource;
    private bool _isPausedForFullscreen;
    private bool _isVideoMode;
    private bool _pauseOnFullscreen = true;
    private string _currentVideoPath = string.Empty;

    public LiveWallpaperWindow(MonitorBounds monitor)
    {
        InitializeComponent();
        _monitor = monitor;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == nint.Zero) return;

            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);
            DesktopLayerHelper.AttachToWallpaperLayer(hwnd, _monitor);
        };

        Loaded += (_, _) => KeepAtBottom();

        _pauseCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _pauseCheckTimer.Tick += (_, _) => CheckFullscreenPause();
        _pauseCheckTimer.Start();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // Permanently lock this wallpaper window to HWND_BOTTOM so it never rises above apps or widgets
        if (msg == DesktopLayerHelper.WM_WINDOWPOSCHANGING && lParam != nint.Zero)
        {
            var pos = Marshal.PtrToStructure<DesktopLayerHelper.WINDOWPOS>(lParam);
            pos.hwndInsertAfter = DesktopLayerHelper.HWND_BOTTOM;
            Marshal.StructureToPtr(pos, lParam, false);
        }
        return nint.Zero;
    }

    public void KeepAtBottom()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
            DesktopLayerHelper.AttachToWallpaperLayer(hwnd, _monitor);
    }

    public void ApplySettings(AppSettings settings)
    {
        _pauseOnFullscreen = settings.PauseWallpaperOnFullscreen;

        if (string.Equals(settings.WallpaperMode, "Video", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(settings.WallpaperVideoPath) &&
            File.Exists(settings.WallpaperVideoPath))
        {
            _isVideoMode = true;
            AnimatedShaderCanvas.Visibility = Visibility.Collapsed;
            VideoPlayer.Visibility = Visibility.Visible;

            if (!string.Equals(_currentVideoPath, settings.WallpaperVideoPath, StringComparison.OrdinalIgnoreCase))
            {
                _currentVideoPath = settings.WallpaperVideoPath;
                try
                {
                    VideoPlayer.Stop();
                    VideoPlayer.Source = new Uri(settings.WallpaperVideoPath, UriKind.Absolute);
                    VideoPlayer.Play();
                }
                catch
                {
                    FallbackToPreset("Aurora");
                }
            }
            else if (!_isPausedForFullscreen)
            {
                VideoPlayer.Play();
            }
        }
        else
        {
            _currentVideoPath = string.Empty;
            FallbackToPreset(settings.WallpaperMode);
        }

        KeepAtBottom();
    }

    private void FallbackToPreset(string mode)
    {
        _isVideoMode = false;
        VideoPlayer.Stop();
        VideoPlayer.Visibility = Visibility.Collapsed;
        AnimatedShaderCanvas.Visibility = Visibility.Visible;

        switch (mode)
        {
            case "LiquidMesh":
                SetBaseGradient(Color.FromRgb(10, 12, 28), Color.FromRgb(22, 16, 48), Color.FromRgb(10, 18, 36));
                SetOrbColors(Orb1, Color.FromArgb(215, 10, 132, 255), Color.FromArgb(95, 94, 92, 230));
                SetOrbColors(Orb2, Color.FromArgb(210, 255, 55, 95), Color.FromArgb(85, 191, 90, 242));
                SetOrbColors(Orb3, Color.FromArgb(190, 48, 209, 255), Color.FromArgb(70, 10, 132, 255));
                break;

            case "Nebula":
                SetBaseGradient(Color.FromRgb(16, 8, 22), Color.FromRgb(32, 14, 28), Color.FromRgb(12, 10, 24));
                SetOrbColors(Orb1, Color.FromArgb(215, 255, 149, 0), Color.FromArgb(90, 255, 55, 95));
                SetOrbColors(Orb2, Color.FromArgb(205, 191, 90, 242), Color.FromArgb(85, 94, 92, 230));
                SetOrbColors(Orb3, Color.FromArgb(185, 255, 94, 58), Color.FromArgb(70, 191, 90, 242));
                break;

            default: // Aurora
                SetBaseGradient(Color.FromRgb(6, 12, 24), Color.FromRgb(12, 24, 42), Color.FromRgb(8, 12, 26));
                SetOrbColors(Orb1, Color.FromArgb(215, 0, 232, 255), Color.FromArgb(90, 0, 132, 255));
                SetOrbColors(Orb2, Color.FromArgb(205, 48, 209, 88), Color.FromArgb(85, 94, 92, 230));
                SetOrbColors(Orb3, Color.FromArgb(195, 123, 47, 255), Color.FromArgb(75, 0, 200, 255));
                break;
        }

        StartOrbAnimations();
    }

    private void SetBaseGradient(Color c1, Color c2, Color c3)
    {
        BaseBackdropBrush.GradientStops.Clear();
        BaseBackdropBrush.GradientStops.Add(new GradientStop(c1, 0));
        BaseBackdropBrush.GradientStops.Add(new GradientStop(c2, 0.5));
        BaseBackdropBrush.GradientStops.Add(new GradientStop(c3, 1));
    }

    private static void SetOrbColors(System.Windows.Shapes.Ellipse orb, Color inner, Color mid)
    {
        orb.Fill = new RadialGradientBrush
        {
            GradientStops =
            [
                new GradientStop(inner, 0.0),
                new GradientStop(mid, 0.48),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0)
            ]
        };
    }

    private void StartOrbAnimations()
    {
        AnimateAxis(Orb1Transform, TranslateTransform.XProperty, -60, 220, 14);
        AnimateAxis(Orb1Transform, TranslateTransform.YProperty, -40, 140, 19);

        AnimateAxis(Orb2Transform, TranslateTransform.XProperty, 60, -240, 17);
        AnimateAxis(Orb2Transform, TranslateTransform.YProperty, 40, -160, 13);

        AnimateAxis(Orb3Transform, TranslateTransform.XProperty, -180, 180, 21);
        AnimateAxis(Orb3Transform, TranslateTransform.YProperty, -110, 110, 16);
    }

    private static void AnimateAxis(TranslateTransform target, DependencyProperty prop, double from, double to, double seconds)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        target.BeginAnimation(prop, anim);
    }

    private void CheckFullscreenPause()
    {
        if (!_pauseOnFullscreen) return;

        bool shouldPause = WindowTracker.IsMonitorFullscreen(_monitor);
        if (shouldPause && !_isPausedForFullscreen)
        {
            _isPausedForFullscreen = true;
            if (_isVideoMode) VideoPlayer.Pause();
        }
        else if (!shouldPause && _isPausedForFullscreen)
        {
            _isPausedForFullscreen = false;
            if (_isVideoMode) VideoPlayer.Play();
        }
    }

    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (!_isPausedForFullscreen)
        {
            VideoPlayer.Play();
        }
        KeepAtBottom();
    }

    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Position = TimeSpan.Zero;
        VideoPlayer.Play();
    }

    private void VideoPlayer_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OneUI");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:O}] Wallpaper MediaFailed: {e.ErrorException}\n");
        }
        catch { }
        FallbackToPreset("Aurora");
    }

    protected override void OnClosed(EventArgs e)
    {
        _pauseCheckTimer.Stop();
        _hwndSource?.RemoveHook(WndProc);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
            DesktopLayerHelper.UnregisterWallpaper(_monitor.DeviceName, hwnd);
        VideoPlayer.Stop();
        base.OnClosed(e);
    }
}
