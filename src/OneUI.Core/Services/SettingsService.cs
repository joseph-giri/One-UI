using System.Text.Json;
using OneUI.Core.Models;

namespace OneUI.Core.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public SettingsService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _settingsPath = customPath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _settingsPath = Path.Combine(appData, "OneUI", "settings.json");
        }
    }

    public string SettingsPath => _settingsPath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(_settingsPath);
            var parsed = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (parsed is null)
            {
                return new AppSettings();
            }

            // Clamp numeric values to safe ranges
            parsed.DockIconSize = Math.Clamp(parsed.DockIconSize, 32, 72);
            parsed.DockMagnificationScale = Math.Clamp(parsed.DockMagnificationScale, 1.0, 2.0);
            parsed.GlassTintOpacity = Math.Clamp(parsed.GlassTintOpacity, 0.08, 0.80);
            parsed.PinnedApps ??= AppSettings.DefaultPinnedApps();
            parsed.TrafficLightExcludedProcesses ??= new AppSettings().TrafficLightExcludedProcesses;

            return parsed;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
