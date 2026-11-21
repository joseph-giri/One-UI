using System.Windows;
using System.Windows.Media;
using OneUI.App.Native;
using OneUI.Core.Models;

namespace OneUI.App.Theme;

public static class ThemeManager
{
    public static void Apply(AppSettings settings)
    {
        bool isDark = !string.Equals(settings.GlassTheme, "Light", StringComparison.OrdinalIgnoreCase);
        bool blurEnabled = settings.EnableGlassBlur;
        // Keep minimum alpha >= 2 so DWM composition never flashes an uninitialized 0-alpha frame
        double clampedOpacity = Math.Clamp(settings.GlassTintOpacity, 0.0, 0.75);
        byte alpha = (byte)Math.Clamp((int)Math.Round(clampedOpacity * 255), 2, 192);

        var res = System.Windows.Application.Current.Resources;

        if (isDark)
        {
            byte dockAlpha = (byte)Math.Clamp(alpha + (alpha > 0 ? 10 : 0), 0, 192);
            byte cardAlpha = (byte)Math.Clamp(18 + (int)(clampedOpacity * 34), 14, 48);
            res["GlassSurfaceBrush"] = new SolidColorBrush(Color.FromArgb(alpha, 14, 16, 24));
            res["DockPillSurfaceBrush"] = new SolidColorBrush(Color.FromArgb(dockAlpha, 16, 18, 28));
            res["GlassCardBrush"] = new SolidColorBrush(Color.FromArgb(cardAlpha, 255, 255, 255));
            res["GlassCardHoverBrush"] = new SolidColorBrush(Color.FromArgb((byte)Math.Min(cardAlpha + 24, 80), 255, 255, 255));
            res["GlassSidebarBrush"] = new SolidColorBrush(Color.FromArgb((byte)Math.Min(alpha + 14, 64), 0, 0, 0));
            res["GlassTextPrimaryBrush"] = new SolidColorBrush(Color.FromArgb(248, 255, 255, 255));
            res["GlassTextSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(185, 225, 232, 248));
            res["GlassTextMutedBrush"] = new SolidColorBrush(Color.FromArgb(125, 210, 220, 240));
            res["GlassDividerBrush"] = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
            res["GlassTooltipBgBrush"] = new SolidColorBrush(Color.FromArgb(232, 20, 22, 32));
            res["GlassMenuBgBrush"] = new SolidColorBrush(Color.FromArgb(240, 22, 24, 34));
        }
        else
        {
            byte lightAlpha = (byte)Math.Clamp((int)Math.Round(clampedOpacity * 255), 0, 200);
            byte lightCardAlpha = (byte)Math.Clamp(40 + (int)(clampedOpacity * 130), 36, 155);
            res["GlassSurfaceBrush"] = new SolidColorBrush(Color.FromArgb(lightAlpha, 245, 247, 253));
            res["DockPillSurfaceBrush"] = new SolidColorBrush(Color.FromArgb(lightAlpha, 246, 248, 254));
            res["GlassCardBrush"] = new SolidColorBrush(Color.FromArgb(lightCardAlpha, 255, 255, 255));
            res["GlassCardHoverBrush"] = new SolidColorBrush(Color.FromArgb((byte)Math.Min(lightCardAlpha + 48, 210), 255, 255, 255));
            res["GlassSidebarBrush"] = new SolidColorBrush(Color.FromArgb((byte)Math.Min(lightAlpha / 3 + 10, 42), 0, 0, 0));
            res["GlassTextPrimaryBrush"] = new SolidColorBrush(Color.FromArgb(245, 16, 18, 26));
            res["GlassTextSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(175, 55, 62, 78));
            res["GlassTextMutedBrush"] = new SolidColorBrush(Color.FromArgb(125, 75, 82, 98));
            res["GlassDividerBrush"] = new SolidColorBrush(Color.FromArgb(32, 0, 0, 0));
            res["GlassTooltipBgBrush"] = new SolidColorBrush(Color.FromArgb(238, 248, 250, 254));
            res["GlassMenuBgBrush"] = new SolidColorBrush(Color.FromArgb(244, 248, 250, 255));
        }

        foreach (Window win in System.Windows.Application.Current.Windows)
        {
            if (win.AllowsTransparency || win is Views.LiveWallpaperWindow) continue;
            if (win is Views.DesktopWidgetWindow widget && widget.IsTransparentSurface) continue;

            bool rounded = win is not Views.MenuBarWindow;
            GlassHelper.UpdateTheme(win, isDark, roundedCorners: rounded, blurEnabled: blurEnabled);
        }
    }
}
