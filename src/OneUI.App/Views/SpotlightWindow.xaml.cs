using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OneUI.App.Native;
using OneUI.App.Theme;
using OneUI.Core.Models;
using OneUI.Core.Services;

namespace OneUI.App.Views;

public sealed class SpotlightItemViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Badge { get; init; } = string.Empty;
    public string ActionTarget { get; init; } = string.Empty;
    public nint WindowHandle { get; init; }
    public SearchResultKind Kind { get; init; }
    public ImageSource? Icon { get; init; }
    public string Glyph { get; init; } = string.Empty;
}

public partial class SpotlightWindow : Window
{
    private readonly AppDiscoveryService _discovery;
    private readonly WindowTracker? _tracker;
    private readonly ObservableCollection<SpotlightItemViewModel> _items = [];

    public event Action? OpenSettingsRequested;
    public event Action? RestoreWindowsRequested;

    private static readonly (string Name, string Subtitle, string CommandId, string Glyph)[] SystemCommands =
    [
        ("OneUI Settings", "Configure Dock, Glass Theme, and Window Controls", "cmd:oneui-settings", "\uE713"),
        ("Restore Native Windows UI", "Show Windows taskbar immediately", "cmd:restore-taskbar", "\uE777"),
        ("Show Desktop", "Minimize or restore all open windows (Win+D)", "cmd:desktop", "\uE7F4"),
        ("Lock Screen", "Lock your Windows session", "cmd:lock", "\uE72E"),
        ("Sleep", "Put this PC to sleep", "cmd:sleep", "\uE708"),
        ("Recycle Bin", "Open Windows Recycle Bin", "cmd:recycle", "\uE74D"),
        ("Task Manager", "Inspect running apps and performance", "cmd:taskmgr", "\uE9D9"),
        ("Windows Settings", "Open system settings", "cmd:winsettings", "\uE713"),
        ("Snipping Tool", "Capture a screenshot", "cmd:snip", "\uE722")
    ];

    public SpotlightWindow(AppDiscoveryService discovery, WindowTracker? tracker = null)
    {
        InitializeComponent();
        _discovery = discovery;
        _tracker = tracker;
        ResultsList.ItemsSource = _items;
        GlassHelper.PrepareLiquidGlass(this, roundedCorners: true, isToolWindow: true);
    }

    public void ToggleOpen(MonitorBounds? monitor = null)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        var mon = monitor ?? MonitorHelper.GetMonitorAtCursor();
        Left = mon.LeftDip + (mon.WidthDip - Width) / 2.0;
        Top = mon.TopDip + mon.HeightDip * 0.20;
        QueryBox.Text = string.Empty;
        _items.Clear();
        ResultsContainer.Visibility = Visibility.Collapsed;

        Show();
        Activate();
        QueryBox.Focus();
        AnimationHelper.AnimateCenterPopIn(RootCard, fromScale: 0.95, durationMs: 170);
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = QueryBox.Text.Trim();
        PlaceholderText.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;

        _items.Clear();
        if (string.IsNullOrEmpty(query))
        {
            ResultsContainer.Visibility = Visibility.Collapsed;
            return;
        }

        // 1. Instant Calculator
        if (MathEvaluator.TryEvaluate(query, out var calcVal))
        {
            var formatted = MathEvaluator.FormatResult(calcVal);
            _items.Add(new SpotlightItemViewModel
            {
                Title = $"= {formatted}",
                Subtitle = "Press Enter to copy result to clipboard",
                Badge = "Calculator",
                ActionTarget = formatted,
                Kind = SearchResultKind.Calculator,
                Glyph = "\uE8EF"
            });
        }

