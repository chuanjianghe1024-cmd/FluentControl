using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentControl.FcsOpcUa;

internal sealed record Signal
{
    public required string DeviceId { get; init; }
    public required string SensorId { get; init; }
    public required string SignalId { get; init; }
    public string ValueType { get; init; } = "number";
    public string Unit { get; init; } = "";
    public string Key(string system, string gateway) => $"fcs/{system}/{gateway}/{DeviceId}/{SensorId}/{SignalId}";
    public void Validate()
    {
        Options.Id(DeviceId); Options.Id(SensorId); Options.Id(SignalId);
        if (ValueType is not ("number" or "integer" or "boolean" or "string") || Unit is null || Unit.Length > 64)
            throw new InvalidDataException("Unsupported signal type or unit.");
    }
}
internal sealed record Mount
{
    public required string SystemId { get; init; }
    public required string GatewayId { get; init; }
    public required string Endpoint { get; init; }
    public Signal[] Signals { get; init; } = [];
}
internal sealed record Options
{
    public required string SystemId { get; init; }
    public required string GatewayId { get; init; }
    public string Endpoint { get; init; } = "opc.tcp://localhost:4840/fcs";
    public string PkiDirectory { get; init; } = "opcua-pki";
    public string? GatewayUrl { get; init; }
    public string? GatewayTokenFile { get; init; }
    public bool WindowsPipe { get; init; }
    public int PollMilliseconds { get; init; } = 2000;
    public int StaleMilliseconds { get; init; } = 15000;
    public Signal[] Signals { get; init; } = [];
    public Mount[] Mounts { get; init; } = [];
    public static Options Load(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Configuration exceeds 64 KiB.");
        var result = JsonSerializer.Deserialize<Options>(File.ReadAllText(path), new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Empty configuration.");
        result.Validate(); return result;
    }
    public static void Id(string? value)
    {
        if (value is not { Length: > 0 and <= 100 } || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new InvalidDataException("IDs must contain 1–100 ASCII letters, digits, '-' or '_'.");
    }
    public static Uri UaUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "opc.tcp" ||
            uri.Port is < 1 or > 65535 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Invalid OPC UA endpoint.");
        return uri;
    }
    public void Validate()
    {
        Id(SystemId); Id(GatewayId); UaUri(Endpoint);
        if (string.IsNullOrWhiteSpace(PkiDirectory) || PollMilliseconds is < 500 or > 60000 ||
            StaleMilliseconds < PollMilliseconds * 2 || StaleMilliseconds > 300000 ||
            Signals is null || Mounts is null || Signals.Length > 128 || Mounts.Length > 8)
            throw new InvalidDataException("Invalid limits.");
        if (Signals.Length > 0 && (WindowsPipe == (GatewayUrl != null)))
            throw new InvalidDataException("Choose exactly one local Gateway transport.");
        if (WindowsPipe && !OperatingSystem.IsWindows())
            throw new InvalidDataException("Windows Gateway pipe transport requires Windows.");
        if (GatewayUrl != null)
        {
            var url = new Uri(GatewayUrl, UriKind.Absolute);
            // Bearer credentials never leave this machine in the preview adapter.
            if (url.Scheme != "http" || !IPAddress.TryParse(url.Host, out var ip) || !IPAddress.IsLoopback(ip) ||
                url.UserInfo.Length != 0 || url.AbsolutePath != "/" || url.Query.Length != 0 || url.Fragment.Length != 0 ||
                string.IsNullOrWhiteSpace(GatewayTokenFile))
                throw new InvalidDataException("Gateway HTTP must use a literal loopback address and token file.");
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signal in Signals)
        {
            signal.Validate(); if (!keys.Add(signal.Key(SystemId, GatewayId))) throw new InvalidDataException("Duplicate resource.");
        }
        var systems = new HashSet<string>(StringComparer.Ordinal) { SystemId };
        foreach (var mount in Mounts)
        {
            Id(mount.SystemId); Id(mount.GatewayId); UaUri(mount.Endpoint);
            if (!systems.Add(mount.SystemId) || mount.Signals is null || mount.Signals.Length is < 1 or > 128)
                throw new InvalidDataException("Duplicate/self mount or invalid signal count.");
            foreach (var signal in mount.Signals)
            {
                signal.Validate(); if (!keys.Add(signal.Key(mount.SystemId, mount.GatewayId))) throw new InvalidDataException("Duplicate resource.");
            }
        }
        if (keys.Count > 256) throw new InvalidDataException("At most 256 exported signals.");
    }
}
