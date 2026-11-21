namespace OneUI.Core.Services;

public sealed class ClipboardEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; init; } = string.Empty;
    public DateTime CopiedAt { get; set; } = DateTime.Now;
    public bool IsPinned { get; set; }
}

public sealed class ClipboardHistoryStore
{
    private readonly List<ClipboardEntry> _entries = [];
    public int MaxEntries { get; }

    public ClipboardHistoryStore(int maxEntries = 20)
    {
        MaxEntries = Math.Max(5, maxEntries);
    }

    public IReadOnlyList<ClipboardEntry> GetEntries(string? searchQuery = null)
    {
        IEnumerable<ClipboardEntry> q = _entries
            .OrderByDescending(e => e.IsPinned)
            .ThenByDescending(e => e.CopiedAt);

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            string term = searchQuery.Trim();
            q = q.Where(e => e.Text.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return q.ToList();
    }

    public bool Record(string? text, DateTime? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Length > 4000) trimmed = trimmed[..4000];

        var now = timestamp ?? DateTime.Now;

        // Deduplicate: move existing identical entry to top
        var existing = _entries.FirstOrDefault(e => string.Equals(e.Text, trimmed, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.CopiedAt = now;
            return false;
        }

        _entries.Insert(0, new ClipboardEntry { Text = trimmed, CopiedAt = now });
        TrimToMax();
        return true;
    }

    public void TogglePin(string id)
    {
        var item = _entries.FirstOrDefault(e => e.Id == id);
        if (item is not null) item.IsPinned = !item.IsPinned;
    }

    public void Remove(string id) => _entries.RemoveAll(e => e.Id == id);

    public void ClearUnpinned() => _entries.RemoveAll(e => !e.IsPinned);

    private void TrimToMax()
    {
        while (_entries.Count > MaxEntries)
        {
            var oldestUnpinned = _entries
                .Where(e => !e.IsPinned)
                .OrderBy(e => e.CopiedAt)
                .FirstOrDefault();

            if (oldestUnpinned is null) break;
            _entries.Remove(oldestUnpinned);
        }
    }
}
