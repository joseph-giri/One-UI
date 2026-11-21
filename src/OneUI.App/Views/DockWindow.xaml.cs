using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using OneUI.App.Native;
using OneUI.App.Theme;
using OneUI.Core.Models;
using OneUI.Core.Services;

namespace OneUI.App.Views;

public partial class DockWindow : Window
{
    private readonly AppSettings _settings;
    private readonly AppDiscoveryService _discovery;
    private readonly WindowTracker _tracker;
    private readonly List<DockIconVisual> _iconVisuals = [];
    private readonly DispatcherTimer _autoHideTimer;
    private readonly DispatcherTimer _previewDelayTimer;
    private readonly DispatcherTimer _previewHideTimer;
    private readonly WindowPreviewPopup _previewPopup;
    private MonitorBounds _currentMonitor = MonitorHelper.GetPrimaryMonitor();
    private DockItem? _pickerCurrentItem;
    private DockIconVisual? _hoveredVisual;
    private bool _isAutoHidden;
    private bool _isSpringActive;
    private bool _pendingRebuild;

    public event Action? SettingsChanged;
    public event Action<MonitorBounds>? StartMenuRequested;

    private sealed class DockIconVisual
    {
        public required DockItem Item { get; init; }
        public required Grid Container { get; init; }
        public required FrameworkElement IconHost { get; init; }
        public required ScaleTransform Scale { get; init; }
        public required TranslateTransform Translate { get; init; }
        public double CurrentScale { get; set; } = 1.0;
        public double TargetScale { get; set; } = 1.0;
        public double CurrentLift { get; set; }
        public double TargetLift { get; set; }
        public double CurrentSpread { get; set; }
        public double TargetSpread { get; set; }
    }

