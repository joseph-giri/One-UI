using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace OneUI.App.Native;

public sealed record NowPlayingInfo(
    bool HasSession,
    string Title,
    string Artist,
    bool IsPlaying,
    ImageSource? Thumbnail);

/// <summary>
/// Reads real-time Now Playing metadata (track title, artist, album art, playback state)
/// from Windows 11's Global System Media Transport Controls (Spotify, YouTube, Edge/Chrome, VLC, etc.).
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private bool _disposed;

    public NowPlayingInfo Current { get; private set; } = new(false, "Nothing Playing", "Play media to see it here", false, null);

    public event Action<NowPlayingInfo>? NowPlayingChanged;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_manager is null || _disposed) return;

            _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;
            AttachSession(_manager.GetCurrentSession());
            await RefreshCurrentAsync();
        }
        catch { }
    }

    private void Manager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        AttachSession(sender.GetCurrentSession());
        _ = RefreshCurrentAsync();
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_currentSession is not null)
        {
            try
            {
                _currentSession.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
            }
            catch { }
        }

        _currentSession = session;

        if (_currentSession is not null)
        {
            try
            {
                _currentSession.MediaPropertiesChanged += Session_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += Session_PlaybackInfoChanged;
            }
            catch { }
        }
    }

    private void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => _ = RefreshCurrentAsync();

    private void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        => _ = RefreshCurrentAsync();

    public async Task RefreshCurrentAsync()
    {
        if (_disposed) return;

        var session = _currentSession ?? _manager?.GetCurrentSession();
        if (session is null)
        {
            Update(new NowPlayingInfo(false, "Nothing Playing", "Play media to see it here", false, null));
            return;
        }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            bool isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            string title = string.IsNullOrWhiteSpace(props?.Title) ? "Media Playing" : props.Title.Trim();
            string artist = !string.IsNullOrWhiteSpace(props?.Artist)
                ? props.Artist.Trim()
                : (!string.IsNullOrWhiteSpace(props?.AlbumArtist) ? props.AlbumArtist.Trim() : FormatSourceApp(session.SourceAppUserModelId));

            ImageSource? thumb = null;
            if (props?.Thumbnail is not null)
            {
                try
                {
                    using var ras = await props.Thumbnail.OpenReadAsync();
                    using var netStream = ras.AsStreamForRead();
                    using var ms = new MemoryStream();
                    await netStream.CopyToAsync(ms);
                    ms.Position = 0;

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 96;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    thumb = bmp;
                }
                catch { }
            }

            Update(new NowPlayingInfo(true, title, artist, isPlaying, thumb));
        }
        catch
        {
            Update(new NowPlayingInfo(false, "Nothing Playing", "Play media to see it here", false, null));
        }
    }

    public async void TogglePlayPause()
    {
        try
        {
            var session = _currentSession ?? _manager?.GetCurrentSession();
            if (session is not null)
            {
                await session.TryTogglePlayPauseAsync();
                return;
            }
        }
        catch { }
        SystemStatusService.MediaPlayPause();
    }

    public async void SkipNext()
    {
        try
        {
            var session = _currentSession ?? _manager?.GetCurrentSession();
            if (session is not null)
            {
                await session.TrySkipNextAsync();
                return;
            }
        }
        catch { }
        SystemStatusService.MediaNext();
    }

    public async void SkipPrevious()
    {
        try
        {
            var session = _currentSession ?? _manager?.GetCurrentSession();
            if (session is not null)
            {
                await session.TrySkipPreviousAsync();
                return;
            }
        }
        catch { }
        SystemStatusService.MediaPrevious();
    }

    private void Update(NowPlayingInfo info)
    {
        Current = info;
        NowPlayingChanged?.Invoke(info);
    }

    private static string FormatSourceApp(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "System Media";
        var lower = aumid.ToLowerInvariant();
        if (lower.Contains("chrome")) return "Google Chrome";
        if (lower.Contains("edge")) return "Microsoft Edge";
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("vlc")) return "VLC Media Player";
        return "System Media";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AttachSession(null);
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= Manager_CurrentSessionChanged;
            _manager = null;
        }
    }
}
