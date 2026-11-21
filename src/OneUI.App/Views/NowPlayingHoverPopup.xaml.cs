using System.Windows;
using System.Windows.Threading;
using OneUI.App.Native;
using OneUI.App.Theme;

namespace OneUI.App.Views;

public partial class NowPlayingHoverPopup : Window
{
    private readonly MediaSessionService _media;
    private readonly DispatcherTimer _hideTimer;
    private bool _pillHovered;
    private bool _popupHovered;

    public NowPlayingHoverPopup(MediaSessionService media)
    {
        InitializeComponent();
        _media = media;
        GlassHelper.PrepareLiquidGlass(this, roundedCorners: true, isToolWindow: true, noActivate: true);

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_pillHovered && !_popupHovered && IsVisible)
                Hide();
        };

        _media.NowPlayingChanged += info => Dispatcher.BeginInvoke(() => UpdateInfo(info));
        UpdateInfo(_media.Current);
    }

    public void OnPillHoverChanged(bool isHovering, double pillCenterXDip, MonitorBounds monitor)
    {
        _pillHovered = isHovering;

        if (isHovering)
        {
            _hideTimer.Stop();
            var info = _media.Current;
            if (string.IsNullOrWhiteSpace(info.Title) ||
                string.Equals(info.Title, "Nothing Playing", StringComparison.OrdinalIgnoreCase))
                return;

            UpdateInfo(info);
            Left = Math.Clamp(pillCenterXDip - Width / 2.0, monitor.LeftDip + 10, monitor.RightDip - Width - 10);
            Top = monitor.TopDip + 32;

            if (!IsVisible)
            {
                Show();
                AnimationHelper.AnimatePopIn(RootCard, fromScale: 0.94, fromOffsetY: -6, durationMs: 150);
            }
        }
        else
        {
            ScheduleHideCheck();
        }
    }

    private void UpdateInfo(NowPlayingInfo info)
    {
        TrackTitleText.Text = info.Title;
        TrackArtistText.Text = info.Artist;
        PlayPauseIcon.Text = info.IsPlaying ? "\uE769" : "\uE768";
        AlbumArtImage.Source = info.Thumbnail;
        FallbackIcon.Visibility = info.Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Popup_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _popupHovered = true;
        _hideTimer.Stop();
    }

    private void Popup_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _popupHovered = false;
        ScheduleHideCheck();
    }

    private void ScheduleHideCheck()
    {
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => _media.SkipPrevious();
    private void PlayPause_Click(object sender, RoutedEventArgs e) => _media.TogglePlayPause();
    private void Next_Click(object sender, RoutedEventArgs e) => _media.SkipNext();
}