    public DockWindow(AppSettings settings, AppDiscoveryService discovery, WindowTracker tracker)
    {
        InitializeComponent();
        _settings = settings;
        _discovery = discovery;
        _tracker = tracker;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == nint.Zero) return;
            int ex = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
            ex |= Win32Api.WS_EX_TOOLWINDOW | Win32Api.WS_EX_NOACTIVATE;
            ex &= ~Win32Api.WS_EX_APPWINDOW;
            _ = Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, ex);
        };

        _previewPopup = new WindowPreviewPopup();
        _previewPopup.PointerLeftPopup += () => _previewHideTimer?.Start();

        _previewDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _previewDelayTimer.Tick += (_, _) =>
        {
            _previewDelayTimer.Stop();
            ShowHoverPreviewForCurrent();
        };

        _previewHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _previewHideTimer.Tick += (_, _) =>
        {
            _previewHideTimer.Stop();
            if (!IsMouseOver && !_previewPopup.IsMouseOver)
            {
                _previewPopup.HideAndClear();
                if (_pendingRebuild && !WindowPickerPopup.IsOpen)
                    RebuildDock(force: true);
            }
        };

        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _autoHideTimer.Tick += AutoHideTimer_Tick;
        _autoHideTimer.Start();

        SizeChanged += (_, _) => RepositionBottomCenter();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(RepositionBottomCenter);
        Closed += (_, _) => _previewPopup.Close();
        RebuildDock(force: true);
    }

    private void ShowHoverPreviewForCurrent()
    {
        if (!IsMouseOver || WindowPickerPopup.IsOpen || _hoveredVisual is null)
            return;

        var item = _hoveredVisual.Item;
        if (!item.IsRunning || item.Windows.Count == 0)
            return;

        TooltipPopup.IsOpen = false;
        var pt = _hoveredVisual.Container.TranslatePoint(new Point(_hoveredVisual.Container.ActualWidth / 2.0, 0), this);
        var dockHwnd = new WindowInteropHelper(this).Handle;
        if (dockHwnd == nint.Zero || !Win32Api.GetWindowRect(dockHwnd, out var dockRect))
            return;

        double iconCenterXPhys = dockRect.Left + (pt.X * _currentMonitor.DpiScaleX);
        int dockTopPhys = dockRect.Top + (int)Math.Round(16 * _currentMonitor.DpiScaleY);
        _previewPopup.ShowForItem(item, _currentMonitor, iconCenterXPhys, dockTopPhys);
    }

    public void RebuildDock(bool force = false)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Never yank icons out from under the user's cursor while hovering or picking a window
            if (!force && (IsMouseOver || WindowPickerPopup.IsOpen))
            {
                _pendingRebuild = true;
                return;
            }

            _pendingRebuild = false;
            IconsPanel.Children.Clear();
            _iconVisuals.Clear();

            var runningWindows = _tracker.GetRunningAppWindows();
            var matchedRunningHandles = new HashSet<nint>();

            // 0. Windows Start Menu button
            AddIconControl(new DockItem
            {
                Id = "special:start",
                Name = "Start Menu",
                IsPinned = true,
                IsSpecial = true
            });
            AddDivider();

            // 1. Pinned Applications (collect ALL open windows for each pinned app!)
            foreach (var pinned in _settings.PinnedApps)
            {
                var procKey = System.IO.Path.GetFileNameWithoutExtension(pinned.ExecutablePath);
                var matchingWindows = runningWindows
                    .Where(w => !string.IsNullOrEmpty(procKey) &&
                                w.ProcessName.Equals(procKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var mw in matchingWindows)
                    matchedRunningHandles.Add(mw.Handle);

                AddIconControl(new DockItem
                {
                    Id = pinned.ExecutablePath,
                    Name = pinned.Name,
                    ExecutablePath = pinned.ExecutablePath,
                    Arguments = pinned.Arguments,
                    ProcessName = matchingWindows.FirstOrDefault()?.ProcessName ?? procKey,
                    IsPinned = true,
                    IsRunning = matchingWindows.Count > 0,
                    WindowHandle = matchingWindows.FirstOrDefault()?.Handle ?? nint.Zero,
                    Windows = matchingWindows
                });
            }

            // 2. Unpinned Running Applications (grouped by process name, preserving ALL windows per process!)
            var unpinnedGroups = runningWindows
                .Where(w => !matchedRunningHandles.Contains(w.Handle))
                .GroupBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            if (unpinnedGroups.Count > 0)
            {
                AddDivider();
                foreach (var group in unpinnedGroups)
                {
                    var first = group.First();
                    var allWins = group.ToList();
                    AddIconControl(new DockItem
                    {
                        Id = first.ExecutablePath,
                        Name = FormatAppDisplayName(first.ProcessName, first.Title),
                        ExecutablePath = first.ExecutablePath,
                        ProcessName = first.ProcessName,
                        IsPinned = false,
                        IsRunning = true,
                        WindowHandle = first.Handle,
                        Windows = allWins
                    });
                }
            }

            // 3. Divider + Recycle Bin
            AddDivider();
            AddIconControl(new DockItem
            {
                Id = "special:trash",
                Name = "Recycle Bin",
                ExecutablePath = "explorer.exe",
                Arguments = "shell:RecycleBinFolder",
                IsPinned = true,
                IsSpecial = true
            });

            RepositionBottomCenter();
        });
    }

    private static string FormatAppDisplayName(string proc, string fallbackTitle)
    {
        if (string.IsNullOrWhiteSpace(proc)) return fallbackTitle;
        return proc.ToLowerInvariant() switch
        {
            "chrome" => "Google Chrome",
            "msedge" => "Microsoft Edge",
            "firefox" => "Firefox",
            "explorer" => "File Explorer",
            "code" => "Visual Studio Code",
            "windowsterminal" or "wt" => "Windows Terminal",
            _ => char.ToUpperInvariant(proc[0]) + proc[1..]
        };
    }

    private void AddDivider()
    {
        var sep = new Border
        {
            Width = 1,
            Height = _settings.DockIconSize * 0.72,
            CornerRadius = new CornerRadius(0.5),
            Margin = new Thickness(8, 0, 8, 9),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        sep.SetResourceReference(Border.BackgroundProperty, "GlassDividerBrush");
        IconsPanel.Children.Add(sep);
    }

    private void AddIconControl(DockItem item)
    {
        double size = _settings.DockIconSize;

        var scale = new ScaleTransform(1.0, 1.0);
        var translate = new TranslateTransform(0, 0);
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(scale);
        transformGroup.Children.Add(translate);

        FrameworkElement iconVisual;
        ImageSource? img = item.Id switch
        {
            "special:start" => null,
            "special:trash" => _discovery.GetIcon("special:trash"),
            _ => _discovery.GetIcon(item.ExecutablePath) ?? _discovery.GetIconByAppName(item.Name)
        };

        if (img is not null)
        {
            // Render genuine Windows 11 default icon directly on the glass (no fake Mac squircle plate!)
            var image = new System.Windows.Controls.Image
            {
                Source = img,
                Width = size,
                Height = size,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.32, Color = Colors.Black }
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            iconVisual = image;
        }
        else if (item.Id == "special:start")
        {
            // Minimal native Windows 11 4-pane blue emblem directly on the glass
            var winGrid = new Grid
            {
                Width = size * 0.62,
                Height = size * 0.62,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.30, Color = Colors.Black }
            };
            winGrid.RowDefinitions.Add(new RowDefinition());
            winGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            winGrid.RowDefinitions.Add(new RowDefinition());
            winGrid.ColumnDefinitions.Add(new ColumnDefinition());
            winGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
            winGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var winBlue = new LinearGradientBrush(Color.FromRgb(76, 194, 255), Color.FromRgb(0, 120, 212), 45);
            for (int r = 0; r <= 2; r += 2)
            {
                for (int c = 0; c <= 2; c += 2)
                {
                    var pane = new Border { Background = winBlue, CornerRadius = new CornerRadius(1.5) };
                    Grid.SetRow(pane, r);
                    Grid.SetColumn(pane, c);
                    winGrid.Children.Add(pane);
                }
            }
            iconVisual = winGrid;
        }
        else
        {
            // Clean native Fluent glyph fallback if an executable has no embedded icon
            iconVisual = new TextBlock
            {
                Text = "\uE74C",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = size * 0.56,
                Foreground = (Brush)FindResource("GlassTextPrimaryBrush"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        // Wrap icon + optional multi-window count badge (2, 3, ...) in a single transformed host
        var hostGrid = new Grid
        {
            Width = size,
            Height = size,
            Cursor = System.Windows.Input.Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 1.0),
            RenderTransform = transformGroup
        };
        hostGrid.Children.Add(iconVisual);

        if (item.HasMultipleWindows)
        {
            var badge = new Border
            {
                MinWidth = 17,
                Height = 17,
                CornerRadius = new CornerRadius(8.5),
                Background = new SolidColorBrush(Color.FromRgb(10, 132, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(4, 0, 4, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -3, -3, 0),
                Child = new TextBlock
                {
                    Text = item.WindowCount.ToString(),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            hostGrid.Children.Add(badge);
        }

        // Running app luminous indicator pill (wider pill when multiple windows are open!)
        var dot = new Border
        {
            Width = item.HasMultipleWindows ? 13 : (item.IsRunning ? 5 : 4),
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Background = item.IsRunning
                ? new SolidColorBrush(Color.FromArgb(245, 255, 255, 255))
                : Brushes.Transparent
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        stack.Children.Add(hostGrid);
        stack.Children.Add(dot);

        var container = new Grid
        {
            Margin = new Thickness(5, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        container.Children.Add(stack);

        hostGrid.MouseLeftButtonUp += (_, _) => HandleIconClick(item, hostGrid, translate);
        hostGrid.MouseDown += (_, args) =>
        {
            // Middle-click always opens a brand new window/instance (just like Windows taskbar!)
            if (args.ChangedButton == MouseButton.Middle && !item.IsSpecial)
            {
                AnimationHelper.AnimateLaunchBounce(translate, doubleHop: true);
                AppDiscoveryService.Launch(item.ExecutablePath, item.Arguments);
                args.Handled = true;
            }
        };
        hostGrid.ContextMenu = BuildContextMenu(item);

        IconsPanel.Children.Add(container);
        _iconVisuals.Add(new DockIconVisual
        {
            Item = item,
            Container = container,
            IconHost = hostGrid,
            Scale = scale,
            Translate = translate
        });
    }

    private ContextMenu BuildContextMenu(DockItem item)
    {
        var menu = new ContextMenu();
        var fgHwnd = Win32Api.GetForegroundWindow();

        // List every open window / profile of this application at the top of the right-click menu!
        if (item.Windows.Count > 0)
        {
            foreach (var win in item.Windows)
            {
                var label = win.Title.Length > 44 ? win.Title[..44] + "…" : win.Title;
                if (win.Handle == fgHwnd) label = "✓  " + label;
                else if (win.IsMinimized) label = "⎯  " + label;

                var winMenuItem = new MenuItem { Header = label };
                var targetHandle = win.Handle;
                winMenuItem.Click += (_, _) => WindowTracker.ActivateWindow(targetHandle);
                menu.Items.Add(winMenuItem);
            }
            menu.Items.Add(new Separator());
        }

        if (!item.IsSpecial)
        {
            var newWinItem = new MenuItem { Header = item.IsRunning ? "New Window" : "Open" };
            newWinItem.Click += (_, _) => AppDiscoveryService.Launch(item.ExecutablePath, item.Arguments);
            menu.Items.Add(newWinItem);

            if (item.IsPinned)
            {
                var unpin = new MenuItem { Header = "Remove from Dock" };
                unpin.Click += (_, _) =>
                {
                    _settings.PinnedApps.RemoveAll(p =>
                        p.ExecutablePath.Equals(item.ExecutablePath, StringComparison.OrdinalIgnoreCase));
                    SettingsChanged?.Invoke();
                    RebuildDock(force: true);
                };
                menu.Items.Add(unpin);
            }
            else if (!string.IsNullOrWhiteSpace(item.ExecutablePath))
            {
                var pin = new MenuItem { Header = "Keep in Dock" };
                pin.Click += (_, _) =>
                {
                    _settings.PinnedApps.Add(new PinnedAppConfig
                    {
                        Name = item.Name,
                        ExecutablePath = item.ExecutablePath
                    });
                    SettingsChanged?.Invoke();
                    RebuildDock(force: true);
                };
                menu.Items.Add(pin);
            }
        }
        else
        {
            var openSpecial = new MenuItem { Header = "Open " + item.Name };
            openSpecial.Click += (_, _) => LaunchOrFocusSingle(item);
            menu.Items.Add(openSpecial);
        }

        if (item.Windows.Count > 0)
        {
            menu.Items.Add(new Separator());
            var closeItem = new MenuItem
            {
                Header = item.HasMultipleWindows ? $"Close All {item.WindowCount} Windows" : "Close Window"
            };
            closeItem.Click += (_, _) =>
            {
                foreach (var w in item.Windows)
                    WindowTracker.CloseWindow(w.Handle);
            };
            menu.Items.Add(closeItem);
        }

        return menu;
    }

    private void HandleIconClick(DockItem item, FrameworkElement anchor, TranslateTransform translate)
    {
        _previewDelayTimer.Stop();
        _previewPopup.HideAndClear();

        if (item.HasMultipleWindows)
        {
            TooltipPopup.IsOpen = false;
            ShowWindowPicker(item, anchor);
            return;
        }

        AnimationHelper.AnimateLaunchBounce(translate, doubleHop: !item.IsRunning && !item.IsSpecial);
        LaunchOrFocusSingle(item);
    }

    private void ShowWindowPicker(DockItem item, FrameworkElement anchor)
    {
        _pickerCurrentItem = item;
        PickerHeaderText.Text = $"{item.Name} ({item.WindowCount} windows)";
        PickerItemsPanel.Children.Clear();

        var fgHwnd = Win32Api.GetForegroundWindow();

        foreach (var win in item.Windows)
        {
            bool isFocused = win.Handle == fgHwnd;

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBtn = new Button
            {
                Style = (Style)FindResource("GlassPillButton"),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                Padding = new Thickness(10, 7, 10, 7)
            };

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
            titleStack.Children.Add(new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = isFocused
                    ? new SolidColorBrush(Color.FromRgb(48, 209, 88))
                    : (win.IsMinimized
                        ? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
                        : new SolidColorBrush(Color.FromRgb(10, 132, 255)))
            });

            var cleanTitle = win.Title.Length > 42 ? win.Title[..42] + "…" : win.Title;
            titleStack.Children.Add(new TextBlock
            {
                Text = cleanTitle,
                FontSize = 12.5,
                FontWeight = isFocused ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleBtn.Content = titleStack;

            var targetHwnd = win.Handle;
            titleBtn.Click += (_, _) =>
            {
                WindowPickerPopup.IsOpen = false;
                WindowTracker.ActivateWindow(targetHwnd);
            };

            var closeBtn = new Button
            {
                Style = (Style)FindResource("GlassPillButton"),
                Content = "×",
                FontSize = 13,
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Close this window"
            };
            closeBtn.Click += (_, _) =>
            {
                WindowTracker.CloseWindow(targetHwnd);
                WindowPickerPopup.IsOpen = false;
            };

            Grid.SetColumn(titleBtn, 0);
            Grid.SetColumn(closeBtn, 1);
            rowGrid.Children.Add(titleBtn);
            rowGrid.Children.Add(closeBtn);

            PickerItemsPanel.Children.Add(rowGrid);
        }

        WindowPickerPopup.PlacementTarget = anchor;
        WindowPickerPopup.VerticalOffset = -10;
        WindowPickerPopup.IsOpen = true;
    }

    private void PickerNewWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowPickerPopup.IsOpen = false;
        if (_pickerCurrentItem is not null && !string.IsNullOrWhiteSpace(_pickerCurrentItem.ExecutablePath))
        {
            AppDiscoveryService.Launch(_pickerCurrentItem.ExecutablePath, _pickerCurrentItem.Arguments);
        }
    }

    private void LaunchOrFocusSingle(DockItem item)
    {
        if (item.Id == "special:start")
        {
            StartMenuRequested?.Invoke(_currentMonitor);
            return;
        }

        if (item.IsRunning && item.WindowHandle != nint.Zero)
        {
            WindowTracker.FocusOrRestore(item.WindowHandle);
        }
        else
        {
            AppDiscoveryService.Launch(item.ExecutablePath, item.Arguments);
        }
    }

    private void Dock_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var mousePos = e.GetPosition(IconsPanel);
        double baseSize = _settings.DockIconSize;
        double radius = baseSize * 2.4;
        double maxScale = Math.Min(1.38, _settings.DockMagnificationScale);

        DockIconVisual? closest = null;
        double minDist = double.MaxValue;

        foreach (var v in _iconVisuals)
        {
            var pt = v.Container.TranslatePoint(new Point(v.Container.ActualWidth / 2.0, 0), IconsPanel);
            double dist = Math.Abs(mousePos.X - pt.X);

            if (dist < minDist)
            {
                minDist = dist;
                closest = v;
            }

            double scale = MagnificationMath.ComputeScale(dist, radius, maxScale);
            v.TargetScale = scale;
            v.TargetLift = MagnificationMath.ComputeLiftOffset(scale, 6.0);
            v.TargetSpread = (scale - 1.0) * (baseSize * 0.36);
        }

        StartSpringLoop();

        if (closest is not null && minDist < baseSize * 0.85 && !WindowPickerPopup.IsOpen)
        {
            _previewHideTimer.Stop();

            if (!ReferenceEquals(_hoveredVisual, closest))
            {
                _hoveredVisual = closest;
                _previewDelayTimer.Stop();

                if (closest.Item.IsRunning && closest.Item.Windows.Count > 0)
                {
                    if (_previewPopup.IsVisible)
                    {
                        // Switching from one running icon to another: update preview immediately
                        ShowHoverPreviewForCurrent();
                    }
                    else
                    {
                        _previewDelayTimer.Start();
                    }
                }
                else
                {
                    _previewPopup.HideAndClear();
                }
            }

            if (!_previewPopup.IsVisible)
            {
                var suffix = closest.Item.HasMultipleWindows ? $" ({closest.Item.WindowCount} windows)" : "";
                TooltipText.Text = closest.Item.Name + suffix;
                var pt = closest.Container.TranslatePoint(new Point(closest.Container.ActualWidth / 2.0, 0), DockRootGrid);
                TooltipPopup.HorizontalOffset = pt.X - 54;
                TooltipPopup.VerticalOffset = -38;
                TooltipPopup.IsOpen = true;
            }
        }
        else
        {
            _hoveredVisual = null;
            _previewDelayTimer.Stop();
            TooltipPopup.IsOpen = false;
            if (_previewPopup.IsVisible)
                _previewHideTimer.Start();
        }
    }

    private void Dock_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hoveredVisual = null;
        _previewDelayTimer.Stop();
        TooltipPopup.IsOpen = false;

        foreach (var v in _iconVisuals)
        {
            v.TargetScale = 1.0;
            v.TargetLift = 0.0;
            v.TargetSpread = 0.0;
        }
        StartSpringLoop();

        if (_previewPopup.IsVisible)
        {
            _previewHideTimer.Start();
        }
        else if (_pendingRebuild && !WindowPickerPopup.IsOpen)
        {
            RebuildDock(force: true);
        }
    }

    private void StartSpringLoop()
    {
        if (_isSpringActive) return;
        _isSpringActive = true;
        CompositionTarget.Rendering += OnSpringFrame;
    }

    private void OnSpringFrame(object? sender, EventArgs e)
    {
        const double smoothing = 0.30;
        bool anyMoving = false;

        foreach (var v in _iconVisuals)
        {
            double ds = v.TargetScale - v.CurrentScale;
            double dl = v.TargetLift - v.CurrentLift;
            double dp = v.TargetSpread - v.CurrentSpread;

            if (Math.Abs(ds) > 0.002 || Math.Abs(dl) > 0.05 || Math.Abs(dp) > 0.05)
            {
                v.CurrentScale += ds * smoothing;
                v.CurrentLift += dl * smoothing;
                v.CurrentSpread += dp * smoothing;
                anyMoving = true;
            }
            else
            {
                v.CurrentScale = v.TargetScale;
                v.CurrentLift = v.TargetLift;
                v.CurrentSpread = v.TargetSpread;
            }

            v.Scale.ScaleX = v.CurrentScale;
            v.Scale.ScaleY = v.CurrentScale;
            v.Translate.Y = v.CurrentLift;
            v.Container.Margin = new Thickness(5 + v.CurrentSpread, 0, 5 + v.CurrentSpread, 0);
        }

        if (!anyMoving)
        {
            CompositionTarget.Rendering -= OnSpringFrame;
            _isSpringActive = false;
        }
    }

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        // Hide Dock completely when a fullscreen app (game, YouTube, F11) or Windows shell flyout is active
        if (WindowTracker.IsWindowsShellFlyoutActive() || WindowTracker.IsMonitorFullscreen(_currentMonitor))
        {
            if (!_isAutoHidden)
            {
                _isAutoHidden = true;
                TooltipPopup.IsOpen = false;
                WindowPickerPopup.IsOpen = false;
                _previewPopup.HideAndClear();
                if (IsVisible) Hide();
            }

            // Still allow the dock to follow the cursor if the user moves to another non-fullscreen monitor
            if (Win32Api.GetCursorPos(out var fsPt))
            {
                var otherMon = MonitorHelper.GetMonitorAtCursor();
                if (!string.Equals(otherMon.DeviceName, _currentMonitor.DeviceName, StringComparison.OrdinalIgnoreCase) &&
                    !WindowTracker.IsMonitorFullscreen(otherMon))
                {
                    _currentMonitor = otherMon;
                    _isAutoHidden = false;
                    Show();
                    Opacity = 1.0;
                    RepositionBottomCenter();
                }
            }
            return;
        }

        if (!Win32Api.GetCursorPos(out var pt)) return;

        var cursorMon = MonitorHelper.GetMonitorAtCursor();
        if (!string.Equals(cursorMon.DeviceName, _currentMonitor.DeviceName, StringComparison.OrdinalIgnoreCase) &&
            !IsMouseOver && !WindowPickerPopup.IsOpen && !WindowTracker.IsMonitorFullscreen(cursorMon))
        {
            _currentMonitor = cursorMon;
            _isAutoHidden = false;
            if (!IsVisible) Show();
            Opacity = 1.0;
            RepositionBottomCenter();
            return;
        }

        if (!_settings.DockAutoHide)
        {
            if (_isAutoHidden || !IsVisible)
            {
                _isAutoHidden = false;
                if (!IsVisible) Show();
                Opacity = 1.0;
                RepositionBottomCenter();
            }
            return;
        }

        int monBottomPx = _currentMonitor.PhysicalTop + _currentMonitor.PhysicalHeight;
        int thresholdPx = _isAutoHidden
            ? (int)Math.Round(12 * _currentMonitor.DpiScaleY)
            : (int)Math.Round((ActualHeight + 20) * _currentMonitor.DpiScaleY);

        bool nearBottom = pt.Y >= monBottomPx - thresholdPx;
        if (nearBottom && _isAutoHidden)
        {
            _isAutoHidden = false;
            if (!IsVisible) Show();
            Opacity = 1.0;
            RepositionBottomCenter();
        }
        else if (!nearBottom && !_isAutoHidden && !IsMouseOver && !WindowPickerPopup.IsOpen)
        {
            _isAutoHidden = true;
            TooltipPopup.IsOpen = false;
            Opacity = 0.0;
        }
    }

    public void RefreshMonitors()
    {
        var all = MonitorHelper.GetAllMonitors();
        _currentMonitor = all.FirstOrDefault(m => m.DeviceName == _currentMonitor.DeviceName)
                          ?? MonitorHelper.GetPrimaryMonitor();
        RepositionBottomCenter();
    }

    public void RepositionBottomCenter()
    {
        if (_isAutoHidden) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero && ActualWidth > 0 && ActualHeight > 0)
        {
            int widthPx = (int)Math.Round(ActualWidth * _currentMonitor.DpiScaleX);
            int heightPx = (int)Math.Round(ActualHeight * _currentMonitor.DpiScaleY);
            int marginBottomPx = (int)Math.Round(8 * _currentMonitor.DpiScaleY);
            int xPx = _currentMonitor.PhysicalLeft + (_currentMonitor.PhysicalWidth - widthPx) / 2;
            int yPx = _currentMonitor.PhysicalTop + _currentMonitor.PhysicalHeight - heightPx - marginBottomPx;
            Win32Api.SetWindowPos(
                hwnd,
                nint.Zero,
                xPx,
                yPx,
                0,
                0,
                Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_NOACTIVATE);
        }
        else
        {
            Left = _currentMonitor.LeftDip + (_currentMonitor.WidthDip - ActualWidth) / 2.0;
            Top = _currentMonitor.BottomDip - ActualHeight - 8;
        }
    }
}
