using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace OneUI.App.Native;

/// <summary>
/// Applies hardware-accelerated Windows 11 Liquid Glass (DWM Acrylic backdrop + rounded corners)
/// and tool-window extended styles to WPF windows.
/// </summary>
public static class GlassHelper
{
    public static void PrepareLiquidGlass(Window window, bool roundedCorners = true, bool isToolWindow = true, bool noActivate = false)
    {
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.Background = Brushes.Transparent;

        var chrome = new WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(0)
        };
        WindowChrome.SetWindowChrome(window, chrome);

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == nint.Zero) return;

            ApplyWindowStyles(hwnd, isToolWindow, noActivate);
            ApplyDwmGlass(hwnd, isDark: true, roundedCorners: roundedCorners);
        };
    }

    public static void ApplyDwmGlass(nint hwnd, bool isDark, bool roundedCorners = true)
    {
        if (hwnd == nint.Zero) return;

        int darkMode = isDark ? 1 : 0;
        _ = Win32Api.DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        int cornerPref = roundedCorners ? Win32Api.DWMWCP_ROUND : Win32Api.DWMWCP_DONOTROUND;
        _ = Win32Api.DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        // Remove default opaque Windows border so our custom specular rim shines cleanly
        int noBorder = Win32Api.DWMWA_COLOR_NONE;
        _ = Win32Api.DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));

        // Windows 11 22H2+ native Acrylic backdrop (DWMSBT_TRANSIENTWINDOW = 3)
        int backdrop = Win32Api.DWMSBT_TRANSIENTWINDOW;
        int hr = Win32Api.DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

        // Fallback to SetWindowCompositionAttribute acrylic if needed
        if (hr != 0)
        {
            EnableLegacyAcrylic(hwnd, isDark ? 0x55181822u : 0x66F5F5FAu);
        }
    }

    public static void UpdateTheme(Window window, bool isDark, bool roundedCorners = true, bool blurEnabled = true)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero) return;

        if (blurEnabled)
        {
            ApplyDwmGlass(hwnd, isDark, roundedCorners);
        }
        else
        {
            SetBackdropEnabled(window, enabled: false, isDark: isDark, roundedCorners: roundedCorners);
        }
    }

    public static void SetBackdropEnabled(Window window, bool enabled, bool isDark = true, bool roundedCorners = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero) return;

        if (enabled)
        {
            ApplyDwmGlass(hwnd, isDark, roundedCorners: true);
        }
        else
        {
            int darkMode = isDark ? 1 : 0;
            _ = Win32Api.DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            int none = Win32Api.DWMSBT_NONE;
            _ = Win32Api.DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
            int cornerPref = roundedCorners ? Win32Api.DWMWCP_ROUND : Win32Api.DWMWCP_DONOTROUND;
            _ = Win32Api.DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
            int noBorder = Win32Api.DWMWA_COLOR_NONE;
            _ = Win32Api.DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
        }
    }

    private static void ApplyWindowStyles(nint hwnd, bool isToolWindow, bool noActivate)
    {
        int exStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
        if (isToolWindow)
        {
            exStyle |= Win32Api.WS_EX_TOOLWINDOW;
            exStyle &= ~Win32Api.WS_EX_APPWINDOW;
        }
        if (noActivate)
        {
            exStyle |= Win32Api.WS_EX_NOACTIVATE;
        }
        _ = Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, exStyle);
    }

    private static void EnableLegacyAcrylic(nint hwnd, uint abgrTint)
    {
        var accent = new Win32Api.AccentPolicy
        {
            AccentState = Win32Api.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 2,
            GradientColor = abgrTint,
            AnimationId = 0
        };

        int size = Marshal.SizeOf(accent);
        nint ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new Win32Api.WindowCompositionAttributeData
            {
                Attribute = Win32Api.WCA_ACCENT_POLICY,
                SizeOfData = size,
                Data = ptr
            };
            _ = Win32Api.SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
