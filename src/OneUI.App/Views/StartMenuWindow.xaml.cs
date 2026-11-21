using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OneUI.App.Native;
using OneUI.App.Theme;
using OneUI.Core.Services;

namespace OneUI.App.Views;

public sealed class StartAppTileViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private ImageSource? _icon;
    private string _fallbackGlyph = string.Empty;
    private Brush _tileBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

    public string Name { get; init; } = string.Empty;
    public string LaunchPath { get; init; } = string.Empty;
    public string IconSourcePath { get; init; } = string.Empty;
    public string GroupKey { get; init; } = "#";
    public string Subtitle { get; init; } = string.Empty;
    public bool IconRequested { get; set; }

    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; Raise(nameof(Icon)); }
    }

    public string FallbackGlyph
    {
        get => _fallbackGlyph;
        set { _fallbackGlyph = value; Raise(nameof(FallbackGlyph)); }
    }

    public Brush TileBrush
    {
        get => _tileBrush;
        set { _tileBrush = value; Raise(nameof(TileBrush)); }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class StartMenuWindow : Window
{
    private enum StartView { Home, AllApps, Search }

    private static readonly string[] PreferredPinned =
    [
        "File Explorer", "Microsoft Edge", "Google Chrome", "WhatsApp",
        "Windows Terminal", "Visual Studio Code", "Windows Settings", "Notepad",
        "Calculator", "Paint", "Snipping Tool", "Task Manager", "Spotify",
        "Discord", "Microsoft Store", "Photos", "Control Panel"
    ];

    private readonly AppDiscoveryService _discovery;
    private readonly Dictionary<string, StartAppTileViewModel> _vmCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<StartAppTileViewModel> _pinned = [];
    private readonly ObservableCollection<StartAppTileViewModel> _recent = [];
    private readonly ObservableCollection<StartAppTileViewModel> _search = [];
    private StartView _view = StartView.Home;
    private int _allAppsBuiltForCount = -1;
    private bool _menuOpen;

    public StartMenuWindow(AppDiscoveryService discovery)
    {
        InitializeComponent();
        _discovery = discovery;

        PinnedItemsControl.ItemsSource = _pinned;
        RecentItemsControl.ItemsSource = _recent;
        SearchItemsControl.ItemsSource = _search;

        GlassHelper.PrepareLiquidGlass(this, roundedCorners: true, isToolWindow: true);

        var user = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(user))
        {
            UserNameText.Text = user;
            UserInitialText.Text = char.ToUpperInvariant(user[0]).ToString();
        }
    }

    public void ToggleOpen(MonitorBounds? monitor = null, bool fromTopLeft = false)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        var mon = monitor ?? MonitorHelper.GetMonitorAtCursor();
        if (fromTopLeft)
        {
            Left = mon.LeftDip + 12;
            Top = mon.TopDip + 34;
        }
        else
        {
            Left = mon.LeftDip + (mon.WidthDip - Width) / 2.0;
            Top = mon.BottomDip - Height - 86;
        }

        SearchBox.Text = string.Empty;
        SetView(StartView.Home);
        BuildHome();

        Show();
        Activate();
        SearchBox.Focus();
        AnimationHelper.AnimatePopIn(RootCard, fromScale: 0.96, fromOffsetY: fromTopLeft ? -10 : 14, durationMs: 190);
    }

    // ---------- View management ----------

    private void SetView(StartView view)
    {
        _view = view;
        HomePanel.Visibility = view == StartView.Home ? Visibility.Visible : Visibility.Collapsed;
        AllAppsPanel.Visibility = view == StartView.AllApps ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = view == StartView.Search ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildHome()
    {
        _pinned.Clear();
        foreach (var app in PickPinned())
            _pinned.Add(GetAppVm(app));

        _recent.Clear();
        foreach (var item in LoadRecentItems())
            _recent.Add(item);
        RecommendedSection.Visibility = _recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildAllApps()
    {
        var apps = _discovery.Apps;
        if (_allAppsBuiltForCount == apps.Count) return;

        var list = apps.Select(GetAppVm).ToList();
        var view = new ListCollectionView(list);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(StartAppTileViewModel.GroupKey)));
        AllAppsItemsControl.ItemsSource = view;
        _allAppsBuiltForCount = apps.Count;
    }

    private List<DiscoveredApp> PickPinned()
    {
        var apps = _discovery.Apps;
        var result = new List<DiscoveredApp>();

        foreach (var pref in PreferredPinned)
        {
            var match = apps.FirstOrDefault(a => a.Name.Equals(pref, StringComparison.OrdinalIgnoreCase))
                        ?? apps.FirstOrDefault(a => a.Name.StartsWith(pref, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !result.Contains(match))
                result.Add(match);
            if (result.Count >= 12) break;
        }

        foreach (var app in apps)
        {
            if (result.Count >= 12) break;
            if (!result.Contains(app)) result.Add(app);
        }

        return result;
    }

    private IEnumerable<StartAppTileViewModel> LoadRecentItems()
    {
        var results = new List<StartAppTileViewModel>();
        try
        {
            var recentDir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (string.IsNullOrEmpty(recentDir) || !Directory.Exists(recentDir))
                return results;

            var files = new DirectoryInfo(recentDir)
                .EnumerateFiles("*.lnk")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(6);

            foreach (var f in files)
            {
                var vm = new StartAppTileViewModel
                {
                    Name = Path.GetFileNameWithoutExtension(f.Name),
                    LaunchPath = f.FullName,
                    IconSourcePath = f.FullName,
                    Subtitle = TimeAgo(f.LastWriteTime)
                };
                EnsureIcon(vm);
                results.Add(vm);
            }
        }
        catch { }
        return results;
    }

    private static string TimeAgo(DateTime when)
    {
        var span = DateTime.Now - when;
        if (span.TotalMinutes < 1) return "Just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        if (span.TotalDays < 2) return "Yesterday";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} days ago";
        return when.ToString("MMM d");
    }

    // ---------- View models & icons ----------

    private StartAppTileViewModel GetAppVm(DiscoveredApp app)
    {
        if (_vmCache.TryGetValue(app.LaunchPath, out var cached))
        {
            EnsureIcon(cached);
            return cached;
        }

        var first = app.Name.Length > 0 ? char.ToUpperInvariant(app.Name[0]) : '#';
        var vm = new StartAppTileViewModel
        {
            Name = app.Name,
            LaunchPath = app.LaunchPath,
            IconSourcePath = app.TargetPath,
            GroupKey = char.IsLetter(first) ? first.ToString() : "#"
        };
        _vmCache[app.LaunchPath] = vm;
        EnsureIcon(vm);
        return vm;
    }

    private void EnsureIcon(StartAppTileViewModel vm)
    {
        if (vm.IconRequested) return;
        vm.IconRequested = true;

        var primary = vm.IconSourcePath;
        var alt = vm.LaunchPath;

        Task.Run(() =>
        {
            ImageSource? icon = null;
            try { icon = _discovery.GetIcon(primary) ?? _discovery.GetIcon(alt); } catch { }
            return icon;
        }).ContinueWith(t =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (t.Result is ImageSource icon)
                {
                    vm.Icon = icon;
                    vm.TileBrush = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
                }
                else
                {
                    var (glyph, brush) = GetFallbackVisual(vm.Name);
                    vm.FallbackGlyph = glyph;
                    vm.TileBrush = brush;
                }
            });
        });
    }

    private static (string Glyph, Brush TileBrush) GetFallbackVisual(string name)
    {
        var subtleGlass = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
        var lower = name.ToLowerInvariant();
        if (lower.Contains("explorer")) return ("\uE8B7", subtleGlass);
        if (lower.Contains("edge") || lower.Contains("chrome") || lower.Contains("browser")) return ("\uE774", subtleGlass);
        if (lower.Contains("terminal") || lower.Contains("powershell") || lower.Contains("command")) return ("\uE756", subtleGlass);
        if (lower.Contains("setting") || lower.Contains("control")) return ("\uE713", subtleGlass);
        if (lower.Contains("calc")) return ("\uE8EF", subtleGlass);
        if (lower.Contains("note")) return ("\uE70B", subtleGlass);
        if (lower.Contains("snip") || lower.Contains("paint")) return ("\uE722", subtleGlass);
        if (lower.Contains("task")) return ("\uE9D9", subtleGlass);
        return ("\uE74C", subtleGlass);
    }

    // ---------- Search ----------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrEmpty(query))
        {
            SetView(StartView.Home);
            return;
        }

        _search.Clear();
        var matches = _discovery.Apps
            .Select(a => (App: a, Score: FuzzyMatcher.Score(query, a.Name)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(30);

        foreach (var (app, _) in matches)
            _search.Add(GetAppVm(app));

        SearchHeaderText.Text = _search.Count > 0 ? "Best match" : "Results";
        NoResultsText.Visibility = _search.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetView(StartView.Search);
    }

    // ---------- Navigation ----------

    private void ShowAllApps_Click(object sender, RoutedEventArgs e)
    {
        BuildAllApps();
        SetView(StartView.AllApps);
    }

    private void BackToHome_Click(object sender, RoutedEventArgs e) => SetView(StartView.Home);

    private void AppTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: StartAppTileViewModel tile })
        {
            Hide();
            AppDiscoveryService.Launch(tile.LaunchPath);
        }
    }

    // ---------- Footer actions ----------

    private void OpenWinSettings_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SystemStatusService.OpenWindowsSettings();
    }

    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        _menuOpen = true;
        PowerMenu.PlacementTarget = PowerButton;
        PowerMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        PowerMenu.IsOpen = true;
    }

    private void PowerMenu_Closed(object sender, RoutedEventArgs e) => _menuOpen = false;

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SystemStatusService.LockScreen();
    }

    private void Sleep_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SystemStatusService.SleepComputer();
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        if (System.Windows.MessageBox.Show("Restart your PC now?", "Restart", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            SystemStatusService.RestartComputer();
    }

    private void Shutdown_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        if (System.Windows.MessageBox.Show("Shut down your PC now?", "Shut down", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            SystemStatusService.ShutdownComputer();
    }

    // ---------- Keyboard / focus ----------

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text))
                SearchBox.Text = string.Empty;
            else if (_view == StartView.AllApps)
                SetView(StartView.Home);
            else
                Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _view == StartView.Search && _search.Count > 0)
        {
            var first = _search[0];
            Hide();
            AppDiscoveryService.Launch(first.LaunchPath);
            e.Handled = true;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsVisible && !_menuOpen) Hide();
    }
}
