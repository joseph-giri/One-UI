namespace OneUI.Core.Models;

public sealed class DockItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public bool IsRunning { get; set; }
    public bool IsSpecial { get; set; }
    public nint WindowHandle { get; set; }
    public List<WindowInfo> Windows { get; set; } = [];

    public int WindowCount => Windows.Count;
    public bool HasMultipleWindows => Windows.Count > 1;
}
