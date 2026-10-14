using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
namespace FluentControl.Services;

// Pure data: neither a preset nor a subscription can install code or write hardware.
public sealed class AdaptivePoint
{
    public double X { get; set; }
    public double Brightness { get; set; }
}
public sealed class AdaptivePreset
{
    public string Format { get; set; } = "fluentcontrol.adaptive-preset";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Axis { get; set; } = "ambient";
    public List<AdaptivePoint> Points { get; set; } = new();
}
public sealed class AdaptiveSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public AdaptivePreset Preset { get; set; } = new();
    public override string ToString() => Preset.Name;
}
public sealed class AdaptiveSettings
{
    public bool Enabled { get; set; }
    public bool ThemeEnabled { get; set; }
    public string Mode { get; set; } = "ambient";
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public double Latitude { get; set; } = 30.5928;
    public double Longitude { get; set; } = 114.3055;
    public double DayBrightness { get; set; } = 65;
    public double NightBrightness { get; set; } = 20;
    public double LightThemeHour { get; set; } = 7;
    public double DarkThemeHour { get; set; } = 20;
    public string ActiveSourceId { get; set; } = "simulation";
    public List<LightSourceSubscription> LightSources { get; set; } = new();
    public AdaptivePreset TimeCurve { get; set; } = AdaptivePresets.Default("clock");
    public bool PreviewEnabled { get; set; }
    public string Source { get; set; } = "simulation";
    public string TargetDisplayId { get; set; } = "";
    public AdaptivePreset Curve { get; set; } = AdaptivePresets.Default("ambient");
    public List<AdaptiveSubscription> Subscriptions { get; set; } = new();
}
public static class AdaptivePresets
{
    public const int MaximumBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = 12
    };
    public static AdaptivePreset Default(string axis) => new()
    {
        Name = axis == "clock" ? "24h" : "Lux", Axis = axis,
        Points = (axis == "clock" ? new[] { (0d, 20d), (7d, 40d), (12d, 65d), (18d, 45d), (22d, 25d), (24d, 20d) }
            : new[] { (0d, 10d), (10d, 20d), (100d, 40d), (500d, 65d), (2000d, 85d), (10000d, 100d) })
            .Select(x => new AdaptivePoint { X = x.Item1, Brightness = x.Item2 }).ToList()
    };
    public static void Validate(AdaptivePreset p)
    {
        if (p is null || p.Format != "fluentcontrol.adaptive-preset" || p.Version != 1 || p.Axis is not ("ambient" or "clock") ||
            string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 100 || p.Points is null || p.Points.Count is < 2 or > 64)
            throw new InvalidDataException("Invalid adaptive preset.");
        var maximum = p.Axis == "clock" ? 24d : 10000d;
        double previous = -1;
        foreach (var point in p.Points)
        {
            if (point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Brightness) || point.X < 0 || point.X > maximum ||
                (previous >= 0 && point.X - previous < .01 - 1e-9) || point.Brightness < 0 || point.Brightness > 100) throw new InvalidDataException("Invalid curve points.");
            previous = point.X;
        }
        if (p.Points[0].X != 0 || p.Points[^1].X != maximum || (p.Axis == "clock" && p.Points[0].Brightness != p.Points[^1].Brightness))
            throw new InvalidDataException("Invalid curve endpoints.");
    }
    public static AdaptivePreset Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Preset is too large.");
        var p = JsonSerializer.Deserialize<AdaptivePreset>(json, Options) ?? throw new InvalidDataException("Empty preset.");
        Validate(p); return p;
    }
    public static string Export(AdaptivePreset p) { Validate(p); return JsonSerializer.Serialize(p, Options); }
    public static AdaptivePreset Copy(AdaptivePreset p) => Parse(Export(p));
    public static double Position(string axis, double x) => axis == "clock" ? x / 24 : Math.Log10(1 + x) / Math.Log10(10001);
    public static double Input(string axis, double position) => axis == "clock" ? Math.Clamp(position, 0, 1) * 24 : Math.Pow(10001, Math.Clamp(position, 0, 1)) - 1;
    public static double Evaluate(AdaptivePreset p, double input)
    {
        Validate(p);
        if (!double.IsFinite(input)) throw new ArgumentOutOfRangeException(nameof(input));
        input = p.Axis == "clock" ? ((input % 24) + 24) % 24 : Math.Clamp(input, 0, 10000);
        for (var i = 1; i < p.Points.Count; i++)
        {
            var a = p.Points[i - 1]; var b = p.Points[i];
            if (input > b.X) continue;
            var f = (Position(p.Axis, input) - Position(p.Axis, a.X)) / (Position(p.Axis, b.X) - Position(p.Axis, a.X));
            return a.Brightness + (b.Brightness - a.Brightness) * f;
        }
        return p.Points[^1].Brightness;
    }
    public static Uri SubscriptionUri(string text)
    {
        if (text.Length > 2048 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || string.IsNullOrEmpty(uri.Host)) throw new InvalidDataException("Invalid HTTPS subscription URL.");
        return uri;
    }
    public static async Task<AdaptivePreset> DownloadAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        var uri = SubscriptionUri(url);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumBytes) throw new InvalidDataException("Preset is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > MaximumBytes) throw new InvalidDataException("Preset is too large.");
            output.Write(buffer, 0, count);
        }
        return Parse(new System.Text.UTF8Encoding(false, true).GetString(output.ToArray()));
    }
    public static void Normalize(AdaptiveSettings s)
    {
        try { Validate(s.Curve); } catch { s.Curve = Default("ambient"); s.PreviewEnabled = false; }
        try { Validate(s.TimeCurve); if (s.TimeCurve.Axis != "clock") throw new InvalidDataException(); } catch { s.TimeCurve = Default("clock"); s.Enabled = false; }
        if (s.Curve.Axis == "clock") { s.TimeCurve = s.Curve; s.Curve = Default("ambient"); }
        if (s.Mode is not ("ambient" or "clock" or "solar")) { s.Mode = "ambient"; s.Enabled = false; }
        try { TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId); } catch { s.TimeZoneId = TimeZoneInfo.Local.Id; s.Enabled = false; }
        if (!double.IsFinite(s.Latitude) || !double.IsFinite(s.Longitude) || Math.Abs(s.Latitude) > 90 || Math.Abs(s.Longitude) > 180) { s.Latitude = 30.5928; s.Longitude = 114.3055; s.Enabled = false; }
        static double Limit(double v, double fallback, double max) => double.IsFinite(v) ? Math.Clamp(v, 0, max) : fallback;
        s.DayBrightness = Limit(s.DayBrightness, 65, 100); s.NightBrightness = Limit(s.NightBrightness, 20, 100);
        s.LightThemeHour = Limit(s.LightThemeHour, 7, 24); s.DarkThemeHour = Limit(s.DarkThemeHour, 20, 24);
        s.LightSources ??= new();
        s.LightSources = s.LightSources.Where(x => { try { if (x is null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 100) return false; LightFeed.UriFor(x.Url); x.PollMilliseconds = Math.Clamp(x.PollMilliseconds, 500, 60000); return true; } catch { return false; } }).DistinctBy(x => x.Id).Take(32).ToList();
        if (s.Source is not ("simulation" or "fc-usb")) s.Source = "simulation";
        s.Subscriptions ??= new();
        s.Subscriptions = s.Subscriptions.Where(x =>
        {
            try { if (x is null) return false; SubscriptionUri(x.Url); Validate(x.Preset); return true; } catch { return false; }
        }).DistinctBy(x => x.Url).Take(32).ToList();
    }
}
