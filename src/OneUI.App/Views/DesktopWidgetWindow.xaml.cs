using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OneUI.App.Native;
using OneUI.Core.Models;

namespace OneUI.App.Views;

public enum DesktopWidgetKind
{
    Clock,
    SystemMonitor,
    QuickNotes,
    Media,
    Weather,
    Calendar,
    Reminder
}

public partial class DesktopWidgetWindow : Window
{
    private readonly DesktopWidgetKind _kind;
    private readonly AppSettings _settings;
    private readonly SystemStatusService _status;
    private readonly MediaSessionService _media;
    private readonly WeatherService _weather;
    private readonly DispatcherTimer _timer;
    private readonly double _defaultLeft;
    private readonly double _defaultTop;
    private bool _suppressNoteChange;
    private DateTime _calendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _lastCalendarToday = DateTime.Today;
    private DateTime _lastWeatherFetchUtc = DateTime.MinValue;
    private WeatherSnapshot? _lastWeatherSnapshot;
    private bool _weatherFetching;

    public event Action? SettingsChanged;
    public event Action<DesktopWidgetKind>? HideRequested;
    public event Action? CustomizeClockRequested;

    public string WidgetKey => _kind.ToString();
    public bool IsTransparentSurface =>
        _kind == DesktopWidgetKind.Clock &&
        string.Equals(_settings.ClockWidget?.BackgroundStyle, "Transparent", StringComparison.OrdinalIgnoreCase);

    public DesktopWidgetWindow(
        DesktopWidgetKind kind,
        AppSettings settings,
        SystemStatusService status,
        MediaSessionService media,
        WeatherService weather,
        double defaultLeft,
        double defaultTop)
    {
        InitializeComponent();
        _kind = kind;
        _settings = settings;
        _status = status;
        _media = media;
        _weather = weather;
        _defaultLeft = defaultLeft;
        _defaultTop = defaultTop;

        bool interactiveTyping = kind is DesktopWidgetKind.QuickNotes or DesktopWidgetKind.Reminder;
        GlassHelper.PrepareLiquidGlass(this, roundedCorners: true, isToolWindow: true, noActivate: !interactiveTyping);

        ClockPanel.Visibility = kind == DesktopWidgetKind.Clock ? Visibility.Visible : Visibility.Collapsed;
        SystemPanel.Visibility = kind == DesktopWidgetKind.SystemMonitor ? Visibility.Visible : Visibility.Collapsed;
        NotesPanel.Visibility = kind == DesktopWidgetKind.QuickNotes ? Visibility.Visible : Visibility.Collapsed;
        MediaPanel.Visibility = kind == DesktopWidgetKind.Media ? Visibility.Visible : Visibility.Collapsed;
        WeatherPanel.Visibility = kind == DesktopWidgetKind.Weather ? Visibility.Visible : Visibility.Collapsed;
        CalendarPanel.Visibility = kind == DesktopWidgetKind.Calendar ? Visibility.Visible : Visibility.Collapsed;
        ReminderPanel.Visibility = kind == DesktopWidgetKind.Reminder ? Visibility.Visible : Visibility.Collapsed;

        if (kind == DesktopWidgetKind.Clock)
        {
            CustomizeClockMenuItem.Visibility = Visibility.Visible;
            ApplyClockCustomization();
        }

        if (kind == DesktopWidgetKind.Weather)
        {
            ConfigureWeatherMenuItem.Visibility = Visibility.Visible;
        }

        if (kind == DesktopWidgetKind.Calendar)
        {
            BuildCalendarWidget();
        }

        if (kind == DesktopWidgetKind.Reminder)
        {
            RebuildRemindersList();
        }

        if (kind == DesktopWidgetKind.Media)
        {
            _media.NowPlayingChanged += info => Dispatcher.BeginInvoke(() => UpdateNowPlaying(info));
            UpdateNowPlaying(_media.Current);
        }

        if (kind == DesktopWidgetKind.QuickNotes)
        {
            _suppressNoteChange = true;
            NotesBox.Text = _settings.DesktopNoteText;
            _suppressNoteChange = false;
        }

        ApplySavedOrDefaultPosition();

        Loaded += (_, _) =>
        {
            if (_kind == DesktopWidgetKind.Clock)
                ApplyClockCustomization();
            if (_kind == DesktopWidgetKind.Weather)
                _ = RefreshWeatherAsync();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != nint.Zero)
                DesktopLayerHelper.AttachWidgetToDesktop(hwnd);
        };
        Activated += (_, _) =>
        {
            if (_kind is not (DesktopWidgetKind.QuickNotes or DesktopWidgetKind.Reminder))
                KeepOnDesktopBottom();
        };

