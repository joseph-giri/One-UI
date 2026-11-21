namespace OneUI.Core.Models;

public sealed class WindowInfo
{
    public nint Handle { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public bool IsMinimized { get; set; }
    public bool IsMaximized { get; set; }
    public bool IsFullScreen { get; set; }

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
