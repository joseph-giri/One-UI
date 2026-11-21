using System.Windows.Interop;

namespace OneUI.App.Native;

/// <summary>
/// Registers global Windows hotkeys via a lightweight message-only HwndSource:
/// - Alt+Space: Toggle Spotlight Quick Launcher
/// - Ctrl+Alt+Shift+R: Emergency restore native Windows taskbar
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HOTKEY_SPOTLIGHT = 9001;
    private const int HOTKEY_EMERGENCY_RESTORE = 9002;

    private HwndSource? _source;
    private bool _disposed;

    public event Action? SpotlightPressed;
    public event Action? EmergencyRestorePressed;
    public event Action? QuitRequested;

    public void Start()
    {
        var parameters = new HwndSourceParameters("OneUI_HotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var hwnd = _source.Handle;
        Win32Api.RegisterHotKey(hwnd, HOTKEY_SPOTLIGHT, Win32Api.MOD_ALT | Win32Api.MOD_NOREPEAT, Win32Api.VK_SPACE);
        Win32Api.RegisterHotKey(
            hwnd,
            HOTKEY_EMERGENCY_RESTORE,
            Win32Api.MOD_CONTROL | Win32Api.MOD_ALT | Win32Api.MOD_SHIFT | Win32Api.MOD_NOREPEAT,
            Win32Api.VK_R);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32Api.WM_CLOSE)
        {
            QuitRequested?.Invoke();
            handled = true;
            return nint.Zero;
        }
        if (msg == Win32Api.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_SPOTLIGHT)
            {
                SpotlightPressed?.Invoke();
                handled = true;
            }
            else if (id == HOTKEY_EMERGENCY_RESTORE)
            {
                EmergencyRestorePressed?.Invoke();
                handled = true;
            }
        }
        return nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_source is not null)
        {
            var hwnd = _source.Handle;
            if (hwnd != nint.Zero)
            {
                Win32Api.UnregisterHotKey(hwnd, HOTKEY_SPOTLIGHT);
                Win32Api.UnregisterHotKey(hwnd, HOTKEY_EMERGENCY_RESTORE);
            }
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
