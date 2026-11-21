using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OneUI.App.Native;
using OneUI.Core.Models;

namespace OneUI.App.Views;

public partial class MenuBarWindow : Window
{
    private readonly SystemStatusService _statusService;
    private readonly AppDiscoveryService? _discovery;
    private readonly WindowTracker? _tracker;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _fullscreenTimer;
    private MonitorBounds _monitor;
    private nint _lastActiveHwnd;
    private string _lastActiveProcess = string.Empty;
    private int _tickCount;
    private bool _hiddenForFullscreen;
    private bool _userEnabled = true;
    private bool _taskbarHidden = true;

    public event Action<MonitorBounds>? StartMenuRequested;
    public event Action<MonitorBounds>? SpotlightRequested;
    public event Action<MonitorBounds>? ControlCenterRequested;
    public event Action<MonitorBounds>? CalendarRequested;
    public event Action? SettingsRequested;
    public event Action? ToggleTaskbarRequested;
    public event Action? QuitRequested;

    public MenuBarWindow(
        SystemStatusService statusService,
        MonitorBounds? monitor = null,
        AppDiscoveryService? discovery = null,
        WindowTracker? tracker = null)
    {
        InitializeComponent();
        _statusService = statusService;
        _discovery = discovery;
        _tracker = tracker;
        _monitor = monitor ?? MonitorHelper.GetPrimaryMonitor();

        GlassHelper.PrepareLiquidGlass(this, roundedCorners: false, isToolWindow: true, noActivate: true);
        Loaded += (_, _) => PositionOnMonitor(_monitor);
        Closed += (_, _) => UnregisterAppBar();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();

        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _fullscreenTimer.Tick += (_, _) => CheckFullscreenState();
        _fullscreenTimer.Start();

        RefreshStatus();
    }

    public void SetUserEnabled(bool enabled, bool taskbarHidden = true)
    {
        _userEnabled = enabled;
        _taskbarHidden = taskbarHidden;
        if (!enabled)
        {
            _hiddenForFullscreen = false;
            UnregisterAppBar();
            Hide();
        }
        else if (!WindowTracker.IsMonitorFullscreen(_monitor))
        {
            Show();
            PositionAtTop();
        }
    }

    private void CheckFullscreenState()
    {
        if (!_userEnabled) return;

        bool isFs = WindowTracker.IsMonitorFullscreen(_monitor);
        if (isFs && !_hiddenForFullscreen)
        {
            _hiddenForFullscreen = true;
            if (IsVisible) Hide();
        }
        else if (!isFs && _hiddenForFullscreen)
        {
            _hiddenForFullscreen = false;
            Show();
            PositionAtTop();
        }
    }

