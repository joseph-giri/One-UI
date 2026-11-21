using OneUI.Core.Models;
using OneUI.Core.Services;
using Xunit;

namespace OneUI.Core.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsAndClampsValues()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"oneui-test-{Guid.NewGuid():N}.json");
        try
        {
            var svc = new SettingsService(tempFile);
            var settings = new AppSettings
            {
                GlassTheme = "Light",
                DockIconSize = 999, // should clamp to 72
                HideWindowsTaskbar = false
            };

            svc.Save(settings);
            var loaded = svc.Load();

            Assert.Equal("Light", loaded.GlassTheme);
            Assert.Equal(72, loaded.DockIconSize);
            Assert.False(loaded.HideWindowsTaskbar);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"oneui-corrupt-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, "{ not valid json !!!");
            var svc = new SettingsService(tempFile);
            var loaded = svc.Load();

            Assert.Equal("Dark", loaded.GlassTheme);
            Assert.True(loaded.EnableDock);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
