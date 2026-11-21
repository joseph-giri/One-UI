using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OneUI.App.Native;
using OneUI.App.Theme;
using OneUI.Core.Models;
using OneUI.Core.Services;

namespace OneUI.App.Views;

public enum TopBarPopoverMode
{
    Focus,
    Reminders,
    Clipboard,
    Scratchpad
}

public partial class TopBarPopoverWindow : Window
{
    private readonly AppSettings _settings;
    private readonly FocusTimerService _focusTimer;
    private readonly ClipboardHistoryService _clipboard;
    private VoiceRecognitionService? _voice;
    private TopBarPopoverMode _mode;
    private bool _suppressScratchpad;
    private bool _suppressDeactivate;

    public event Action? DataChanged;

    public TopBarPopoverWindow(
        AppSettings settings,
        FocusTimerService focusTimer,
        ClipboardHistoryService clipboard)
    {
        InitializeComponent();
        _settings = settings;
        _focusTimer = focusTimer;
        _clipboard = clipboard;

        GlassHelper.PrepareLiquidGlass(this, roundedCorners: true, isToolWindow: true);

        _focusTimer.StateChanged += () => Dispatcher.BeginInvoke(RefreshFocusUi);
        _clipboard.HistoryChanged += () => Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible && _mode == TopBarPopoverMode.Clipboard)
                RebuildClipboardList();
        });
    }

    public void ToggleOpen(TopBarPopoverMode mode, MonitorBounds? monitor = null)
    {
        if (IsVisible && _mode == mode)
        {
            Hide();
            return;
        }

        _mode = mode;
        FocusPanel.Visibility = mode == TopBarPopoverMode.Focus ? Visibility.Visible : Visibility.Collapsed;
        RemindersPanel.Visibility = mode == TopBarPopoverMode.Reminders ? Visibility.Visible : Visibility.Collapsed;
        ClipboardPanel.Visibility = mode == TopBarPopoverMode.Clipboard ? Visibility.Visible : Visibility.Collapsed;
        ScratchpadPanel.Visibility = mode == TopBarPopoverMode.Scratchpad ? Visibility.Visible : Visibility.Collapsed;

        switch (mode)
        {
            case TopBarPopoverMode.Focus:
                RefreshFocusUi();
                break;
            case TopBarPopoverMode.Reminders:
                RebuildRemindersList();
                break;
            case TopBarPopoverMode.Clipboard:
                ClipSearchBox.Text = string.Empty;
                RebuildClipboardList();
                break;
            case TopBarPopoverMode.Scratchpad:
                _suppressScratchpad = true;
                ScratchpadBox.Text = _settings.DesktopNoteText ?? string.Empty;
                _suppressScratchpad = false;
                break;
        }

        var mon = monitor ?? MonitorHelper.GetMonitorAtCursor();
        Top = mon.TopDip + 34;
        Left = mode is TopBarPopoverMode.Focus or TopBarPopoverMode.Reminders
            ? mon.LeftDip + (mon.WidthDip - Width) / 2.0
            : mon.RightDip - Width - 180;

        Left = Math.Clamp(Left, mon.LeftDip + 10, mon.RightDip - Width - 10);

        Show();
        Activate();
        AnimationHelper.AnimatePopIn(RootCard, fromScale: 0.94, fromOffsetY: -8, durationMs: 180);

        if (mode == TopBarPopoverMode.Reminders)
            PopReminderInput.Focus();
        else if (mode == TopBarPopoverMode.Scratchpad)
            ScratchpadBox.Focus();
    }

    private void RefreshFocusUi()
    {
        FocusPhaseHeader.Text = _focusTimer.Phase == FocusPhase.Focus ? "FOCUS SESSION" : "BREAK TIME";
        FocusBigTimeText.Text = _focusTimer.FormattedRemaining;
        FocusSessionCountText.Text = $"{_focusTimer.CompletedFocusSessions} done";
        FocusStartPauseLabel.Text = _focusTimer.IsRunning
            ? "Pause"
            : _focusTimer.Phase == FocusPhase.Focus ? "Start Focus" : "Start Break";
        FocusStartPauseBtn.Background = _focusTimer.Phase == FocusPhase.Break
            ? new SolidColorBrush(Color.FromArgb(95, 48, 209, 88))
            : new SolidColorBrush(Color.FromArgb(95, 10, 132, 255));
    }

    private void FocusStartPause_Click(object sender, RoutedEventArgs e) => _focusTimer.ToggleStartPause();
    private void FocusReset_Click(object sender, RoutedEventArgs e) => _focusTimer.Reset();

    private void FocusPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string tag })
        {
            var parts = tag.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out int mins))
            {
                var phase = parts[0] == "Break" ? FocusPhase.Break : FocusPhase.Focus;
                _focusTimer.SetPreset(phase, mins, autoStart: true);
            }
        }
    }

    private void RebuildRemindersList()
    {
        _settings.Reminders ??= [];
        PopReminderItemsPanel.Children.Clear();

        int remaining = _settings.Reminders.Count(r => !r.IsCompleted);
        int done = _settings.Reminders.Count - remaining;
        PopReminderCount.Text = remaining == 0 ? "All done" : $"{remaining} left";
        PopClearDoneBtn.Visibility = done > 0 ? Visibility.Visible : Visibility.Collapsed;

        var now = DateTime.Now;
        var ordered = _settings.Reminders
            .OrderBy(r => r.IsCompleted)
            .ThenBy(r => r.DueAt ?? DateTime.MaxValue)
            .ToList();

        if (ordered.Count == 0)
        {
            var empty = new TextBlock { Text = "No reminders yet.", FontSize = 11.5, FontStyle = FontStyles.Italic, Margin = new Thickness(2, 6, 2, 6) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "GlassTextMutedBrush");
            PopReminderItemsPanel.Children.Add(empty);
            return;
        }

        foreach (var item in ordered)
        {
            bool isContinuous = item.RepeatIntervalMinutes is > 0;
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var checkBtn = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("GlassPillButton"),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 1, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Content = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = item.IsCompleted ? new SolidColorBrush(Color.FromRgb(10, 132, 255)) : Brushes.Transparent,
                    BorderBrush = isContinuous ? new SolidColorBrush(Color.FromRgb(48, 209, 88)) : new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                    BorderThickness = new Thickness(1.4),
                    Child = item.IsCompleted
                        ? new TextBlock { Text = "\uE73E", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, Foreground = Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                        : isContinuous
                            ? new TextBlock { Text = "\uE895", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 8.5, Foreground = new SolidColorBrush(Color.FromRgb(48, 209, 88)), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                            : null
                }
            };
            checkBtn.Click += (_, _) =>
            {
                if (!item.IsCompleted && (isContinuous || !string.IsNullOrEmpty(item.Recurrence)) && item.DueAt.HasValue)
                {
                    var next = NaturalLanguageReminderParser.ComputeNextOccurrence(isContinuous ? DateTime.Now : item.DueAt.Value, item.Recurrence, item.RepeatIntervalMinutes, DateTime.Now);
                    if (next.HasValue) { item.DueAt = next.Value; item.IsPaused = false; item.Notified = false; }
                    else item.IsCompleted = true;
                }
                else
                {
                    item.IsCompleted = !item.IsCompleted;
                }
                RebuildRemindersList();
                DataChanged?.Invoke();
            };
            Grid.SetColumn(checkBtn, 0);

            var stack = new StackPanel();
            var title = new TextBlock
            {
                Text = item.Text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null,
                Opacity = item.IsCompleted || item.IsPaused ? 0.55 : 1.0
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "GlassTextPrimaryBrush");
            stack.Children.Add(title);

            if (item.DueAt.HasValue)
            {
                var due = new TextBlock
                {
                    Text = item.IsPaused ? $"Paused \u00B7 {item.Recurrence}" : NaturalLanguageReminderParser.FormatDueLabel(item.DueAt.Value, now, item.Recurrence),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(isContinuous ? Color.FromRgb(48, 209, 88) : Color.FromRgb(100, 210, 255))
                };
                stack.Children.Add(due);
            }
            Grid.SetColumn(stack, 1);

            var delBtn = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("GlassPillButton"),
                Padding = new Thickness(4, 1, 4, 1),
                VerticalAlignment = VerticalAlignment.Top,
                Content = new TextBlock { Text = "\uE711", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, Opacity = 0.7 }
            };
            delBtn.Click += (_, _) =>
            {
                _settings.Reminders.Remove(item);
                RebuildRemindersList();
                DataChanged?.Invoke();
            };
            Grid.SetColumn(delBtn, 2);

            row.Children.Add(checkBtn);
            row.Children.Add(stack);
            row.Children.Add(delBtn);
            PopReminderItemsPanel.Children.Add(row);
        }
    }

    private void PopReminderInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        string raw = PopReminderInput.Text;
        PopReminderPlaceholder.Visibility = string.IsNullOrEmpty(raw) ? Visibility.Visible : Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(raw))
        {
            PopReminderPreviewBorder.Visibility = Visibility.Collapsed;
            return;
        }
        var parsed = NaturalLanguageReminderParser.Parse(raw);
        if (parsed.DueAt.HasValue)
        {
            PopReminderPreviewText.Text = $"{NaturalLanguageReminderParser.FormatDueLabel(parsed.DueAt.Value, DateTime.Now, parsed.Recurrence)} \u00B7 {parsed.Title}";
            PopReminderPreviewBorder.Visibility = Visibility.Visible;
        }
        else
        {
            PopReminderPreviewBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void PopReminderInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { AddPopReminder(); e.Handled = true; }
    }

    private void PopAddReminder_Click(object sender, RoutedEventArgs e) => AddPopReminder();

    private void AddPopReminder()
    {
        string raw = PopReminderInput.Text.Trim();
        if (string.IsNullOrEmpty(raw)) return;
        var parsed = NaturalLanguageReminderParser.Parse(raw);
        _settings.Reminders ??= [];
        _settings.Reminders.Insert(0, new ReminderItem
        {
            Text = parsed.Title,
            DueAt = parsed.DueAt,
            Recurrence = parsed.Recurrence,
            RepeatIntervalMinutes = parsed.RepeatIntervalMinutes
        });
        PopReminderInput.Text = string.Empty;
        PopReminderPreviewBorder.Visibility = Visibility.Collapsed;
        RebuildRemindersList();
        DataChanged?.Invoke();
    }

    private void PopClearDone_Click(object sender, RoutedEventArgs e)
    {
        _settings.Reminders?.RemoveAll(r => r.IsCompleted);
        RebuildRemindersList();
        DataChanged?.Invoke();
    }

    private async void PopVoice_Click(object sender, RoutedEventArgs e)
    {
        if (_voice is not null && _voice.IsListening) { _voice.Cancel(); return; }

        _voice ??= new VoiceRecognitionService();
        _voice.StatusChanged -= OnPopVoiceStatus;
        _voice.StatusChanged += OnPopVoiceStatus;

        _suppressDeactivate = true;
        PopVoiceBtn.Background = new SolidColorBrush(Color.FromArgb(200, 255, 69, 58));
        PopVoiceIcon.Text = "\uE71A";
        PopReminderPreviewText.Text = "Starting microphone...";
        PopReminderPreviewBorder.Visibility = Visibility.Visible;

        var result = await _voice.ListenOnceAsync();

        _suppressDeactivate = false;
        PopVoiceBtn.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        PopVoiceIcon.Text = "\uE720";

        if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
        {
            PopReminderInput.Text = result.Text;
            AddPopReminder();
        }
        else if (!string.IsNullOrEmpty(result.ErrorMessage) && result.ErrorMessage != "Cancelled")
        {
            PopReminderPreviewText.Text = result.ErrorMessage;
            PopReminderPreviewBorder.Visibility = Visibility.Visible;
        }
        else
        {
            PopReminderPreviewBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void OnPopVoiceStatus(string status)
    {
        Dispatcher.BeginInvoke(() =>
        {
            PopReminderPreviewText.Text = status;
            PopReminderPreviewBorder.Visibility = Visibility.Visible;
        });
    }

    private void RebuildClipboardList()
    {
        ClipboardItemsPanel.Children.Clear();
        var entries = _clipboard.GetEntries(ClipSearchBox.Text);

        if (entries.Count == 0)
        {
            var empty = new TextBlock { Text = "Copy any text to see it here.", FontSize = 11.5, FontStyle = FontStyles.Italic, Margin = new Thickness(2, 8, 2, 8) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "GlassTextMutedBrush");
            ClipboardItemsPanel.Children.Add(empty);
            return;
        }

        foreach (var entry in entries)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 6, 6, 6),
                Margin = new Thickness(0, 0, 0, 5),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string preview = entry.Text.Replace("\r", "").Replace("\n", " ↵ ");
            if (preview.Length > 90) preview = preview[..90] + "…";

            var txt = new TextBlock { Text = preview, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            txt.SetResourceReference(TextBlock.ForegroundProperty, "GlassTextPrimaryBrush");
            Grid.SetColumn(txt, 0);

            var pinBtn = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("GlassPillButton"),
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = entry.IsPinned ? "Unpin" : "Pin",
                Content = new TextBlock
                {
                    Text = entry.IsPinned ? "\uE735" : "\uE734",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 10.5,
                    Foreground = entry.IsPinned ? new SolidColorBrush(Color.FromRgb(255, 214, 10)) : Brushes.White
                }
            };
            pinBtn.Click += (_, args) => { args.Handled = true; _clipboard.TogglePin(entry.Id); };
            Grid.SetColumn(pinBtn, 1);

            var delBtn = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("GlassPillButton"),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(2, 0, 0, 0),
                ToolTip = "Remove",
                Content = new TextBlock { Text = "\uE711", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, Opacity = 0.7 }
            };
            delBtn.Click += (_, args) => { args.Handled = true; _clipboard.Remove(entry.Id); };
            Grid.SetColumn(delBtn, 2);

            row.Children.Add(txt);
            row.Children.Add(pinBtn);
            row.Children.Add(delBtn);
            card.Child = row;
            card.MouseLeftButtonUp += (_, _) =>
            {
                _clipboard.CopyToClipboard(entry.Text);
                Hide();
            };
            ClipboardItemsPanel.Children.Add(card);
        }
    }

    private void ClipSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ClipSearchPlaceholder.Visibility = string.IsNullOrEmpty(ClipSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        RebuildClipboardList();
    }

    private void ClearClipboard_Click(object sender, RoutedEventArgs e) => _clipboard.ClearUnpinned();

    private void ScratchpadBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressScratchpad) return;
        _settings.DesktopNoteText = ScratchpadBox.Text;
        DataChanged?.Invoke();
    }

    private void CopyScratchpad_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ScratchpadBox.Text))
            _clipboard.CopyToClipboard(ScratchpadBox.Text);
    }

    private void ClearScratchpad_Click(object sender, RoutedEventArgs e)
    {
        ScratchpadBox.Text = string.Empty;
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_suppressDeactivate) return;
        if (IsVisible) Hide();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Hide();
    }
}
