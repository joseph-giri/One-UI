using System.Windows.Threading;
using OneUI.Core.Services;

namespace OneUI.App.Native;

public sealed class ClipboardHistoryService : IDisposable
{
    private readonly ClipboardHistoryStore _store = new(maxEntries: 20);
    private readonly DispatcherTimer _pollTimer;
    private string _lastSeen = string.Empty;

    public event Action? HistoryChanged;

    public ClipboardHistoryService()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _pollTimer.Tick += (_, _) => PollClipboard();
        _pollTimer.Start();
        PollClipboard();
    }

    public IReadOnlyList<ClipboardEntry> GetEntries(string? search = null) => _store.GetEntries(search);

    public void CopyToClipboard(string text)
    {
        try
        {
            _lastSeen = text;
            System.Windows.Clipboard.SetText(text);
            _store.Record(text);
            HistoryChanged?.Invoke();
        }
        catch { }
    }

    public void TogglePin(string id)
    {
        _store.TogglePin(id);
        HistoryChanged?.Invoke();
    }

    public void Remove(string id)
    {
        _store.Remove(id);
        HistoryChanged?.Invoke();
    }

    public void ClearUnpinned()
    {
        _store.ClearUnpinned();
        HistoryChanged?.Invoke();
    }

    private void PollClipboard()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText()) return;
            string text = System.Windows.Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (string.Equals(text, _lastSeen, StringComparison.Ordinal)) return;

            _lastSeen = text;
            if (_store.Record(text))
                HistoryChanged?.Invoke();
        }
        catch
        {
            // Clipboard can be briefly locked by another app; safely retry next tick
        }
    }

    public void Dispose() => _pollTimer.Stop();
}
