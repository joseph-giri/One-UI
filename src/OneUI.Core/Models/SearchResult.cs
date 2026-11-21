namespace OneUI.Core.Models;

public enum SearchResultKind
{
    Calculator,
    OpenWindow,
    Application,
    SystemCommand,
    WebSearch
}

public sealed class SearchResult
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string ActionTarget { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public string Glyph { get; set; } = string.Empty;
    public nint WindowHandle { get; set; }
    public SearchResultKind Kind { get; set; }
    public int Score { get; set; }
}
