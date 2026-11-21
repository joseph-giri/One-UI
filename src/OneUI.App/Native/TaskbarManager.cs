namespace OneUI.App.Native;

/// <summary>
/// Safely and reversibly hides or restores the native Windows 11 taskbar
/// (primary Shell_TrayWnd and all multi-monitor Shell_SecondaryTrayWnd windows).
/// Never modifies system files or registry keys.
/// </summary>
public sealed class TaskbarManager : IDisposable
{
    private bool _isHiddenByOneUI;

    public bool IsTaskbarHidden => _isHiddenByOneUI;

    public void SetTaskbarHidden(bool hide)
    {
        if (hide)
            HideTaskbar();
        else
            RestoreTaskbar();
    }

    public void HideTaskbar()
    {
        ToggleTaskbars(Win32Api.SW_HIDE);
        _isHiddenByOneUI = true;
    }

    public void RestoreTaskbar()
    {
        ToggleTaskbars(Win32Api.SW_SHOW);
        _isHiddenByOneUI = false;
    }

    private static void ToggleTaskbars(int showCommand)
    {
        var primary = Win32Api.FindWindow("Shell_TrayWnd", null);
        if (primary != nint.Zero)
        {
            Win32Api.ShowWindow(primary, showCommand);
        }

        // Also handle secondary monitors
        nint secondary = nint.Zero;
        while (true)
        {
            secondary = Win32Api.FindWindowEx(nint.Zero, secondary, "Shell_SecondaryTrayWnd", null);
            if (secondary == nint.Zero) break;
            Win32Api.ShowWindow(secondary, showCommand);
        }
    }

    public void Dispose()
    {
        if (_isHiddenByOneUI)
        {
            RestoreTaskbar();
        }
    }
}
