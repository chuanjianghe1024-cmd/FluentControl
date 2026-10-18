using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace FluentControl.Services;

public sealed class LightSourceSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public int PollMilliseconds { get; set; } = 1000;
    public override string ToString() => Name;
}
public sealed class LightSample
{
    public string Format { get; set; } = "";
    public int Version { get; set; }
    public string SourceId { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public double? Lux { get; set; }
    public bool Valid { get; set; }
    public bool Simulated { get; set; }
}
public static class LightFeed
{
    public const int MinimumPollMilliseconds = 200;
    public const int MaximumPollMilliseconds = 86400000;
    public static void ValidateSource(LightSourceSubscription source)
    {
        if (LocalLightProtocol.IsLocal(source.Url))
        {
            LocalLightProtocol.PortFor(source.Url);
            if (string.IsNullOrWhiteSpace(source.DeviceId) || source.DeviceId.Length > 100) throw new InvalidDataException("Invalid device identity.");
        }
        else UriFor(source.Url);
    }
    public const int MaximumBytes = 16 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    public static Uri UriFor(string url)
    {
        if (url is null || url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Invalid light source URL.");
        return uri;
    }
    public static LightSample Parse(string json, DateTimeOffset now, int pollMilliseconds)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Light sample too large.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        if (!document.RootElement.TryGetProperty("timestamp", out var time) || time.ValueKind != JsonValueKind.String ||
            !System.Text.RegularExpressions.Regex.IsMatch(time.GetString() ?? "", @"T.*(?:Z|[+-]\d{2}:\d{2})$")) throw new InvalidDataException("Timestamp must include a time zone.");
        var s = JsonSerializer.Deserialize<LightSample>(json, Json) ?? throw new InvalidDataException("Empty light sample.");
        if (s.Format != "fluentcontrol.light-sample" || s.Version != 1 || string.IsNullOrWhiteSpace(s.SourceId) || s.SourceId.Length > 100 || !s.Valid ||
            s.Lux is not double lux || !double.IsFinite(lux) || lux < 0 || lux > 200000 || s.Timestamp == default ||
            now - s.Timestamp > TimeSpan.FromMilliseconds(Math.Max(5000, 3 * Math.Clamp(pollMilliseconds, MinimumPollMilliseconds, MaximumPollMilliseconds))) || s.Timestamp - now > TimeSpan.FromSeconds(5))
            throw new InvalidDataException("Invalid or stale light sample.");
        return s;
    }
    public static string Serialize(LightSample sample) => JsonSerializer.Serialize(sample, Json);
    public static async Task<LightSample> ReadAsync(HttpClient client, LightSourceSubscription source, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UriFor(source.Url));
        request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumBytes) throw new InvalidDataException("Light sample too large.");
        using var stream = await response.Content.ReadAsStreamAsync(cancellation); using var output = new MemoryStream();
        byte[] buffer = new byte[4096]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) != 0)
        {
            if (output.Length + count > MaximumBytes) throw new InvalidDataException("Light sample too large.");
            output.Write(buffer, 0, count);
        }
        return Parse(new System.Text.UTF8Encoding(false, true).GetString(output.ToArray()), DateTimeOffset.UtcNow, source.PollMilliseconds);
    }
}
public sealed class LightSmoothing
{
    private readonly Queue<double> samples = new();
    private DateTimeOffset last;
    private string identity = "";
    private double? filtered;
    public void Reset() { samples.Clear(); last = default; identity = ""; filtered = null; }
    public double? Add(LightSample sample)
    {
        if (identity.Length > 0 && identity != sample.SourceId) throw new InvalidDataException("Light source identity changed.");
        identity = sample.SourceId;
        if (sample.Timestamp < last) throw new InvalidDataException("Out-of-order light sample.");
        if (sample.Timestamp == last) return filtered;
        last = sample.Timestamp;
        samples.Enqueue(sample.Lux!.Value); while (samples.Count > 3) samples.Dequeue();
        if (samples.Count < 3) return null;
        var median = samples.Order().ElementAt(1);
        filtered = filtered is double previous ? previous + .25 * (median - previous) : median;
        return filtered;
    }
}
public sealed record SolarTimes(DateTimeOffset? Sunrise, DateTimeOffset? Sunset, bool PolarDay)
{
    public bool IsDay(DateTimeOffset now) => Sunrise is { } rise && Sunset is { } set ? now >= rise && now < set : PolarDay;
}
public static class SolarSchedule
{
    // Independent implementation of NOAA's published general solar equations.
    // Approximate apparent sunrise/sunset, no geolocation requests.
    public static SolarTimes Calculate(DateTime localDate, double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180) throw new ArgumentOutOfRangeException();
        double gamma = 2 * Math.PI / (DateTime.IsLeapYear(localDate.Year) ? 366 : 365) * (localDate.DayOfYear - 1);
        double eq = 229.18 * (.000075 + .001868 * Math.Cos(gamma) - .032077 * Math.Sin(gamma) - .014615 * Math.Cos(2 * gamma) - .040849 * Math.Sin(2 * gamma));
        double decl = .006918 - .399912 * Math.Cos(gamma) + .070257 * Math.Sin(gamma) - .006758 * Math.Cos(2 * gamma) + .000907 * Math.Sin(2 * gamma) - .002697 * Math.Cos(3 * gamma) + .00148 * Math.Sin(3 * gamma);
        double lat = Math.Clamp(latitude, -89.9999, 89.9999) * Math.PI / 180;
        double cosHour = (Math.Cos(90.833 * Math.PI / 180) - Math.Sin(lat) * Math.Sin(decl)) / (Math.Cos(lat) * Math.Cos(decl));
        if (cosHour > 1) return new(null, null, false);
        if (cosHour < -1) return new(null, null, true);
        double ha = Math.Acos(cosHour) * 180 / Math.PI;
        var utcDate = new DateTimeOffset(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, TimeSpan.Zero);
        return new(utcDate.AddMinutes(720 - 4 * (longitude + ha) - eq), utcDate.AddMinutes(720 - 4 * (longitude - ha) - eq), false);
    }
    public static bool InDayRange(double hour, double start, double end) => start <= end ? hour >= start && hour < end : hour >= start || hour < end;
    public static double? NextBrightness(double current, double target) => Math.Abs(current - target) < 2 ? null : current + Math.Clamp(target - current, -5, 5);
}