        _timer = new DispatcherTimer
        {
            Interval = kind switch
            {
                DesktopWidgetKind.Clock => TimeSpan.FromSeconds(1),
                DesktopWidgetKind.SystemMonitor => TimeSpan.FromSeconds(2),
                DesktopWidgetKind.Weather => TimeSpan.FromMinutes(1),
                DesktopWidgetKind.Calendar => TimeSpan.FromMinutes(1),
                _ => TimeSpan.FromSeconds(30)
            }
        };
        _timer.Tick += (_, _) => RefreshData();
        _timer.Start();
        RefreshData();
    }

    public void ApplySavedOrDefaultPosition()
    {
        if (_settings.WidgetPositions.TryGetValue(WidgetKey, out var pos))
        {
            Left = pos.Left;
            Top = pos.Top;
        }
        else
        {
            Left = _defaultLeft;
            Top = _defaultTop;
        }
    }

    public void ReattachToDesktopLayer()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
            DesktopLayerHelper.AttachWidgetToDesktop(hwnd);
    }

    private void KeepOnDesktopBottom()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
            DesktopLayerHelper.SendToDesktopBottom(hwnd);
    }

    public void ApplyClockCustomization()
    {
        if (_kind != DesktopWidgetKind.Clock) return;
        var cfg = _settings.ClockWidget ?? new ClockWidgetConfig();

        // Style (Digital / Stacked / Analog)
        DigitalClockFace.Visibility = cfg.Style == "Digital" ? Visibility.Visible : Visibility.Collapsed;
        StackedClockFace.Visibility = cfg.Style == "Stacked" ? Visibility.Visible : Visibility.Collapsed;
        AnalogClockFace.Visibility = cfg.Style == "Analog" ? Visibility.Visible : Visibility.Collapsed;

        // Font Family, Weight, Size
        var fontFam = new FontFamily($"{cfg.FontFamily}, Segoe UI Variable Display, Segoe UI");
        var weight = cfg.FontWeight switch
        {
            "Normal" => FontWeights.Normal,
            "SemiBold" => FontWeights.SemiBold,
            "Bold" => FontWeights.Bold,
            _ => FontWeights.Light
        };
        double size = Math.Clamp(cfg.FontSize, 26, 92);

        ClockTimeText.FontFamily = fontFam;
        ClockTimeText.FontWeight = weight;
        ClockTimeText.FontSize = size;
        ClockSecondsInlineText.FontFamily = fontFam;
        ClockSecondsInlineText.FontSize = Math.Max(14, size * 0.42);
        ClockAmPmText.FontSize = Math.Max(11, size * 0.25);

        StackedHourText.FontFamily = fontFam;
        StackedHourText.FontWeight = weight;
        StackedHourText.FontSize = Math.Clamp(size * 1.2, 36, 96);
        StackedHourText.LineHeight = StackedHourText.FontSize * 0.96;
        StackedMinuteText.FontFamily = fontFam;
        StackedMinuteText.FontWeight = weight;
        StackedMinuteText.FontSize = StackedHourText.FontSize;
        StackedMinuteText.LineHeight = StackedHourText.LineHeight;

        double dialSize = Math.Clamp(size * 3.1, 116, 220);
        AnalogClockFace.Width = dialSize;
        AnalogClockFace.Height = dialSize;

        // Text / Hand Color
        Brush textBrush;
        try
        {
            var col = (Color)ColorConverter.ConvertFromString(cfg.TextColorHex);
            textBrush = new SolidColorBrush(col);
        }
        catch
        {
            textBrush = Brushes.White;
        }

        ClockTimeText.Foreground = textBrush;
        ClockSecondsInlineText.Foreground = textBrush;
        ClockAmPmText.Foreground = textBrush;
        StackedHourText.Foreground = textBrush;
        StackedMinuteText.Foreground = textBrush;
        StackedSecondsText.Foreground = textBrush;
        ClockDateText.Foreground = textBrush;
        AnalogHourHand.Background = textBrush;
        AnalogMinuteHand.Background = textBrush;

        // Alignment
        var hAlign = cfg.Alignment switch
        {
            "Center" => System.Windows.HorizontalAlignment.Center,
            "Right" => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Left
        };
        var textAlign = cfg.Alignment switch
        {
            "Center" => TextAlignment.Center,
            "Right" => TextAlignment.Right,
            _ => TextAlignment.Left
        };
        DigitalTimeRow.HorizontalAlignment = hAlign;
        ClockDateText.TextAlignment = cfg.Style is "Stacked" or "Analog" ? TextAlignment.Center : textAlign;

        // Background style (Glass / Minimal / Transparent)
        bool isDark = !string.Equals(_settings.GlassTheme, "Light", StringComparison.OrdinalIgnoreCase);
        if (cfg.BackgroundStyle == "Transparent")
        {
            WidgetBorder.Background = Brushes.Transparent;
            WidgetBorder.BorderThickness = new Thickness(0);
            WidgetBorder.Padding = new Thickness(10, 6, 10, 6);
            GlassHelper.SetBackdropEnabled(this, enabled: false, isDark: isDark);
        }
        else if (cfg.BackgroundStyle == "Minimal")
        {
            WidgetBorder.Background = new SolidColorBrush(Color.FromArgb(40, 12, 14, 22));
            WidgetBorder.BorderThickness = new Thickness(0.8);
            WidgetBorder.Padding = new Thickness(16, 12, 16, 12);
            GlassHelper.SetBackdropEnabled(this, enabled: true, isDark: isDark);
        }
        else
        {
            WidgetBorder.SetResourceReference(Border.BackgroundProperty, "GlassSurfaceBrush");
            WidgetBorder.BorderThickness = new Thickness(1);
            WidgetBorder.Padding = new Thickness(18, 14, 18, 14);
            GlassHelper.SetBackdropEnabled(this, enabled: true, isDark: isDark);
        }

        // Customizable Clock Shadow (works in Transparent, Minimal, and Glass modes)
        if (cfg.EnableShadow && cfg.ShadowOpacity > 0.01)
        {
            Color shadowCol;
            try { shadowCol = (Color)ColorConverter.ConvertFromString(cfg.ShadowColorHex); }
            catch { shadowCol = Colors.Black; }

            ClockPanel.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = Math.Clamp(cfg.ShadowBlurRadius, 0, 40),
                ShadowDepth = Math.Clamp(cfg.ShadowDepth, 0, 16),
                Opacity = Math.Clamp(cfg.ShadowOpacity, 0, 1),
                Color = shadowCol
            };
        }
        else
        {
            ClockPanel.Effect = null;
        }

        // Visibility toggles
        ClockSecondsInlineText.Visibility = cfg.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        StackedSecondsText.Visibility = cfg.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        AnalogSecondHand.Visibility = cfg.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        ClockAmPmText.Visibility = cfg.Use24Hour ? Visibility.Collapsed : Visibility.Visible;
        ClockDateText.Visibility = cfg.ShowDate ? Visibility.Visible : Visibility.Collapsed;

        RefreshData();
    }

    private void RefreshData()
    {
        if (!IsVisible) return;

        if (_kind == DesktopWidgetKind.Clock)
        {
            var cfg = _settings.ClockWidget ?? new ClockWidgetConfig();
            var now = DateTime.Now;

            ClockTimeText.Text = cfg.Use24Hour ? now.ToString("HH:mm") : now.ToString("h:mm");
            ClockSecondsInlineText.Text = now.ToString(":ss");
            ClockAmPmText.Text = now.ToString("tt");

            StackedHourText.Text = cfg.Use24Hour ? now.ToString("HH") : now.ToString("hh");
            StackedMinuteText.Text = now.ToString("mm");
            StackedSecondsText.Text = cfg.Use24Hour ? now.ToString("ss's'") : now.ToString("ss's' • tt");

            HourHandRotate.Angle = ((now.Hour % 12) + now.Minute / 60.0) * 30.0;
            MinuteHandRotate.Angle = (now.Minute + now.Second / 60.0) * 6.0;
            SecondHandRotate.Angle = now.Second * 6.0;

            try
            {
                ClockDateText.Text = now.ToString(string.IsNullOrWhiteSpace(cfg.DateFormat) ? "dddd, MMMM d" : cfg.DateFormat);
            }
            catch
            {
                ClockDateText.Text = now.ToString("dddd, MMMM d");
            }
        }
        else if (_kind == DesktopWidgetKind.SystemMonitor)
        {
            var t = _status.GetTelemetry();
            SysCpuText.Text = $"{t.CpuPercent}%";
            SysRamText.Text = $"{t.UsedRamGb:0.0} / {t.TotalRamGb:0} GB";

            const double barMax = 228.0;
            SysCpuBar.Width = Math.Clamp(barMax * (t.CpuPercent / 100.0), 8.0, barMax);
            SysRamBar.Width = Math.Clamp(barMax * (t.RamPercent / 100.0), 8.0, barMax);

            var bat = _status.GetBatteryStatus();
            if (bat.HasBattery)
            {
                SysBatteryRow.Visibility = Visibility.Visible;
                SysBatteryText.Text = bat.IsCharging ? $"{bat.Percent}% (Charging)" : $"{bat.Percent}%";
            }
            else
            {
                SysBatteryRow.Visibility = Visibility.Collapsed;
            }
        }
        else if (_kind == DesktopWidgetKind.Weather)
        {
            if ((DateTime.UtcNow - _lastWeatherFetchUtc) >= TimeSpan.FromMinutes(15))
                _ = RefreshWeatherAsync();
        }
        else if (_kind == DesktopWidgetKind.Calendar)
        {
            if (DateTime.Today != _lastCalendarToday)
            {
                _lastCalendarToday = DateTime.Today;
                _calendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                BuildCalendarWidget();
            }
        }
    }

    public async Task RefreshWeatherAsync(bool force = false)
    {
        if (_kind != DesktopWidgetKind.Weather) return;
        if (_weatherFetching) return;
        if (!force && _lastWeatherSnapshot is not null && (DateTime.UtcNow - _lastWeatherFetchUtc) < TimeSpan.FromMinutes(15))
        {
            RenderWeather(_lastWeatherSnapshot);
            return;
        }

        _weatherFetching = true;
        try
        {
            if (_lastWeatherSnapshot is null)
            {
                WeatherLocationText.Text = string.IsNullOrWhiteSpace(_settings.WeatherCity) ? "Detecting location..." : _settings.WeatherCity;
                WeatherConditionText.Text = "Updating...";
            }

            var snapshot = await _weather.FetchAsync(_settings.WeatherCity);
            _lastWeatherSnapshot = snapshot;
            _lastWeatherFetchUtc = DateTime.UtcNow;
            RenderWeather(snapshot);
        }
        finally
        {
            _weatherFetching = false;
        }
    }

    private void RenderWeather(WeatherSnapshot s)
    {
        if (!s.IsSuccess)
        {
            WeatherLocationText.Text = string.IsNullOrWhiteSpace(_settings.WeatherCity) ? "Weather" : _settings.WeatherCity;
            WeatherTempText.Text = "--°";
            WeatherConditionText.Text = s.ErrorMessage ?? "Unavailable";
            WeatherHighLowText.Text = "Click refresh";
            WeatherFeelsText.Text = "Feels --°";
            WeatherHumidityText.Text = "Hum --%";
            WeatherWindText.Text = "-- km/h";
            WeatherForecastGrid.Children.Clear();
            return;
        }

        bool useF = _settings.WeatherUseFahrenheit;
        var (glyph, desc, accentHex) = WeatherService.DescribeWmoCode(s.WeatherCode, s.IsDay);

        WeatherLocationText.Text = s.LocationName;
        WeatherIconText.Text = glyph;
        try { WeatherIconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentHex)); }
        catch { WeatherIconText.Foreground = Brushes.White; }

        WeatherTempText.Text = $"{Math.Round(WeatherService.FormatTemp(s.TemperatureC, useF)):0}°";
        WeatherConditionText.Text = desc;
        WeatherFeelsText.Text = $"Feels {Math.Round(WeatherService.FormatTemp(s.ApparentTempC, useF)):0}°";
        WeatherHumidityText.Text = $"Hum {s.HumidityPercent}%";
        WeatherWindText.Text = useF
            ? $"{Math.Round(s.WindSpeedKmh * 0.621371):0} mph"
            : $"{Math.Round(s.WindSpeedKmh):0} km/h";

        if (s.Forecast.Count > 0)
        {
            var today = s.Forecast[0];
            WeatherHighLowText.Text = $"H:{Math.Round(WeatherService.FormatTemp(today.MaxTempC, useF)):0}°  L:{Math.Round(WeatherService.FormatTemp(today.MinTempC, useF)):0}°";
        }

        WeatherForecastGrid.Children.Clear();
        foreach (var day in s.Forecast.Skip(1).Take(3))
        {
            var (dGlyph, _, dAccent) = WeatherService.DescribeWmoCode(day.WeatherCode, true);
            Brush accentBrush;
            try { accentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dAccent)); }
            catch { accentBrush = Brushes.White; }

            var col = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            var dayTxt = new TextBlock { Text = day.DayName, FontSize = 10.5, FontWeight = FontWeights.Medium, TextAlignment = TextAlignment.Center };
            dayTxt.SetResourceReference(TextBlock.ForegroundProperty, "GlassTextSecondaryBrush");

            var iconTxt = new TextBlock
            {
                Text = dGlyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = accentBrush,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3)
            };

            var rangeTxt = new TextBlock
            {
                Text = $"{Math.Round(WeatherService.FormatTemp(day.MaxTempC, useF)):0}° / {Math.Round(WeatherService.FormatTemp(day.MinTempC, useF)):0}°",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center
            };
            rangeTxt.SetResourceReference(TextBlock.ForegroundProperty, "GlassTextPrimaryBrush");

            col.Children.Add(dayTxt);
            col.Children.Add(iconTxt);
            col.Children.Add(rangeTxt);
            WeatherForecastGrid.Children.Add(col);
        }
    }

    private void WeatherRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshWeatherAsync(force: true);
    }

    private void BuildCalendarWidget()
    {
        CalWidgetMonthText.Text = _calendarMonth.ToString("MMMM yyyy");
        CalWidgetDaysGrid.Children.Clear();

        int firstDow = (int)_calendarMonth.DayOfWeek;
        int daysInMonth = DateTime.DaysInMonth(_calendarMonth.Year, _calendarMonth.Month);
        var today = DateTime.Today;

        for (int i = 0; i < firstDow; i++)
            CalWidgetDaysGrid.Children.Add(new Border { Height = 26 });

        for (int day = 1; day <= daysInMonth; day++)
        {
            bool isToday = _calendarMonth.Year == today.Year && _calendarMonth.Month == today.Month && day == today.Day;
            var txt = new TextBlock
            {
                Text = day.ToString(),
                FontSize = 11.5,
                FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (isToday)
                txt.Foreground = Brushes.White;
            else
                txt.SetResourceReference(TextBlock.ForegroundProperty, "GlassTextPrimaryBrush");

            var cell = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(1),
                Background = isToday ? new SolidColorBrush(Color.FromRgb(10, 132, 255)) : Brushes.Transparent,
                Child = txt
            };
            CalWidgetDaysGrid.Children.Add(cell);
        }
    }

    private void CalWidgetPrev_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = _calendarMonth.AddMonths(-1);
        BuildCalendarWidget();
    }

    private void CalWidgetNext_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = _calendarMonth.AddMonths(1);
        BuildCalendarWidget();
    }

    private void CalWidgetMonth_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        BuildCalendarWidget();
    }

    private void RebuildRemindersList()
    {
        _settings.Reminders ??= [];
        ReminderItemsPanel.Children.Clear();

        int remaining = _settings.Reminders.Count(r => !r.IsCompleted);
        int done = _settings.Reminders.Count - remaining;
        ReminderCountText.Text = remaining == 0 ? "All done" : $"{remaining} left";
        ClearDoneRemindersBtn.Visibility = done > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_settings.Reminders.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No reminders yet. Add one below!",
                FontSize = 11.5,
                Margin = new Thickness(2, 6, 2, 6),
                FontStyle = FontStyles.Italic
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "GlassTextMutedBrush");
            ReminderItemsPanel.Children.Add(empty);
            return;
        }

        foreach (var item in _settings.Reminders)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var checkBtn = new System.Windows.Controls.Button
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 1, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Content = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = item.IsCompleted ? new SolidColorBrush(Color.FromRgb(10, 132, 255)) : Brushes.Transparent,
                    BorderBrush = item.IsCompleted ? new SolidColorBrush(Color.FromRgb(10, 132, 255)) : new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                    BorderThickness = new Thickness(1.4),
                    Child = item.IsCompleted
                        ? new TextBlock
                        {
                            Text = "\uE73E",
                            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                            FontSize = 9,
                            Foreground = Brushes.White,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                        : null
                }
            };
            checkBtn.Style = (Style)FindResource("GlassPillButton");
            checkBtn.Padding = new Thickness(0);
            checkBtn.Background = Brushes.Transparent;
            checkBtn.Click += (_, _) =>
            {
                item.IsCompleted = !item.IsCompleted;
                RebuildRemindersList();
                SettingsChanged?.Invoke();
            };
            Grid.SetColumn(checkBtn, 0);

            var text = new TextBlock
            {
                Text = item.Text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null,
                Opacity = item.IsCompleted ? 0.52 : 1.0
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "GlassTextPrimaryBrush");
            Grid.SetColumn(text, 1);

            var delBtn = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("GlassPillButton"),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "Delete",
                Content = new TextBlock
                {
                    Text = "\uE711",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 9,
                    Opacity = 0.7
                }
            };
            delBtn.Click += (_, _) =>
            {
                _settings.Reminders.Remove(item);
                RebuildRemindersList();
                SettingsChanged?.Invoke();
            };
            Grid.SetColumn(delBtn, 2);

            row.Children.Add(checkBtn);
            row.Children.Add(text);
            row.Children.Add(delBtn);
            ReminderItemsPanel.Children.Add(row);
        }
    }

    private void ReminderInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ReminderPlaceholder.Visibility = string.IsNullOrEmpty(ReminderInputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReminderInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddReminderFromInput();
            e.Handled = true;
        }
    }

    private void AddReminder_Click(object sender, RoutedEventArgs e) => AddReminderFromInput();

    private void AddReminderFromInput()
    {
        string text = ReminderInputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _settings.Reminders ??= [];
        _settings.Reminders.Insert(0, new ReminderItem { Text = text, IsCompleted = false });
        ReminderInputBox.Text = string.Empty;
        RebuildRemindersList();
        SettingsChanged?.Invoke();
    }

    private void ClearDoneReminders_Click(object sender, RoutedEventArgs e)
    {
        _settings.Reminders?.RemoveAll(r => r.IsCompleted);
        RebuildRemindersList();
        SettingsChanged?.Invoke();
    }

    private void Widget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
            SavePosition();
            KeepOnDesktopBottom();
        }
    }

    private void Widget_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SavePosition();
        ReattachToDesktopLayer();
    }

    private void SavePosition()
    {
        _settings.WidgetPositions[WidgetKey] = new WidgetPosition { Left = Math.Round(Left), Top = Math.Round(Top) };
        SettingsChanged?.Invoke();
    }

    private void NotesBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Let clicks inside the TextBox focus & type instead of triggering DragMove
        e.Handled = false;
    }

    private void NotesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressNoteChange) return;
        _settings.DesktopNoteText = NotesBox.Text;
        SettingsChanged?.Invoke();
    }

    private void UpdateNowPlaying(NowPlayingInfo info)
    {
        WidgetMediaTitle.Text = info.Title;
        WidgetMediaArtist.Text = info.Artist;
        WidgetPlayPauseIcon.Text = info.IsPlaying ? "\uE769" : "\uE768";
        WidgetMediaArt.Source = info.Thumbnail;
        WidgetMediaFallbackIcon.Visibility = info.Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MediaPrev_Click(object sender, RoutedEventArgs e) => _media.SkipPrevious();
    private void MediaPlayPause_Click(object sender, RoutedEventArgs e) => _media.TogglePlayPause();
    private void MediaNext_Click(object sender, RoutedEventArgs e) => _media.SkipNext();

    private void CustomizeClock_Click(object sender, RoutedEventArgs e) => CustomizeClockRequested?.Invoke();

    private void HideWidget_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke(_kind);

    private void ResetPosition_Click(object sender, RoutedEventArgs e)
    {
        _settings.WidgetPositions.Remove(WidgetKey);
        Left = _defaultLeft;
        Top = _defaultTop;
        SettingsChanged?.Invoke();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
