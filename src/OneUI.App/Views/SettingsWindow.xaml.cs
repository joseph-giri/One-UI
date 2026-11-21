using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using OneUI.App.Native;
using OneUI.Core.Models;

namespace OneUI.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _loading = true;

    public event Action? SettingsApplied;
    public event Action? ResetWidgetsRequested;
    public event Action? QuitRequested;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        GlassHelper.PrepareLiquidGlass(this, roundedCorners: true, isToolWindow: false);
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
        LoadFromSettings();
    }

    public void LoadFromSettings()
    {
        _loading = true;
        EnableDockCheck.IsChecked = _settings.EnableDock;
        EnableMenuBarCheck.IsChecked = _settings.EnableMenuBar;
        HideTaskbarCheck.IsChecked = _settings.HideWindowsTaskbar;
        LaunchAtStartupCheck.IsChecked = _settings.LaunchAtStartup;
        TintOpacitySlider.Value = _settings.GlassTintOpacity;
        DockSizeSlider.Value = _settings.DockIconSize;
        DockMagSlider.Value = _settings.DockMagnificationScale;
        DockAutoHideCheck.IsChecked = _settings.DockAutoHide;

        EnableWallpaperCheck.IsChecked = _settings.EnableLiveWallpaper;
        PauseFullscreenCheck.IsChecked = _settings.PauseWallpaperOnFullscreen;
        WallpaperVideoPathText.Text = string.IsNullOrWhiteSpace(_settings.WallpaperVideoPath)
            ? "No video selected"
            : _settings.WallpaperVideoPath;

        EnableClockWidgetCheck.IsChecked = _settings.EnableClockWidget;
        EnableSystemWidgetCheck.IsChecked = _settings.EnableSystemWidget;
        EnableNotesWidgetCheck.IsChecked = _settings.EnableNotesWidget;
        EnableMediaWidgetCheck.IsChecked = _settings.EnableMediaWidget;
        EnableWeatherWidgetCheck.IsChecked = _settings.EnableWeatherWidget;
        EnableCalendarWidgetCheck.IsChecked = _settings.EnableCalendarWidget;
        EnableReminderWidgetCheck.IsChecked = _settings.EnableReminderWidget;
        WeatherCityBox.Text = _settings.WeatherCity ?? string.Empty;
        WeatherFahrenheitCheck.IsChecked = _settings.WeatherUseFahrenheit;

        _settings.ClockWidget ??= new ClockWidgetConfig();
        ClockSizeSlider.Value = Math.Clamp(_settings.ClockWidget.FontSize, 28, 88);
        Clock24hCheck.IsChecked = _settings.ClockWidget.Use24Hour;
        ClockSecondsCheck.IsChecked = _settings.ClockWidget.ShowSeconds;
        ClockDateCheck.IsChecked = _settings.ClockWidget.ShowDate;
        ClockShadowCheck.IsChecked = _settings.ClockWidget.EnableShadow;
        ClockShadowBlurSlider.Value = Math.Clamp(_settings.ClockWidget.ShadowBlurRadius, 0, 36);
        ClockShadowDepthSlider.Value = Math.Clamp(_settings.ClockWidget.ShadowDepth, 0, 14);
        ClockShadowOpacitySlider.Value = Math.Clamp(_settings.ClockWidget.ShadowOpacity, 0, 1);
        _loading = false;
    }

    public void OpenWidgetsTab() => SelectTab("Widgets");

    private void SelectTab(string tab)
    {
        NavGeneralBtn.Tag = tab == "General" ? "Selected" : null;
        NavWallpaperBtn.Tag = tab == "Wallpaper" ? "Selected" : null;
        NavWidgetsBtn.Tag = tab == "Widgets" ? "Selected" : null;

        GeneralPage.Visibility = tab == "General" ? Visibility.Visible : Visibility.Collapsed;
        WallpaperPage.Visibility = tab == "Wallpaper" ? Visibility.Visible : Visibility.Collapsed;
        WidgetsPage.Visibility = tab == "Widgets" ? Visibility.Visible : Visibility.Collapsed;

        PageTitleText.Text = tab switch
        {
            "Wallpaper" => "Live Wallpaper",
            "Widgets" => "Desktop Widgets",
            _ => "General & Liquid Glass"
        };
    }

    private void NavGeneral_Click(object sender, RoutedEventArgs e) => SelectTab("General");
    private void NavWallpaper_Click(object sender, RoutedEventArgs e) => SelectTab("Wallpaper");
    private void NavWidgets_Click(object sender, RoutedEventArgs e) => SelectTab("Widgets");

    private void AnySetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        CommitChanges();
    }

    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        CommitChanges();
    }

    private void DarkTheme_Click(object sender, RoutedEventArgs e)
    {
        _settings.GlassTheme = "Dark";
        SettingsApplied?.Invoke();
    }

    private void LightTheme_Click(object sender, RoutedEventArgs e)
    {
        _settings.GlassTheme = "Light";
        SettingsApplied?.Invoke();
    }

    private void PresetAurora_Click(object sender, RoutedEventArgs e) => ApplyWallpaperPreset("Aurora");
    private void PresetLiquidMesh_Click(object sender, RoutedEventArgs e) => ApplyWallpaperPreset("LiquidMesh");
    private void PresetNebula_Click(object sender, RoutedEventArgs e) => ApplyWallpaperPreset("Nebula");

    private void ApplyWallpaperPreset(string preset)
    {
        _settings.WallpaperMode = preset;
        _settings.EnableLiveWallpaper = true;
        EnableWallpaperCheck.IsChecked = true;
        SettingsApplied?.Invoke();
    }

    private void BrowseVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a Live Wallpaper Video",
            Filter = "Video Files (*.mp4;*.mov;*.wmv;*.mkv;*.avi)|*.mp4;*.mov;*.wmv;*.mkv;*.avi|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _settings.WallpaperVideoPath = dialog.FileName;
            _settings.WallpaperMode = "Video";
            _settings.EnableLiveWallpaper = true;
            WallpaperVideoPathText.Text = dialog.FileName;
            EnableWallpaperCheck.IsChecked = true;
            SettingsApplied?.Invoke();
        }
    }

    private void ResetWidgets_Click(object sender, RoutedEventArgs e)
    {
        _settings.WidgetPositions.Clear();
        ResetWidgetsRequested?.Invoke();
        SettingsApplied?.Invoke();
    }

    private void ClockStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string style })
        {
            _settings.ClockWidget.Style = style;
            SettingsApplied?.Invoke();
        }
    }

    private void ClockBg_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string bg })
        {
            _settings.ClockWidget.BackgroundStyle = bg;
            SettingsApplied?.Invoke();
        }
    }

    private void ClockFont_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string font })
        {
            _settings.ClockWidget.FontFamily = font;
            SettingsApplied?.Invoke();
        }
    }

    private void ClockWeight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string w })
        {
            _settings.ClockWidget.FontWeight = w;
            SettingsApplied?.Invoke();
        }
    }

    private void ClockColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string hex })
        {
            _settings.ClockWidget.TextColorHex = hex;
            SettingsApplied?.Invoke();
        }
    }

    private void ClockShadowColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string hex })
        {
            _settings.ClockWidget.ShadowColorHex = hex;
            _settings.ClockWidget.EnableShadow = true;
            ClockShadowCheck.IsChecked = true;
            SettingsApplied?.Invoke();
        }
    }

    private void ClockAlign_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string align })
        {
            _settings.ClockWidget.Alignment = align;
            SettingsApplied?.Invoke();
        }
    }

    private void ClockDateFmt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string fmt })
        {
            _settings.ClockWidget.DateFormat = fmt;
            SettingsApplied?.Invoke();
        }
    }

    private void ApplyWeatherCity_Click(object sender, RoutedEventArgs e)
    {
        _settings.WeatherCity = WeatherCityBox.Text.Trim();
        SettingsApplied?.Invoke();
    }

    private void CommitChanges()
    {
        if (_loading || !IsInitialized) return;
        _settings.EnableDock = EnableDockCheck.IsChecked == true;
        _settings.EnableMenuBar = EnableMenuBarCheck.IsChecked == true;
        _settings.HideWindowsTaskbar = HideTaskbarCheck.IsChecked == true;
        _settings.LaunchAtStartup = LaunchAtStartupCheck.IsChecked == true;
        SystemStatusService.SetLaunchAtStartup(_settings.LaunchAtStartup);
        _settings.GlassTintOpacity = Math.Round(TintOpacitySlider.Value, 2);
        _settings.DockIconSize = Math.Round(DockSizeSlider.Value);
        _settings.DockMagnificationScale = Math.Round(DockMagSlider.Value, 2);
        _settings.DockAutoHide = DockAutoHideCheck.IsChecked == true;

        _settings.EnableLiveWallpaper = EnableWallpaperCheck.IsChecked == true;
        _settings.PauseWallpaperOnFullscreen = PauseFullscreenCheck.IsChecked == true;

        _settings.EnableClockWidget = EnableClockWidgetCheck.IsChecked == true;
        _settings.EnableSystemWidget = EnableSystemWidgetCheck.IsChecked == true;
        _settings.EnableNotesWidget = EnableNotesWidgetCheck.IsChecked == true;
        _settings.EnableMediaWidget = EnableMediaWidgetCheck.IsChecked == true;
        _settings.EnableWeatherWidget = EnableWeatherWidgetCheck.IsChecked == true;
        _settings.EnableCalendarWidget = EnableCalendarWidgetCheck.IsChecked == true;
        _settings.EnableReminderWidget = EnableReminderWidgetCheck.IsChecked == true;
        _settings.WeatherCity = WeatherCityBox.Text.Trim();
        _settings.WeatherUseFahrenheit = WeatherFahrenheitCheck.IsChecked == true;

        _settings.ClockWidget.FontSize = Math.Round(ClockSizeSlider.Value);
        _settings.ClockWidget.Use24Hour = Clock24hCheck.IsChecked == true;
        _settings.ClockWidget.ShowSeconds = ClockSecondsCheck.IsChecked == true;
        _settings.ClockWidget.ShowDate = ClockDateCheck.IsChecked == true;
        _settings.ClockWidget.EnableShadow = ClockShadowCheck.IsChecked == true;
        _settings.ClockWidget.ShadowBlurRadius = Math.Round(ClockShadowBlurSlider.Value);
        _settings.ClockWidget.ShadowDepth = Math.Round(ClockShadowDepthSlider.Value);
        _settings.ClockWidget.ShadowOpacity = Math.Round(ClockShadowOpacitySlider.Value, 2);

        SettingsApplied?.Invoke();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void RestoreAndQuit_Click(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
}