    public void PositionOnMonitor(MonitorBounds monitor)
    {
        _monitor = monitor;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero && IsVisible)
        {
            AppBarHelper.RegisterOrUpdateTopBar(hwnd, monitor, heightDip: 28, taskbarHidden: _taskbarHidden);
        }
        else
        {
            Left = monitor.LeftDip;
            Top = monitor.TopDip;
            Width = monitor.WidthDip;
            Height = 28;
        }
    }

    public void UnregisterAppBar()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
        {
            AppBarHelper.Unregister(hwnd);
        }
    }

    public void PositionAtTop() => PositionOnMonitor(_monitor);

    public void UpdateActiveWindow(WindowInfo? info)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (info is null || string.IsNullOrWhiteSpace(info.ProcessName))
            {
                ActiveAppText.Text = "Desktop";
                ActiveWindowTitleText.Text = string.Empty;
                ActiveAppIcon.Visibility = Visibility.Collapsed;
                _lastActiveHwnd = nint.Zero;
                _lastActiveProcess = string.Empty;
                return;
            }

            _lastActiveHwnd = info.Handle;
            _lastActiveProcess = info.ProcessName;
            var name = FormatProcessName(info.ProcessName);
            ActiveAppText.Text = name.Length > 24 ? name[..24] + "…" : name;

            var rawTitle = info.Title?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(rawTitle) && !rawTitle.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                ActiveWindowTitleText.Text = rawTitle.Length > 42 ? rawTitle[..42] + "…" : rawTitle;
            }
            else
            {
                ActiveWindowTitleText.Text = string.Empty;
            }

            var icon = _discovery?.GetIcon(info.ExecutablePath);
            if (icon is not null)
            {
                ActiveAppIcon.Source = icon;
                ActiveAppIcon.Visibility = Visibility.Visible;
            }
            else
            {
                ActiveAppIcon.Visibility = Visibility.Collapsed;
            }
        });
    }

    private static string FormatProcessName(string proc) => proc.ToLowerInvariant() switch
    {
        "explorer" => "File Explorer",
        "msedge" => "Microsoft Edge",
        "chrome" => "Google Chrome",
        "firefox" => "Firefox",
        "code" => "Visual Studio Code",
        "windowsterminal" or "wt" => "Windows Terminal",
        "notepad" => "Notepad",
        _ => char.ToUpperInvariant(proc[0]) + proc[1..]
    };

    private void RefreshStatus()
    {
        ClockText.Text = DateTime.Now.ToString("ddd MMM d   h:mm tt");

        // Self-healing work area check every 4 seconds (skip while a fullscreen app is active on this monitor)
        _tickCount++;
        if (_tickCount % 4 == 0 && IsVisible && !_hiddenForFullscreen)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != nint.Zero)
                AppBarHelper.EnsureWorkAreaReserved(hwnd, _monitor, heightDip: 28, taskbarHidden: _taskbarHidden);
        }

        var net = _statusService.GetNetworkStatus();
        NetworkIcon.Text = net.IsConnected ? "\uE701" : "\uEB55";

        bool muted = _statusService.GetIsMuted();
        float vol = _statusService.GetMasterVolume();
        VolumeIcon.Text = muted || vol < 0.01f ? "\uE74F" : vol < 0.5f ? "\uE993" : "\uE767";

        var bat = _statusService.GetBatteryStatus();
        if (bat.HasBattery)
        {
            BatteryPanel.Visibility = Visibility.Visible;
            BatteryText.Text = $"{bat.Percent}%";
            BatteryFillBar.Width = Math.Clamp(17.0 * (bat.Percent / 100.0), 2.0, 17.0);
            if (bat.IsCharging)
                BatteryFillBar.Background = new SolidColorBrush(Color.FromRgb(48, 209, 88));
            else if (bat.Percent <= 20)
                BatteryFillBar.Background = new SolidColorBrush(Color.FromRgb(255, 69, 58));
            else
                BatteryFillBar.SetResourceReference(Border.BackgroundProperty, "GlassTextPrimaryBrush");
        }
        else
        {
            BatteryPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void WindowMenuButton_Click(object sender, RoutedEventArgs e)
    {
        WindowContextMenu.Items.Clear();

        if (_lastActiveHwnd != nint.Zero)
        {
            var snapLeft = new MenuItem { Header = "Snap Active Window Left Half" };
            snapLeft.Click += (_, _) => WindowTracker.SnapWindowHalf(_lastActiveHwnd, leftHalf: true);
            WindowContextMenu.Items.Add(snapLeft);

            var snapRight = new MenuItem { Header = "Snap Active Window Right Half" };
            snapRight.Click += (_, _) => WindowTracker.SnapWindowHalf(_lastActiveHwnd, leftHalf: false);
            WindowContextMenu.Items.Add(snapRight);

            var moveMon = new MenuItem { Header = "Move Active Window to Other Display" };
            moveMon.Click += (_, _) => WindowTracker.MoveWindowToOtherMonitor(_lastActiveHwnd);
            WindowContextMenu.Items.Add(moveMon);

            var pinTop = new MenuItem { Header = "Toggle Always on Top" };
            pinTop.Click += (_, _) => WindowTracker.ToggleAlwaysOnTop(_lastActiveHwnd);
            WindowContextMenu.Items.Add(pinTop);

            WindowContextMenu.Items.Add(new Separator());
        }

        // List all open windows (with active app's windows first so switching Chrome accounts or Explorer folders is 1 click!)
        var allWindows = _tracker?.GetRunningAppWindows() ?? [];
        var sameAppWindows = allWindows
            .Where(w => !string.IsNullOrEmpty(_lastActiveProcess) &&
                        w.ProcessName.Equals(_lastActiveProcess, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sameAppWindows.Count > 0)
        {
            foreach (var win in sameAppWindows)
            {
                var title = win.Title.Length > 46 ? win.Title[..46] + "…" : win.Title;
                var prefix = win.Handle == _lastActiveHwnd ? "✓  " : "    ";
                var item = new MenuItem { Header = $"{prefix}{title}" };
                var h = win.Handle;
                item.Click += (_, _) => WindowTracker.ActivateWindow(h);
                WindowContextMenu.Items.Add(item);
            }
            WindowContextMenu.Items.Add(new Separator());
        }

        var otherWindows = allWindows
            .Where(w => string.IsNullOrEmpty(_lastActiveProcess) ||
                        !w.ProcessName.Equals(_lastActiveProcess, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();

        foreach (var win in otherWindows)
        {
            var appName = FormatProcessName(win.ProcessName);
            var title = win.Title.Length > 34 ? win.Title[..34] + "…" : win.Title;
            var item = new MenuItem { Header = $"{appName} — {title}" };
            var h = win.Handle;
            item.Click += (_, _) => WindowTracker.ActivateWindow(h);
            WindowContextMenu.Items.Add(item);
        }

        WindowContextMenu.PlacementTarget = WindowMenuButton;
        WindowContextMenu.IsOpen = true;
    }

    private void SystemMenuButton_Click(object sender, RoutedEventArgs e)
    {
        StartMenuRequested?.Invoke(_monitor);
    }

    private void Spotlight_Click(object sender, RoutedEventArgs e) => SpotlightRequested?.Invoke(_monitor);
    private void ControlCenter_Click(object sender, RoutedEventArgs e) => ControlCenterRequested?.Invoke(_monitor);
    private void Clock_Click(object sender, RoutedEventArgs e) => CalendarRequested?.Invoke(_monitor);
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void ToggleTaskbar_Click(object sender, RoutedEventArgs e) => ToggleTaskbarRequested?.Invoke();
    private void Quit_Click(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();

    private void Notifications_Click(object sender, RoutedEventArgs e) => SystemStatusService.OpenNotificationCenter();
    private void ShowDesktop_Click(object sender, RoutedEventArgs e) => SystemStatusService.ShowDesktop();
    private void OpenWindowsSettings_Click(object sender, RoutedEventArgs e) => SystemStatusService.OpenWindowsSettings();
    private void OpenTaskManager_Click(object sender, RoutedEventArgs e) => SystemStatusService.OpenTaskManager();
    private void OpenExplorer_Click(object sender, RoutedEventArgs e) => AppDiscoveryService.Launch("explorer.exe");
    private void Network_Click(object sender, RoutedEventArgs e) => SystemStatusService.OpenWindowsSettings("network");
    private void Lock_Click(object sender, RoutedEventArgs e) => SystemStatusService.LockScreen();
    private void Sleep_Click(object sender, RoutedEventArgs e) => SystemStatusService.SleepComputer();

    private void CloseActive_Click(object sender, RoutedEventArgs e)
    {
        if (_lastActiveHwnd != nint.Zero)
            WindowTracker.CloseWindow(_lastActiveHwnd);
    }

    private void MinimizeActive_Click(object sender, RoutedEventArgs e)
    {
        if (_lastActiveHwnd != nint.Zero)
            WindowTracker.MinimizeWindow(_lastActiveHwnd);
    }

    private void ToggleMaximizeActive_Click(object sender, RoutedEventArgs e)
    {
        if (_lastActiveHwnd != nint.Zero)
            WindowTracker.ToggleMaximizeWindow(_lastActiveHwnd);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "OneUI — Liquid Glass Shell Overlay for Windows 11\n\n" +
            "• 100% Safe & Reversible (zero system file patching)\n" +
            "• Quick Search Shortcut: Alt + Space\n" +
            "• Emergency Restore Windows Taskbar: Ctrl + Alt + Shift + R",
            "About OneUI",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
