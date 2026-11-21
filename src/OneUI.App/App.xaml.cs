using System.Drawing;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using OneUI.App.Native;
using OneUI.App.Theme;
using OneUI.App.Views;
using OneUI.Core.Models;
using OneUI.Core.Services;
using WinForms = System.Windows.Forms;

namespace OneUI.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private SettingsService _settingsService = null!;
    private AppSettings _settings = null!;
    private TaskbarManager _taskbar = null!;
    private WindowTracker _tracker = null!;
    private GlobalHotkeyService _hotkeys = null!;
    private AppDiscoveryService _discovery = null!;
    private SystemStatusService _status = null!;
    private MediaSessionService _media = null!;
    private WeatherService _weather = null!;
    private string _lastWeatherCity = string.Empty;
    private bool _lastWeatherFahrenheit;
    private WinForms.NotifyIcon? _trayIcon;

    private readonly List<MenuBarWindow> _menuBars = [];
    private readonly List<LiveWallpaperWindow> _wallpapers = [];
    private readonly Dictionary<DesktopWidgetKind, DesktopWidgetWindow> _widgets = new();
    private ControlCenterWindow? _controlCenter;
    private CalendarFlyoutWindow? _calendarFlyout;
    private StartMenuWindow? _startMenu;
    private DockWindow? _dock;
    private SpotlightWindow? _spotlight;
    private SettingsWindow? _settingsWindow;
    private System.Windows.Threading.DispatcherTimer? _autoReloadTimer;
    private readonly DateTime _startupTimeUtc = DateTime.UtcNow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Terminate any older running OneUI.App instances so two docks/bars never stack
        int currentPid = Environment.ProcessId;
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("OneUI.App"))
        {
            if (proc.Id != currentPid)
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(1500);
                }
                catch { }
            }
            proc.Dispose();
        }

        // Clean up any leftover .old files from hot-reload builds
        try
        {
            foreach (var oldFile in Directory.GetFiles(AppContext.BaseDirectory, "*.old"))
            {
                try { File.Delete(oldFile); } catch { }
            }
        }
        catch { }

        _singleInstanceMutex = new Mutex(true, "Global\\OneUI_LiquidGlass_SingleInstance", out _);

        // Crash-safety: always restore the native Windows taskbar and desktop work area
        DispatcherUnhandledException += (_, args) =>
        {
            AppBarHelper.UnregisterAll();
            _taskbar?.RestoreTaskbar();
            LogCrash(args.Exception);
            args.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            AppBarHelper.UnregisterAll();
            _taskbar?.RestoreTaskbar();
            if (args.ExceptionObject is Exception ex) LogCrash(ex);
        };
        SessionEnding += (_, _) =>
        {
            AppBarHelper.UnregisterAll();
            _taskbar?.RestoreTaskbar();
        };

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _taskbar = new TaskbarManager();
        _tracker = new WindowTracker();
        _hotkeys = new GlobalHotkeyService();
        _discovery = new AppDiscoveryService();
        _status = new SystemStatusService();
        _media = new MediaSessionService();
        _weather = new WeatherService();
        _lastWeatherCity = _settings.WeatherCity ?? string.Empty;
        _lastWeatherFahrenheit = _settings.WeatherUseFahrenheit;
        _ = _media.StartAsync();

        // Index Start Menu apps off the UI thread so launch is instant
        Task.Run(() => _discovery.RefreshIndex());

        SetupTrayIcon();
        SetupWindows();
        WireEvents();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        ThemeManager.Apply(_settings);
        ApplySettingsLive();

        _tracker.Start();
        _hotkeys.Start();
        StartUpdateWatcher();
    }

    private void StartUpdateWatcher()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "OneUI.App.exe");

        _autoReloadTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autoReloadTimer.Tick += (_, _) =>
        {
            try
            {
                if (File.Exists(exePath) && File.GetLastWriteTimeUtc(exePath) > _startupTimeUtc.AddSeconds(2))
                {
                    _autoReloadTimer.Stop();
                    AppBarHelper.UnregisterAll();
                    _taskbar.RestoreTaskbar();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true
                    });
                    Shutdown();
                }
            }
            catch { }
        };
        _autoReloadTimer.Start();
    }

    private void SetupTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Quick Search (Alt+Space)", null, (_, _) => _spotlight?.ToggleOpen());
        menu.Items.Add("OneUI Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add("Toggle Windows Taskbar", null, (_, _) => ToggleTaskbar());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Restore Native Windows UI & Quit", null, (_, _) => SafeQuit());

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "OneUI — Liquid Glass (Alt+Space to Search)",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();
    }

    private void SetupWindows()
    {
        _controlCenter = new ControlCenterWindow(_status, _media, _settings);
        _calendarFlyout = new CalendarFlyoutWindow();
        _startMenu = new StartMenuWindow(_discovery);
        _dock = new DockWindow(_settings, _discovery, _tracker);
        _spotlight = new SpotlightWindow(_discovery, _tracker);
        _settingsWindow = new SettingsWindow(_settings);
        SetupWidgets();
        RebuildMenuBars();
    }

    private void SetupWidgets()
    {
        var primary = MonitorHelper.GetPrimaryMonitor();
        double rightCol = primary.RightDip - 270;
        double leftCol = primary.LeftDip + 24;

        CreateWidget(DesktopWidgetKind.Clock, rightCol, primary.TopDip + 48);
        CreateWidget(DesktopWidgetKind.SystemMonitor, rightCol, primary.TopDip + 190);
        CreateWidget(DesktopWidgetKind.QuickNotes, rightCol, primary.TopDip + 350);
        CreateWidget(DesktopWidgetKind.Media, rightCol - 260, primary.TopDip + 48);
        CreateWidget(DesktopWidgetKind.Weather, leftCol, primary.TopDip + 48);
        CreateWidget(DesktopWidgetKind.Calendar, leftCol, primary.TopDip + 248);
        CreateWidget(DesktopWidgetKind.Reminder, leftCol, primary.TopDip + 508);
    }

    private void CreateWidget(DesktopWidgetKind kind, double defaultLeft, double defaultTop)
    {
        var w = new DesktopWidgetWindow(kind, _settings, _status, _media, _weather, defaultLeft, defaultTop);
        w.SettingsChanged += () => _settingsService.Save(_settings);
        w.HideRequested += widgetKind =>
        {
            switch (widgetKind)
            {
                case DesktopWidgetKind.Clock: _settings.EnableClockWidget = false; break;
                case DesktopWidgetKind.SystemMonitor: _settings.EnableSystemWidget = false; break;
                case DesktopWidgetKind.QuickNotes: _settings.EnableNotesWidget = false; break;
                case DesktopWidgetKind.Media: _settings.EnableMediaWidget = false; break;
                case DesktopWidgetKind.Weather: _settings.EnableWeatherWidget = false; break;
                case DesktopWidgetKind.Calendar: _settings.EnableCalendarWidget = false; break;
                case DesktopWidgetKind.Reminder: _settings.EnableReminderWidget = false; break;
            }
            SaveAndApply();
        };
        w.CustomizeClockRequested += () =>
        {
            ShowSettings();
            _settingsWindow?.OpenWidgetsTab();
        };
        w.ReminderDue += item =>
        {
            try
            {
                _trayIcon?.ShowBalloonTip(
                    8000,
                    "OneUI Reminder",
                    item.Text,
                    WinForms.ToolTipIcon.Info);
            }
            catch { }
        };
        _widgets[kind] = w;
    }

    private void SyncLiveWallpapers()
    {
        if (!_settings.EnableLiveWallpaper)
        {
            foreach (var wp in _wallpapers) wp.Close();
            _wallpapers.Clear();
            return;
        }

        var monitors = MonitorHelper.GetAllMonitors();
        if (_wallpapers.Count != monitors.Count)
        {
            foreach (var wp in _wallpapers) wp.Close();
            _wallpapers.Clear();

            foreach (var mon in monitors)
            {
                var wp = new LiveWallpaperWindow(mon);
                wp.Show();
                _wallpapers.Add(wp);
            }
        }

        foreach (var wp in _wallpapers)
        {
            wp.ApplySettings(_settings);
        }
    }

    private void SyncWidgets()
    {
        if (_widgets.TryGetValue(DesktopWidgetKind.Clock, out var clockWidget))
            clockWidget.ApplyClockCustomization();

        ToggleWidget(DesktopWidgetKind.Clock, _settings.EnableClockWidget);
        ToggleWidget(DesktopWidgetKind.SystemMonitor, _settings.EnableSystemWidget);
        ToggleWidget(DesktopWidgetKind.QuickNotes, _settings.EnableNotesWidget);
        ToggleWidget(DesktopWidgetKind.Media, _settings.EnableMediaWidget);
        ToggleWidget(DesktopWidgetKind.Weather, _settings.EnableWeatherWidget);
        ToggleWidget(DesktopWidgetKind.Calendar, _settings.EnableCalendarWidget);
        ToggleWidget(DesktopWidgetKind.Reminder, _settings.EnableReminderWidget);

        if (_settings.EnableWeatherWidget && _widgets.TryGetValue(DesktopWidgetKind.Weather, out var weatherWidget))
        {
            string city = _settings.WeatherCity ?? string.Empty;
            bool cityChanged = !string.Equals(city, _lastWeatherCity, StringComparison.OrdinalIgnoreCase);
            bool unitChanged = _settings.WeatherUseFahrenheit != _lastWeatherFahrenheit;
            _lastWeatherCity = city;
            _lastWeatherFahrenheit = _settings.WeatherUseFahrenheit;

            if (cityChanged)
                _ = weatherWidget.RefreshWeatherAsync(force: true);
            else if (unitChanged)
                _ = weatherWidget.RefreshWeatherAsync(force: false);
        }
    }

    private void ToggleWidget(DesktopWidgetKind kind, bool enabled)
    {
        if (!_widgets.TryGetValue(kind, out var w)) return;
        if (enabled)
        {
            if (!w.IsVisible) w.Show();
            w.ReattachToDesktopLayer();
        }
        else
        {
            if (w.IsVisible) w.Hide();
        }
    }

    private void RebuildMenuBars()
    {
        foreach (var bar in _menuBars)
        {
            bar.Close();
        }
        _menuBars.Clear();

        foreach (var monitor in MonitorHelper.GetAllMonitors())
        {
            var bar = new MenuBarWindow(_status, monitor, _discovery, _tracker);
            bar.StartMenuRequested += mon => _startMenu?.ToggleOpen(mon, fromTopLeft: true);
            bar.SpotlightRequested += mon => _spotlight?.ToggleOpen(mon);
            bar.ControlCenterRequested += mon => _controlCenter?.ToggleOpen(mon);
            bar.CalendarRequested += mon => _calendarFlyout?.ToggleOpen(mon);
            bar.SettingsRequested += ShowSettings;
            bar.ToggleTaskbarRequested += ToggleTaskbar;
            bar.QuitRequested += SafeQuit;
            _menuBars.Add(bar);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RebuildMenuBars();
            _dock?.RefreshMonitors();
            ApplySettingsLive();
        });
    }

    private void WireEvents()
    {
        _hotkeys.SpotlightPressed += () => Dispatcher.BeginInvoke(() =>
        {
            if (_settings.EnableSpotlight)
                _spotlight?.ToggleOpen();
        });

        _hotkeys.EmergencyRestorePressed += () => Dispatcher.BeginInvoke(() =>
        {
            _settings.HideWindowsTaskbar = false;
            _taskbar.RestoreTaskbar();
            _settingsService.Save(_settings);
            ShowSettings();
        });

        _hotkeys.QuitRequested += () => Dispatcher.BeginInvoke(SafeQuit);

        _tracker.ForegroundWindowChanged += info =>
        {
            foreach (var bar in _menuBars)
                bar.UpdateActiveWindow(info);
        };

        _tracker.RunningWindowsChanged += () => _dock?.RebuildDock();

        if (_controlCenter is not null)
        {
            _controlCenter.ThemeToggleRequested += () =>
            {
                _settings.GlassTheme = string.Equals(_settings.GlassTheme, "Dark", StringComparison.OrdinalIgnoreCase)
                    ? "Light"
                    : "Dark";
                SaveAndApply();
            };
            _controlCenter.TaskbarToggleRequested += ToggleTaskbar;
            _controlCenter.DockAutoHideToggleRequested += () =>
            {
                _settings.DockAutoHide = !_settings.DockAutoHide;
                SaveAndApply();
            };
            _controlCenter.GlassOpacityChanged += SaveAndApply;
            _controlCenter.SettingsRequested += ShowSettings;
        }

        if (_dock is not null)
        {
            _dock.SettingsChanged += () => _settingsService.Save(_settings);
            _dock.StartMenuRequested += mon => _startMenu?.ToggleOpen(mon);
        }

        if (_spotlight is not null)
        {
            _spotlight.OpenSettingsRequested += ShowSettings;
            _spotlight.RestoreWindowsRequested += () =>
            {
                _settings.HideWindowsTaskbar = false;
                SaveAndApply();
            };
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.SettingsApplied += SaveAndApply;
            _settingsWindow.ResetWidgetsRequested += () =>
            {
                foreach (var w in _widgets.Values) w.ApplySavedOrDefaultPosition();
            };
            _settingsWindow.QuitRequested += SafeQuit;
        }
    }

    private void ToggleTaskbar()
    {
        _settings.HideWindowsTaskbar = !_settings.HideWindowsTaskbar;
        SaveAndApply();
    }

    private void SaveAndApply()
    {
        _settingsService.Save(_settings);
        ApplySettingsLive();
    }

    private void ApplySettingsLive()
    {
        ThemeManager.Apply(_settings);
        bool hideTaskbar = _settings.HideWindowsTaskbar && _settings.EnableDock;
        _taskbar.SetTaskbarHidden(hideTaskbar);

        foreach (var bar in _menuBars)
        {
            bar.SetUserEnabled(_settings.EnableMenuBar, taskbarHidden: hideTaskbar);
        }

        if (_dock is not null)
        {
            if (_settings.EnableDock) { _dock.Show(); _dock.RebuildDock(); }
            else _dock.Hide();
        }

        SyncLiveWallpapers();
        SyncWidgets();

        // Re-verify widget Z-order right after Live Wallpaper HWNDs finish initializing
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
        {
            foreach (var w in _widgets.Values)
            {
                if (w.IsVisible) w.ReattachToDesktopLayer();
            }
        });
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null) return;
        _settingsWindow.LoadFromSettings();
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void SafeQuit()
    {
        _taskbar.RestoreTaskbar();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var wp in _wallpapers) wp.Close();
        _wallpapers.Clear();
        AppBarHelper.UnregisterAll();
        _taskbar?.Dispose();
        _tracker?.Dispose();
        _hotkeys?.Dispose();
        _media?.Dispose();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OneUI");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:O}]\n{ex}\n\n");
        }
        catch { }
    }
}
