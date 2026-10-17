using System.Text.Json;
namespace FluentControl.Services;

// The wire protocol is shared with tools/light-source/rp2040/main.py.
public static class LocalLightProtocol
{
    public const string Scheme = "fc-usb";
    public static bool IsLocal(string? url) => url?.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase) == true;
    public static string PortFor(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Scheme || uri.UserInfo.Length != 0 ||
            !uri.IsDefaultPort || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath is not ("" or "/") ||
            !System.Text.RegularExpressions.Regex.IsMatch(uri.Host, @"^(auto|com[1-9][0-9]{0,4})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new InvalidDataException("Invalid local sensor address.");
        return uri.Host.ToUpperInvariant();
    }
    public static string Identity(JsonElement frame)
    {
        var id = frame.GetProperty("sourceId").GetString();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) throw new InvalidDataException("Invalid sensor identity.");
        return id;
    }
    public static string Handshake(JsonElement frame)
    {
        if (frame.GetProperty("protocol").GetString() != "fc-light-usb/1") throw new InvalidDataException("Unsupported sensor firmware.");
        return Identity(frame);
    }
    public static LightSample Sample(JsonElement frame, string identity, DateTimeOffset now)
    {
        if (Identity(frame) != identity) throw new InvalidDataException("Sensor identity changed.");
        if (!frame.GetProperty("valid").GetBoolean() || !frame.GetProperty("lux").TryGetDouble(out double lux) ||
            !double.IsFinite(lux) || lux is < 0 or > 200000) throw new InvalidDataException("Invalid sensor measurement.");
        return new() { Format = "fluentcontrol.light-sample", Version = 1, SourceId = identity,
            Timestamp = now, Lux = lux, Valid = true, Simulated = false };
    }
}

