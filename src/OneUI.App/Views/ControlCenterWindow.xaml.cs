using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OneUI.App.Native;
using OneUI.App.Theme;
using OneUI.Core.Models;

namespace OneUI.App.Views;

public partial class ControlCenterWindow : Window
{
    private readonly SystemStatusService _statusService;
    private readonly MediaSessionService _mediaService;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _telemetryTimer;
    private bool _suppressEvents = true;

    public event Action? ThemeToggleRequested;
    public event Action? TaskbarToggleRequested;
    public event Action? DockAutoHideToggleRequested;
    public event Action? GlassOpacityChanged;
    public event Action? SettingsRequested;

    public ControlCenterWindow(SystemStatusService statusService, MediaSessionService mediaService, AppSettings settings)
    {
        InitializeComponent();
        _statusService = statusService;
        _mediaService = mediaService;
        _settings = settings;
        GlassHelper.PrepareLiquidGlass(this, roundedCorners: true, isToolWindow: true);

        _mediaService.NowPlayingChanged += info => Dispatcher.BeginInvoke(() => UpdateNowPlaying(info));
        UpdateNowPlaying(_mediaService.Current);

        _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _telemetryTimer.Tick += (_, _) => RefreshTelemetry();
    }

    private void UpdateNowPlaying(NowPlayingInfo info)
    {
        MediaTitleText.Text = info.Title;
        MediaArtistText.Text = info.Artist;
        MediaPlayPauseIcon.Text = info.IsPlaying ? "\uE769" : "\uE768";
        MediaArtImage.Source = info.Thumbnail;
        MediaFallbackIcon.Visibility = info.Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ToggleOpen(MonitorBounds? monitor = null)
    {
        if (IsVisible)
        {
            _telemetryTimer.Stop();
            Hide();
            return;
        }

        var mon = monitor ?? MonitorHelper.GetMonitorAtCursor();
        Left = mon.RightDip - Width - 12;
        Top = mon.TopDip + 34;
        RefreshState();
        RefreshTelemetry();
        Show();
        Activate();
        _telemetryTimer.Start();
        AnimationHelper.AnimatePopIn(RootCard, fromScale: 0.94, fromOffsetY: -8, durationMs: 190);
    }

    public void RefreshState()
    {
        var net = _statusService.GetNetworkStatus();
        NetworkStatusLabel.Text = net.Label;
        NetworkBadge.Background = net.IsConnected
            ? new SolidColorBrush(Color.FromRgb(10, 132, 255))
            : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));

        bool isDark = !string.Equals(_settings.GlassTheme, "Light", StringComparison.OrdinalIgnoreCase);
        ThemeStatusLabel.Text = isDark ? "Dark Glass" : "Light Glass";
        ThemeIcon.Text = isDark ? "\uE708" : "\uE706";

        TaskbarDot.Background = _settings.HideWindowsTaskbar
            ? new SolidColorBrush(Color.FromRgb(48, 209, 88))
            : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));

        AutoHideDot.Background = _settings.DockAutoHide
            ? new SolidColorBrush(Color.FromRgb(10, 132, 255))
            : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));

        _suppressEvents = true;
        GlassSlider.Value = Math.Round(_settings.GlassTintOpacity * 100);
        GlassPercentText.Text = $"{(int)GlassSlider.Value}%";

        float vol = _statusService.GetMasterVolume();
        bool muted = _statusService.GetIsMuted();
        VolumeSlider.Value = Math.Round(vol * 100);
        VolumePercentText.Text = muted ? "Muted" : $"{(int)VolumeSlider.Value}%";
        _suppressEvents = false;
    }

    private void RefreshTelemetry()
    {
        var t = _statusService.GetTelemetry();
        CpuText.Text = $"{t.CpuPercent}%";
        RamText.Text = $"{t.UsedRamGb:0.0}/{t.TotalRamGb:0} GB";

        const double maxBarWidth = 135.0;
        CpuBar.Width = Math.Clamp(maxBarWidth * (t.CpuPercent / 100.0), 6.0, maxBarWidth);
        RamBar.Width = Math.Clamp(maxBarWidth * (t.RamPercent / 100.0), 6.0, maxBarWidth);
    }

    private void GlassSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents || !IsInitialized) return;
        _settings.GlassTintOpacity = Math.Round(e.NewValue / 100.0, 2);
        GlassPercentText.Text = $"{(int)Math.Round(e.NewValue)}%";
        GlassOpacityChanged?.Invoke();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents || !IsInitialized) return;
        float scalar = (float)(e.NewValue / 100.0);
        _statusService.SetMute(false);
        _statusService.SetMasterVolume(scalar);
        VolumePercentText.Text = $"{(int)Math.Round(e.NewValue)}%";
    }

    private void MuteToggle_Click(object sender, RoutedEventArgs e)
    {
        bool muted = !_statusService.GetIsMuted();
        _statusService.SetMute(muted);
        VolumePercentText.Text = muted ? "Muted" : $"{(int)VolumeSlider.Value}%";
    }

    private void MediaPrev_Click(object sender, RoutedEventArgs e) => _mediaService.SkipPrevious();
    private void MediaPlayPause_Click(object sender, RoutedEventArgs e) => _mediaService.TogglePlayPause();
    private void MediaNext_Click(object sender, RoutedEventArgs e) => _mediaService.SkipNext();

    private void NetworkTile_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SystemStatusService.OpenWindowsSettings("network");
    }

    private void BluetoothTile_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SystemStatusService.OpenWindowsSettings("bluetooth");
    }

    private void ThemeTile_Click(object sender, RoutedEventArgs e)
    {
        ThemeToggleRequested?.Invoke();
        RefreshState();
    }

    private void TaskbarTile_Click(object sender, RoutedEventArgs e)
    {
        TaskbarToggleRequested?.Invoke();
        RefreshState();
    }

    private void DockAutoHideTile_Click(object sender, RoutedEventArgs e)
    {
        DockAutoHideToggleRequested?.Invoke();
        RefreshState();
    }

    private void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SystemStatusService.OpenSnippingTool();
    }

    private void Preferences_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SettingsRequested?.Invoke();
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SystemStatusService.LockScreen();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        _telemetryTimer.Stop();
        if (IsVisible) Hide();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _telemetryTimer.Stop();
            Hide();
        }
    }
}
