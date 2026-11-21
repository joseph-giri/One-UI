using System.Net.Http;
using System.Text.Json;

namespace OneUI.App.Native;

public sealed record WeatherForecastDay(string DayName, int WeatherCode, double MaxTempC, double MinTempC);

public sealed record WeatherSnapshot(
    string LocationName,
    double TemperatureC,
    double ApparentTempC,
    int HumidityPercent,
    double WindSpeedKmh,
    int WeatherCode,
    bool IsDay,
    List<WeatherForecastDay> Forecast,
    DateTime UpdatedAt,
    string? ErrorMessage = null)
{
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}

public sealed class WeatherService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static WeatherService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("OneUI-Desktop/1.0");
    }

    public async Task<WeatherSnapshot> FetchAsync(string? customCity, CancellationToken ct = default)
    {
        try
        {
            double lat;
            double lon;
            string locationName;

            if (!string.IsNullOrWhiteSpace(customCity))
            {
                var geo = await GeocodeCityAsync(customCity.Trim(), ct);
                if (geo is null)
                    return Error($"City \"{customCity.Trim()}\" not found");
                (lat, lon, locationName) = geo.Value;
            }
            else
            {
                var auto = await DetectLocationByIpAsync(ct);
                if (auto is null)
                    return Error("Unable to detect location");
                (lat, lon, locationName) = auto.Value;
            }

            string url = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0:F4}&longitude={1:F4}&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m&daily=temperature_2m_max,temperature_2m_min,weather_code&forecast_days=4&timezone=auto",
                lat, lon);

            using var resp = await Http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var current = root.GetProperty("current");
            double temp = current.GetProperty("temperature_2m").GetDouble();
            double feels = current.GetProperty("apparent_temperature").GetDouble();
            int humidity = (int)Math.Round(current.GetProperty("relative_humidity_2m").GetDouble());
            double wind = current.GetProperty("wind_speed_10m").GetDouble();
            int code = current.GetProperty("weather_code").GetInt32();
            bool isDay = current.GetProperty("is_day").GetInt32() == 1;

            var forecast = new List<WeatherForecastDay>();
            if (root.TryGetProperty("daily", out var daily))
            {
                var times = daily.GetProperty("time");
                var maxs = daily.GetProperty("temperature_2m_max");
                var mins = daily.GetProperty("temperature_2m_min");
                var codes = daily.GetProperty("weather_code");

                int count = Math.Min(times.GetArrayLength(), 4);
                for (int i = 0; i < count; i++)
                {
                    string dateStr = times[i].GetString() ?? "";
                    string dayName = i == 0
                        ? "Today"
                        : DateTime.TryParse(dateStr, out var dt) ? dt.ToString("ddd") : dateStr;

                    forecast.Add(new WeatherForecastDay(
                        dayName,
                        codes[i].GetInt32(),
                        maxs[i].GetDouble(),
                        mins[i].GetDouble()));
                }
            }

            return new WeatherSnapshot(locationName, temp, feels, humidity, wind, code, isDay, forecast, DateTime.Now);
        }
        catch (Exception ex)
        {
            return Error(ex is TaskCanceledException ? "Request timed out" : "Weather unavailable");
        }
    }

    private static async Task<(double Lat, double Lon, string Name)?> GeocodeCityAsync(string city, CancellationToken ct)
    {
        string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json";
        using var resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;

        var first = results[0];
        double lat = first.GetProperty("latitude").GetDouble();
        double lon = first.GetProperty("longitude").GetDouble();
        string name = first.GetProperty("name").GetString() ?? city;
        if (first.TryGetProperty("country_code", out var cc) && !string.IsNullOrWhiteSpace(cc.GetString()))
            name = $"{name}, {cc.GetString()}";
        return (lat, lon, name);
    }

    private static async Task<(double Lat, double Lon, string Name)?> DetectLocationByIpAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await Http.GetAsync("https://ipwho.is/", ct);
            if (resp.IsSuccessStatusCode)
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;
                if (root.TryGetProperty("success", out var ok) && ok.GetBoolean())
                {
                    double lat = root.GetProperty("latitude").GetDouble();
                    double lon = root.GetProperty("longitude").GetDouble();
                    string city = root.TryGetProperty("city", out var c) ? (c.GetString() ?? "Local") : "Local";
                    return (lat, lon, city);
                }
            }
        }
        catch { }

        try
        {
            using var resp = await Http.GetAsync("https://get.geojs.io/v1/ip/geo.json", ct);
            if (resp.IsSuccessStatusCode)
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;
                if (double.TryParse(root.GetProperty("latitude").GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lat) &&
                    double.TryParse(root.GetProperty("longitude").GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lon))
                {
                    string city = root.TryGetProperty("city", out var c) ? (c.GetString() ?? "Local") : "Local";
                    return (lat, lon, city);
                }
            }
        }
        catch { }

        return null;
    }

    private static WeatherSnapshot Error(string message) =>
        new("Weather", 0, 0, 0, 0, 0, true, [], DateTime.Now, message);

    public static (string Glyph, string Description, string AccentHex) DescribeWmoCode(int code, bool isDay) => code switch
    {
        0 => isDay ? ("\uE706", "Clear Sky", "#FFFFD60A") : ("\uE708", "Clear Night", "#FF9BB8FF"),
        1 => isDay ? ("\uE706", "Mostly Clear", "#FFFFD60A") : ("\uE708", "Mostly Clear", "#FF9BB8FF"),
        2 => ("\uE753", "Partly Cloudy", "#FFB8E6FF"),
        3 => ("\uE753", "Overcast", "#FFAEC2D6"),
        45 or 48 => ("\uE753", "Foggy", "#FFB0BEC5"),
        51 or 53 or 55 => ("\uE9C0", "Drizzle", "#FF64D2FF"),
        56 or 57 => ("\uE9C0", "Freezing Drizzle", "#FF64D2FF"),
        61 or 63 or 65 => ("\uE9C0", "Rain", "#FF0A84FF"),
        66 or 67 => ("\uE9C0", "Freezing Rain", "#FF64D2FF"),
        71 or 73 or 75 or 77 => ("\uE9C8", "Snow", "#FFE6F4FF"),
        80 or 81 or 82 => ("\uE9C0", "Rain Showers", "#FF0A84FF"),
        85 or 86 => ("\uE9C8", "Snow Showers", "#FFE6F4FF"),
        95 => ("\uE945", "Thunderstorm", "#FFFF9F0A"),
        96 or 99 => ("\uE945", "Thunderstorm & Hail", "#FFFF453A"),
        _ => ("\uE753", "Cloudy", "#FFB8E6FF")
    };

    public static double FormatTemp(double tempC, bool useFahrenheit) =>
        useFahrenheit ? (tempC * 9.0 / 5.0) + 32.0 : tempC;
}