        // 2. Currently Open Windows (Chrome accounts/tabs, File Explorer folders, VS Code workspaces, etc.)
        if (_tracker is not null)
        {
            var openMatches = _tracker.GetRunningAppWindows()
                .Select(w => (Win: w, Score: Math.Max(FuzzyMatcher.Score(query, w.Title), FuzzyMatcher.Score(query, w.ProcessName))))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(4);

            foreach (var (win, _) in openMatches)
            {
                var icon = _discovery.GetIcon(win.ExecutablePath);
                _items.Add(new SpotlightItemViewModel
                {
                    Title = win.Title,
                    Subtitle = $"Switch to open {win.ProcessName} window",
                    Badge = "Open Window",
                    WindowHandle = win.Handle,
                    Kind = SearchResultKind.OpenWindow,
                    Icon = icon,
                    Glyph = icon is null ? "\uE737" : string.Empty
                });
            }
        }

        // 3. System Commands
        var matchedCommands = SystemCommands
            .Select(c => (Cmd: c, Score: FuzzyMatcher.Score(query, c.Name)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(3);

        foreach (var (cmd, _) in matchedCommands)
        {
            _items.Add(new SpotlightItemViewModel
            {
                Title = cmd.Name,
                Subtitle = cmd.Subtitle,
                Badge = "Command",
                ActionTarget = cmd.CommandId,
                Kind = SearchResultKind.SystemCommand,
                Glyph = cmd.Glyph
            });
        }

        // 4. Installed Applications
        var matchedApps = _discovery.Apps
            .Select(a => (App: a, Score: FuzzyMatcher.Score(query, a.Name)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(6);

        foreach (var (app, _) in matchedApps)
        {
            var icon = _discovery.GetIcon(app.TargetPath) ?? _discovery.GetIcon(app.LaunchPath);
            _items.Add(new SpotlightItemViewModel
            {
                Title = app.Name,
                Subtitle = app.TargetPath,
                Badge = "Launch App",
                ActionTarget = app.LaunchPath,
                Kind = SearchResultKind.Application,
                Icon = icon,
                Glyph = icon is null ? "\uE74C" : string.Empty
            });
        }

        // 5. Web Search fallback
        _items.Add(new SpotlightItemViewModel
        {
            Title = $"Search Web for \"{query}\"",
            Subtitle = "Open in default browser",
            Badge = "Web",
            ActionTarget = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}",
            Kind = SearchResultKind.WebSearch,
            Glyph = "\uE774"
        });

        ResultsContainer.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_items.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && _items.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Min(_items.Count - 1, ResultsList.SelectedIndex + 1);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && _items.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex - 1);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ResultsList.SelectedItem is SpotlightItemViewModel selected)
        {
            ExecuteItem(selected);
            e.Handled = true;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is SpotlightItemViewModel selected)
        {
            ExecuteItem(selected);
        }
    }

    private void ExecuteItem(SpotlightItemViewModel item)
    {
        Hide();
        switch (item.Kind)
        {
            case SearchResultKind.Calculator:
                System.Windows.Clipboard.SetText(item.ActionTarget);
                break;

            case SearchResultKind.OpenWindow:
                WindowTracker.ActivateWindow(item.WindowHandle);
                break;

            case SearchResultKind.Application:
            case SearchResultKind.WebSearch:
                AppDiscoveryService.Launch(item.ActionTarget);
                break;

            case SearchResultKind.SystemCommand:
                ExecuteCommand(item.ActionTarget);
                break;
        }
    }

    private void ExecuteCommand(string commandId)
    {
        switch (commandId)
        {
            case "cmd:oneui-settings": OpenSettingsRequested?.Invoke(); break;
            case "cmd:restore-taskbar": RestoreWindowsRequested?.Invoke(); break;
            case "cmd:desktop": SystemStatusService.ShowDesktop(); break;
            case "cmd:lock": SystemStatusService.LockScreen(); break;
            case "cmd:sleep": SystemStatusService.SleepComputer(); break;
            case "cmd:recycle": SystemStatusService.OpenRecycleBin(); break;
            case "cmd:taskmgr": SystemStatusService.OpenTaskManager(); break;
            case "cmd:winsettings": SystemStatusService.OpenWindowsSettings(); break;
            case "cmd:snip": SystemStatusService.OpenSnippingTool(); break;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsVisible) Hide();
    }
}
