using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OneUI.App.Native;
using OneUI.App.Theme;

namespace OneUI.App.Views;

public partial class CalendarFlyoutWindow : Window
{
    private readonly DispatcherTimer _clockTimer;
    private DateTime _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public CalendarFlyoutWindow()
    {
        InitializeComponent();
        GlassHelper.PrepareLiquidGlass(this, roundedCorners: true, isToolWindow: true);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
    }

    public void ToggleOpen(MonitorBounds? monitor = null)
    {
        if (IsVisible)
        {
            _clockTimer.Stop();
            Hide();
            return;
        }

        _displayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        UpdateClock();
        BuildCalendar();

        var mon = monitor ?? MonitorHelper.GetMonitorAtCursor();
        Left = mon.RightDip - Width - 12;
        Top = mon.TopDip + 34;

        Show();
        Activate();
        _clockTimer.Start();
        AnimationHelper.AnimatePopIn(RootCard, fromScale: 0.94, fromOffsetY: -8, durationMs: 190);
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        BigTimeText.Text = now.ToString("h:mm:ss tt");
        FullDateText.Text = now.ToString("dddd, MMMM d, yyyy");
    }

    private void BuildCalendar()
    {
        MonthYearText.Text = _displayMonth.ToString("MMMM yyyy");
        DaysGrid.Children.Clear();

        int firstDayOfWeek = (int)_displayMonth.DayOfWeek;
        int daysInMonth = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);
        var today = DateTime.Today;

        for (int i = 0; i < firstDayOfWeek; i++)
        {
            DaysGrid.Children.Add(new Border { Height = 28 });
        }

        for (int day = 1; day <= daysInMonth; day++)
        {
            bool isToday = _displayMonth.Year == today.Year && _displayMonth.Month == today.Month && day == today.Day;

            var cell = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(1),
                Background = isToday ? new SolidColorBrush(Color.FromRgb(10, 132, 255)) : Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = day.ToString(),
                    FontSize = 12,
                    FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            if (isToday)
            {
                ((TextBlock)cell.Child).Foreground = Brushes.White;
            }
            else
            {
                ((TextBlock)cell.Child).SetResourceReference(TextBlock.ForegroundProperty, "GlassTextPrimaryBrush");
            }

            DaysGrid.Children.Add(cell);
        }
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        BuildCalendar();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        BuildCalendar();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        BuildCalendar();
    }

    private void DateTimeSettings_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SystemStatusService.OpenWindowsSettings("dateandtime");
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        _clockTimer.Stop();
        if (IsVisible) Hide();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _clockTimer.Stop();
            Hide();
        }
    }
}
