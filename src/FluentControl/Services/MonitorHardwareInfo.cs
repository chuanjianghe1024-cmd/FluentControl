using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace FluentControl.Services;

// Portable observations only: never export an EDID blob, device path or serial number.
public sealed class MonitorHardwareInfo
{
    public int Version { get; set; } = 1;
    public string ManufacturerCode { get; set; } = "";
    public string Firmware { get; set; } = "";
    public string MccsVersion { get; set; } = "";
    public List<MonitorControlInfo> Controls { get; set; } = new();
    public MonitorHardwareInfo Copy() => new() { Version = Version, ManufacturerCode = ManufacturerCode, Firmware = Firmware, MccsVersion = MccsVersion, Controls = Controls.Select(c => c.Copy()).ToList() };

    public static MonitorHardwareInfo? Capture(MonitorDevice device)
    {
        var caps = VcpCapabilities.Parse(device.CapabilitiesText);
        var result = new MonitorHardwareInfo
        {
            ManufacturerCode = ModelIdentity.IsValid(device.ModelId) ? device.ModelId[..3] : "",
            Firmware = VersionText(device.Features.FirstOrDefault(f => f.Definition.Key == "firmware")?.Information),
            MccsVersion = VersionText(device.Features.FirstOrDefault(f => f.Definition.Key == "mccs")?.Information) is { Length: > 0 } version ? version : VersionText(caps.Version)
        };
        foreach (var feature in device.Features)
        {
            var channel = feature.Channel;
            var advertised = caps.Features.ContainsKey(feature.Definition.Code);
            var readable = feature.Information.Length > 0 || channel is { IsAction: false };
            if (!advertised && !readable && channel is null) continue;
            result.Controls.Add(Describe(feature.Definition.Key, feature.Definition.Code, Kind(feature.Definition.Kind), channel, advertised, readable));
        }
        foreach (var channel in device.Channels.Where(c => !c.CompatibilityOnly && !result.Controls.Any(f => f.Key == c.PropertyKey)))
            result.Controls.Add(Describe(channel.PropertyKey, channel.VcpCode, channel.Options is null ? "continuous" : "choice", channel, false, !channel.IsAction));
        return result.Controls.Count == 0 && result.Firmware.Length == 0 && result.MccsVersion.Length == 0 ? null : result;
    }
    private static string VersionText(string? text) => text is not null && Regex.IsMatch(text, @"\A[0-9]{1,5}\.[0-9]{1,5}\z") ? text : "";
    private static string Kind(VcpKind kind) => kind switch { VcpKind.Continuous => "continuous", VcpKind.Choice => "choice", VcpKind.Gamma => "gamma", VcpKind.Action => "action", _ => "read-only" };
    private static MonitorControlInfo Describe(string key, byte? code, string kind, ControlChannel? channel, bool advertised, bool readable) => new()
    {
        Key = key.ToLowerInvariant(), VcpCode = code, Kind = kind, Advertised = advertised, Readable = readable, Writable = channel is not null,
        Minimum = channel?.Minimum, Maximum = channel?.Maximum, Options = channel?.Options?.Select(o => o.Value).Distinct().Order().ToList() ?? new()
    };
    public void Validate()
    {
        if (Version != 1 || ManufacturerCode is null || !Regex.IsMatch(ManufacturerCode, @"\A(?:[A-Z0-9]{3})?\z") || Firmware is null || MccsVersion is null ||
            (Firmware.Length > 0 && VersionText(Firmware).Length == 0) || (MccsVersion.Length > 0 && VersionText(MccsVersion).Length == 0) || Controls is null || Controls.Count > 256)
            throw new InvalidDataException("Invalid monitor hardware information.");
        var keys = new HashSet<string>();
        foreach (var control in Controls)
        {
            if (control is null || control.Key is null || !Regex.IsMatch(control.Key, @"\A[a-z][a-z0-9-]{0,39}\z") || !keys.Add(control.Key) ||
                control.Kind is not ("continuous" or "choice" or "gamma" or "read-only" or "action") || control.Options is null || control.Options.Count > 256 ||
                control.Options.Any(x => !double.IsFinite(x) || x < 0 || x > 65535 || x != Math.Truncate(x)) || control.Options.Distinct().Count() != control.Options.Count ||
                control.Minimum.HasValue != control.Maximum.HasValue || control.Minimum is double min && (!double.IsFinite(min) || min < 0) ||
                control.Maximum is double max && (!double.IsFinite(max) || max > 65535 || max < control.Minimum) ||
                control.Kind == "read-only" && control.Writable)
                throw new InvalidDataException("Invalid monitor control information.");
        }
    }
}
public sealed class MonitorControlInfo
{
    public string Key { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public byte? VcpCode { get; set; }
    public string Kind { get; set; } = "continuous";
    public bool Advertised { get; set; }
    public bool Readable { get; set; }
    public bool Writable { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Minimum { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Maximum { get; set; }
    public List<double> Options { get; set; } = new();
    public MonitorControlInfo Copy() => new() { Key = Key, VcpCode = VcpCode, Kind = Kind, Advertised = Advertised, Readable = Readable, Writable = Writable, Minimum = Minimum, Maximum = Maximum, Options = Options.ToList() };
}
