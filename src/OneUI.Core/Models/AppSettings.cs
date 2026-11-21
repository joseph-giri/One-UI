namespace OneUI.Core.Models;

public sealed class AppSettings
{
    public bool EnableDock { get; set; } = true;
    public bool EnableMenuBar { get; set; } = true;
    public bool EnableSpotlight { get; set; } = true;
    public bool EnableTrafficLights { get; set; } = false;
    public bool HideWindowsTaskbar { get; set; } = true;
    public bool LaunchAtStartup { get; set; } = false;

    public double DockIconSize { get; set; } = 46;
    public double DockMagnificationScale { get; set; } = 1.5;
    public bool DockAutoHide { get; set; } = false;

    /// <summary>
    /// "Dark" or "Light" Liquid Glass appearance.
    /// </summary>
    public string GlassTheme { get; set; } = "Dark";

    /// <summary>
    /// Opacity of the translucent tint layer over DWM Acrylic blur (0.10 to 0.75).
    /// </summary>
    public double GlassTintOpacity { get; set; } = 0.30;

    public List<PinnedAppConfig> PinnedApps { get; set; } = DefaultPinnedApps();

    public List<string> TrafficLightExcludedProcesses { get; set; } =
    [
        "OneUI.App",
        "explorer",
        "SearchHost",
        "StartMenuExperienceHost",
        "ShellExperienceHost",
        "LockApp",
        "Taskmgr"
    ];

    // Live Wallpaper settings
    public bool EnableLiveWallpaper { get; set; } = false;
    /// <summary>
    /// "Aurora", "LiquidMesh", "Nebula", or "Video"
    /// </summary>
    public string WallpaperMode { get; set; } = "Aurora";
    public string WallpaperVideoPath { get; set; } = string.Empty;
    public bool PauseWallpaperOnFullscreen { get; set; } = true;

    // Desktop Widgets settings
    public bool EnableClockWidget { get; set; } = true;
    public bool EnableSystemWidget { get; set; } = true;
    public bool EnableNotesWidget { get; set; } = false;
    public bool EnableMediaWidget { get; set; } = false;
    public bool EnableWeatherWidget { get; set; } = true;
    public bool EnableCalendarWidget { get; set; } = true;
    public bool EnableReminderWidget { get; set; } = true;
    public string WeatherCity { get; set; } = string.Empty;
    public bool WeatherUseFahrenheit { get; set; } = false;
    public string DesktopNoteText { get; set; } = "Welcome to OneUI!\nClick here to jot down quick notes...";
    public List<ReminderItem> Reminders { get; set; } = DefaultReminders();
    public ClockWidgetConfig ClockWidget { get; set; } = new();
    public Dictionary<string, WidgetPosition> WidgetPositions { get; set; } = new();

    public static List<ReminderItem> DefaultReminders() =>
    [
        new() { Text = "Review today's schedule" },
        new() { Text = "Try customizing your OneUI widgets" }
    ];

    public static List<PinnedAppConfig> DefaultPinnedApps() =>
    [
        new() { Name = "File Explorer", ExecutablePath = "explorer.exe", Arguments = "" },
        new() { Name = "Microsoft Edge", ExecutablePath = "msedge.exe", Arguments = "" },
        new() { Name = "Terminal", ExecutablePath = "wt.exe", Arguments = "" },
        new() { Name = "Settings", ExecutablePath = "ms-settings:", Arguments = "" },
        new() { Name = "Notepad", ExecutablePath = "notepad.exe", Arguments = "" },
        new() { Name = "Calculator", ExecutablePath = "calc.exe", Arguments = "" }
    ];
}

public sealed class PinnedAppConfig
{
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}

public sealed class WidgetPosition
{
    public double Left { get; set; }
    public double Top { get; set; }
}

public sealed class ReminderItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? DueAt { get; set; }
    public string? Recurrence { get; set; }
    public bool Notified { get; set; }
}

public sealed class ClockWidgetConfig
{
    /// <summary>
    /// "Digital", "Stacked", or "Analog"
    /// </summary>
    public string Style { get; set; } = "Digital";
    public bool Use24Hour { get; set; } = false;
    public bool ShowSeconds { get; set; } = true;
    public bool ShowDate { get; set; } = true;
    public string DateFormat { get; set; } = "dddd, MMMM d";
    public string FontFamily { get; set; } = "Segoe UI Variable Display";
    public string FontWeight { get; set; } = "Light";
    public double FontSize { get; set; } = 48;
    public string TextColorHex { get; set; } = "#FAFFFFFF";
    /// <summary>
    /// "Glass", "Minimal", or "Transparent"
    /// </summary>
    public string BackgroundStyle { get; set; } = "Glass";
    /// <summary>
    /// "Left", "Center", or "Right"
    /// </summary>
    public string Alignment { get; set; } = "Left";

    // Shadow customization
    public bool EnableShadow { get; set; } = true;
    public string ShadowColorHex { get; set; } = "#FF000000";
    public double ShadowBlurRadius { get; set; } = 14;
    public double ShadowDepth { get; set; } = 2;
    public double ShadowOpacity { get; set; } = 0.65;
}
