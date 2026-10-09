using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentControl.Services;

public sealed record DiagnosticReply(uint Type, uint Current, uint Maximum, int? ErrorCode = null);
public sealed record DiagnosticProgress(int Completed, int Total, byte Code);
public sealed record DiagnosticCapabilities(string Text, string Source, int? ErrorCode = null);
public sealed class DiagnosticReading
{
    public byte Code { get; init; }
    public string HexCode => $"0x{Code:X2}";
    public string Key { get; init; } = "";
    public bool Advertised { get; init; }
    // byte[] is encoded as Base64 by System.Text.Json; the wire format needs numbers.
    public List<byte> AdvertisedOptions { get; init; } = new();
    public string Status { get; init; } = "not-probed";
    public uint? Type { get; init; }
    public uint? Current { get; init; }
    public uint? Maximum { get; init; }
    public int? ErrorCode { get; init; }
    public long ElapsedMilliseconds { get; init; }
}
public sealed class MonitorDiagnosticSnapshot
{
    // Connection identity protects comparisons, but never leaves this process.
    [JsonIgnore] public string DeviceId { get; init; } = "";
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Model { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string ManufacturerCode { get; init; } = "";
    public string Firmware { get; init; } = "";
    public string MccsVersion { get; init; } = "";
    // Only emitted by explicit opt-in: vendor capability strings may carry identifiers.
    public string? RawCapabilities { get; init; }
    public string CapabilitiesSource { get; init; } = "";
    public int? CapabilitiesErrorCode { get; init; }
    public bool CapabilitiesParsed { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public List<DiagnosticReading> Readings { get; init; } = new();

    public MonitorDiagnosticSnapshot ForExport(bool includeRaw) => new()
    {
        CapturedAt = CapturedAt, Model = Model, ModelId = ModelId, ManufacturerCode = ManufacturerCode,
        Firmware = Firmware, MccsVersion = MccsVersion, RawCapabilities = includeRaw ? RawCapabilities : null,
        CapabilitiesSource = CapabilitiesSource, CapabilitiesErrorCode = CapabilitiesErrorCode,
        CapabilitiesParsed = CapabilitiesParsed, ElapsedMilliseconds = ElapsedMilliseconds, Readings = Readings
    };
}
public sealed record DiagnosticDifference(byte Code, string Kind, DiagnosticReading? Before, DiagnosticReading? After);
public sealed class MonitorDiagnosticObservation
{
    public string Label { get; init; } = "";
    public MonitorDiagnosticSnapshot Snapshot { get; init; } = new();
    public List<DiagnosticDifference> Differences { get; init; } = new();
}
public sealed class MonitorDiagnosticReport
{
    public string Format { get; init; } = "fluentcontrol.monitor-diagnostics";
    public int Version { get; init; } = 1;
    public string AppVersion { get; init; } = typeof(MonitorDiagnosticReport).Assembly.GetName().Version?.ToString() ?? "";
    public bool Simulated { get; init; }
    public bool IncludesRawCapabilities { get; init; }
    public string ConnectionNotes { get; init; } = "";
    public string EnvironmentNotes { get; init; } = "";
    public MonitorDiagnosticSnapshot Baseline { get; init; } = new();
    public List<MonitorDiagnosticObservation> Observations { get; init; } = new();
}

public static class MonitorDiagnostics
{
    public const int MaxObservations = 24;
    // Known gettable standard controls only. Unknown/private and action features are
    // recorded as placeholders, never brute-force queried or granted write permission.
    private static readonly HashSet<byte> ReadableCodes = VcpCatalog.All.Where(d => d.Kind != VcpKind.Action)
        .Select(d => d.Code).Concat(new byte[] { 0x0B, 0x0C, 0xC8 }).ToHashSet();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Intentionally takes no writer. Run under the application's device lifecycle gate.
    public static MonitorDiagnosticSnapshot Capture(MonitorDevice device, DiagnosticCapabilities capabilities,
        Func<byte, DiagnosticReply> read, CancellationToken token = default, IProgress<DiagnosticProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        var text = capabilities.Text.Length <= 65536 ? capabilities.Text : "";
        var parsed = VcpCapabilities.Parse(text);
        var codes = ReadableCodes.Concat(parsed.Features.Keys).Distinct().Order().ToArray();
        var readings = new List<DiagnosticReading>();
        foreach (var code in codes)
        {
            token.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            DiagnosticReply? reply = null;
            var status = "not-probed";
            if (ReadableCodes.Contains(code))
            {
                try { reply = read(code); status = reply.ErrorCode.HasValue ? "failed" : "ok"; }
                catch (OperationCanceledException) { throw; }
                catch { status = "failed"; }
            }
            var ok = status == "ok";
            readings.Add(new()
            {
                Code = code, Key = VcpCatalog.All.FirstOrDefault(d => d.Code == code)?.Key ?? "vcp-" + code.ToString("X2"),
                Advertised = parsed.Features.TryGetValue(code, out var options), AdvertisedOptions = options?.ToList() ?? new(),
                Status = status, Type = ok ? reply!.Type : null, Current = ok ? reply!.Current : null,
                Maximum = ok ? reply!.Maximum : null, ErrorCode = reply?.ErrorCode, ElapsedMilliseconds = timer.ElapsedMilliseconds
            });
            progress?.Report(new(readings.Count, codes.Length, code));
        }
        token.ThrowIfCancellationRequested();
        string Version(byte code) => readings.FirstOrDefault(r => r.Code == code && r.Status == "ok")?.Current is uint v ? $"{v >> 8}.{v & 255}" : "";
        return new()
        {
            DeviceId = device.Id, Model = device.Model, ModelId = ModelIdentity.IsValid(device.ModelId) ? device.ModelId : "",
            ManufacturerCode = ModelIdentity.IsValid(device.ModelId) ? device.ModelId[..3] : "",
            Firmware = Version(0xC9), MccsVersion = Version(0xDF) is { Length: > 0 } version ? version : parsed.Version,
            RawCapabilities = text, CapabilitiesSource = capabilities.Source, CapabilitiesErrorCode = capabilities.ErrorCode,
            CapabilitiesParsed = parsed.HasVcpSection, Readings = readings, ElapsedMilliseconds = watch.ElapsedMilliseconds
        };
    }

    public static List<DiagnosticDifference> Compare(MonitorDiagnosticSnapshot baseline, MonitorDiagnosticSnapshot sample)
    {
        if (baseline.DeviceId.Length == 0 || baseline.DeviceId != sample.DeviceId || baseline.ModelId != sample.ModelId)
            throw new InvalidOperationException("Cannot compare different monitor connections.");
        var before = baseline.Readings.ToDictionary(r => r.Code);
        var after = sample.Readings.ToDictionary(r => r.Code);
        var result = new List<DiagnosticDifference>();
        foreach (var code in before.Keys.Union(after.Keys).Order())
        {
            before.TryGetValue(code, out var a); after.TryGetValue(code, out var b);
            var kind = a is null || b is null ? "availability" :
                a.Status != b.Status || a.ErrorCode != b.ErrorCode ? "read-status" :
                a.Status == "ok" && (a.Current != b.Current || a.Maximum != b.Maximum || a.Type != b.Type) ? "value" : "";
            if (kind.Length > 0) result.Add(new(code, kind, a, b));
        }
        return result;
    }

    public static string Serialize(MonitorDiagnosticReport report)
    {
        if (report.Observations.Count > MaxObservations || report.ConnectionNotes.Length > 500 || report.EnvironmentNotes.Length > 1000 ||
            report.Observations.Any(o => string.IsNullOrWhiteSpace(o.Label) || o.Label.Length > 160))
            throw new InvalidDataException("Invalid diagnostic report limits.");
        // Recompute differences while private identity is still available. Never serialize cached arbitrary objects.
        var copy = new MonitorDiagnosticReport
        {
            AppVersion = report.AppVersion, Simulated = report.Simulated, IncludesRawCapabilities = report.IncludesRawCapabilities,
            ConnectionNotes = report.ConnectionNotes, EnvironmentNotes = report.EnvironmentNotes,
            Baseline = report.Baseline.ForExport(report.IncludesRawCapabilities),
            Observations = report.Observations.Select(o => new MonitorDiagnosticObservation
            {
                Label = o.Label, Snapshot = o.Snapshot.ForExport(report.IncludesRawCapabilities), Differences = Compare(report.Baseline, o.Snapshot)
            }).ToList()
        };
        return JsonSerializer.Serialize(copy, JsonOptions);
    }
}
