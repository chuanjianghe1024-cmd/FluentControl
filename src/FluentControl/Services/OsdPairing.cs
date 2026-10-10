using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentControl.Services;

// Local, user-observed pairing evidence. A successful transport response is never a binding.
public sealed class OsdIdentity
{
    public string ModelId { get; set; } = "";
    public string Model { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Firmware { get; set; } = "";
    public string MccsVersion { get; set; } = "";
    public string CapabilitiesFingerprint { get; set; } = "";
    public static OsdIdentity Capture(MonitorDevice device)
    {
        var info = MonitorHardwareInfo.Capture(device);
        var caps = VcpCapabilities.Parse(device.CapabilitiesText);
        var canonical = string.Join(";", caps.Features.OrderBy(x => x.Key).Select(x => $"{x.Key:X2}:" + string.Join(",", x.Value.Order()))) + ";" + caps.Version;
        return new() { ModelId = device.ModelId, Model = device.Model, Manufacturer = info?.ManufacturerCode ?? "",
            Firmware = info?.Firmware ?? "", MccsVersion = info?.MccsVersion ?? caps.Version,
            CapabilitiesFingerprint = caps.HasVcpSection ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))) : "" };
    }
    public bool Matches(OsdIdentity other) => ModelIdentity.IsValid(ModelId) && Firmware.Length > 0 && CapabilitiesFingerprint.Length > 0 &&
        ModelId == other.ModelId && Firmware == other.Firmware && CapabilitiesFingerprint == other.CapabilitiesFingerprint && MccsVersion == other.MccsVersion;
    [JsonIgnore] public string StorageKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ModelId + "|" + Firmware + "|" + MccsVersion + "|" + CapabilitiesFingerprint)));
}

public sealed class OsdCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public byte Code { get; set; }
    public uint Value { get; set; }
    public string Source { get; set; } = "";
    public string SourceModel { get; set; } = "";
    public string SourceProfileId { get; set; } = "";
    [JsonIgnore] public bool DisablesEvents => Code == 0xCA && Value == 3;
    [JsonIgnore] public string Display => Name + $" · VCP 0x{Code:X2} = 0x{Value:X4}";
    public void Validate()
    {
        if (Id.Length is < 1 or > 80 || Name.Length is < 1 or > 120 || Source.Length is < 1 or > 1000 || SourceModel.Length > 120 || SourceProfileId.Length > 80 ||
            Value > 65535 || !(Code == 0xCA && Value is >= 1 and <= 3 || Code == 0x03 || Code >= 0xE0))
            throw new InvalidDataException("Invalid OSD command.");
    }
    public OsdCommand Copy() => new() { Id = Id, Name = Name, Code = Code, Value = Value, Source = Source, SourceModel = SourceModel, SourceProfileId = SourceProfileId };
}

public sealed class OsdSample
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public byte Code { get; set; }
    public uint? Type { get; set; }
    public uint? Current { get; set; }
    public uint? Maximum { get; set; }
    public int? ErrorCode { get; set; }
    public string Error { get; set; } = "";
    public long Milliseconds { get; set; }
    [JsonIgnore] public bool Succeeded => Current.HasValue && ErrorCode is null && Error.Length == 0;
    [JsonIgnore] public string Display => $"0x{Code:X2}: " + (Succeeded ? $"0x{Current:X4} / 0x{Maximum:X4}" :
        Strings.T("读取失败", "Read failed") + (ErrorCode is int e ? $" · 0x{unchecked((uint)e):X8}" : ""));
}

public sealed class OsdTrial
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public string SourceProfileId { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public OsdCommand Command { get; set; } = new();
    public string Context { get; set; } = "unknown";
    public uint? SentValue { get; set; }
    public bool WriteSucceeded { get; set; }
    public int? WriteErrorCode { get; set; }
    public string Error { get; set; } = "";
    public long Milliseconds { get; set; }
    public bool UsedSessionButtonFields { get; set; }
    public OsdSample? Before { get; set; }
    public OsdSample? After { get; set; }
    public string Outcome { get; set; } = "unconfirmed";
    public List<string> Effects { get; set; } = new();
    public string Notes { get; set; } = "";
    public DateTimeOffset? ConfirmedAt { get; set; }
}

public sealed class OsdBinding
{
    public string Action { get; set; } = "";
    public string CommandId { get; set; } = "";
    public string TrialId { get; set; } = "";
    public string Context { get; set; } = "unknown";
}

public sealed class OsdSessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public string AppVersion { get; set; } = typeof(OsdSessionRecord).Assembly.GetName().Version?.ToString() ?? "";
    public string OperatingSystem { get; set; } = Environment.OSVersion.VersionString;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Connection { get; set; } = "";
    public string Hdr { get; set; } = "unknown";
    public string Notes { get; set; } = "";
    public MonitorDiagnosticSnapshot? Baseline { get; set; }
}

public sealed class OsdEventCapture
{
    public string SessionId { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Label { get; set; } = "";
    public bool Cancelled { get; set; }
    public List<OsdSample> Samples { get; set; } = new();
}

public sealed class OsdPairingProfile
{
    [JsonRequired] public string Format { get; set; } = "fluentcontrol.osd-pairing";
    [JsonRequired] public int Version { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Simulated { get; set; }
    [JsonRequired] public OsdIdentity Identity { get; set; } = new();
    public List<OsdSessionRecord> Sessions { get; set; } = new();
    public List<OsdCommand> Commands { get; set; } = new();
    public List<OsdTrial> Trials { get; set; } = new();
    public List<OsdBinding> Bindings { get; set; } = new();
    public List<OsdEventCapture> EventCaptures { get; set; } = new();
    public static readonly string[] Actions = { "open", "close", "toggle", "up", "down", "back", "enter", "left", "right", "increase", "decrease", "enable", "disable", "disable-events" };
    public static readonly string[] Contexts = { "unknown", "closed", "main-menu", "submenu", "adjustment" };
    public static OsdPairingProfile Create(MonitorDevice device, bool simulated) => new()
    {
        Identity = OsdIdentity.Capture(device), Simulated = simulated,
        Commands = new()
        {
            new() { Id = "osd-enable", Name = Strings.T("启用 OSD", "Enable OSD"), Code = 0xCA, Value = 2, Source = "MCCS OSD/Button Control" },
            new() { Id = "osd-disable", Name = Strings.T("禁用 OSD，保留按键事件", "Disable OSD, keep button events"), Code = 0xCA, Value = 1, Source = "MCCS OSD/Button Control" },
            new() { Id = "osd-disable-events", Name = Strings.T("禁用 OSD 与按键事件", "Disable OSD and button events"), Code = 0xCA, Value = 3, Source = "MCCS 2.2 OSD/Button Control; not a generic MCCS 2.1 operation" }
        }
    };
    public OsdCommand? BoundCommand(string action)
    {
        var binding = Bindings.FirstOrDefault(b => b.Action == action);
        return binding is null ? null : Commands.FirstOrDefault(c => c.Id == binding.CommandId);
    }
    public void Confirm(OsdTrial trial, string outcome, IEnumerable<string> effects, string notes)
    {
        if (!Trials.Contains(trial) || trial.SourceProfileId.Length > 0 || outcome is not ("success" or "failure" or "other" or "uncertain") || notes.Length > 1000)
            throw new InvalidDataException("Invalid pairing observation.");
        var selected = effects.Distinct().ToList();
        if (selected.Any(a => !Actions.Contains(a)) || selected.Count > 0 && (!trial.WriteSucceeded || outcome is not ("success" or "other")))
            throw new InvalidDataException("Only an observed successful write can be bound.");
        trial.Outcome = outcome; trial.Effects = selected; trial.Notes = notes; trial.ConfirmedAt = DateTimeOffset.UtcNow;
        Bindings.RemoveAll(b => b.TrialId == trial.Id || selected.Contains(b.Action) || outcome == "failure" && b.CommandId == trial.Command.Id);
        Bindings.AddRange(selected.Select(a => new OsdBinding { Action = a, CommandId = trial.Command.Id, TrialId = trial.Id, Context = trial.Context }));
    }
    // Imported observations remain evidence; they cannot authorize local remote buttons.
    public void ImportCandidates(OsdPairingProfile source)
    {
        source.Validate();
        if (source.Id == Id || source.Simulated != Simulated || !Identity.Matches(source.Identity))
            throw new InvalidDataException(Strings.T("型号、固件或能力信息不匹配。", "Model, firmware or capabilities do not match."));
        if (Commands.Count + source.Commands.Count > 64 || Trials.Count + source.Trials.Count > 512 || Sessions.Count + source.Sessions.Count > 64 || EventCaptures.Count + source.EventCaptures.Count > 32)
            throw new InvalidDataException("Pairing history is full.");
        var commandIds = new Dictionary<string, string>();
        foreach (var command in source.Commands)
        {
            var copy = command.Copy(); copy.Id = Guid.NewGuid().ToString("N"); copy.SourceProfileId = source.Id;
            commandIds[command.Id] = copy.Id; Commands.Add(copy);
        }
        var sessionIds = new Dictionary<string, string>();
        foreach (var session in source.Sessions)
        {
            var copy = JsonSerializer.Deserialize<OsdSessionRecord>(JsonSerializer.Serialize(session, OsdPairingStore.Json), OsdPairingStore.Json)!;
            sessionIds[session.Id] = copy.Id = Guid.NewGuid().ToString("N"); Sessions.Add(copy);
        }
        foreach (var trial in source.Trials)
        {
            var copy = JsonSerializer.Deserialize<OsdTrial>(JsonSerializer.Serialize(trial, OsdPairingStore.Json), OsdPairingStore.Json)!;
            copy.Id = Guid.NewGuid().ToString("N"); copy.SourceProfileId = source.Id;
            copy.Command.Id = commandIds[trial.Command.Id]; copy.SessionId = sessionIds[trial.SessionId]; Trials.Add(copy);
        }
        foreach (var capture in source.EventCaptures)
        {
            var copy = JsonSerializer.Deserialize<OsdEventCapture>(JsonSerializer.Serialize(capture, OsdPairingStore.Json), OsdPairingStore.Json)!;
            copy.SessionId = sessionIds[capture.SessionId]; EventCaptures.Add(copy);
        }
    }
    public void Validate()
    {
        if (Format != "fluentcontrol.osd-pairing" || Version != 1 || Id.Length is < 1 or > 80 || Revision < 0 ||
            Identity is null || Identity.ModelId.Length > 80 || Identity.Model.Length > 200 || Identity.Manufacturer.Length > 80 ||
            Identity.Firmware.Length > 80 || Identity.MccsVersion.Length > 80 || Identity.CapabilitiesFingerprint.Length > 64 ||
            Commands is null || Commands.Count > 64 || Trials is null || Trials.Count > 512 || Bindings is null || Bindings.Count > Actions.Length ||
            Sessions is null || Sessions.Count > 64 || EventCaptures is null || EventCaptures.Count > 32)
            throw new InvalidDataException("Invalid OSD pairing profile.");
        if (Commands.Any(c => c is null) || Commands.Select(c => c.Id).Distinct().Count() != Commands.Count ||
            Sessions.Any(s => s is null || s.Id.Length is < 1 or > 80 || s.Connection.Length > 500 || s.Notes.Length > 1000 || s.Hdr is not ("unknown" or "on" or "off")) ||
            Sessions.Select(s => s.Id).Distinct().Count() != Sessions.Count || Trials.Select(t => t?.Id).Distinct().Count() != Trials.Count)
            throw new InvalidDataException("Invalid pairing identity or history.");
        foreach (var c in Commands) c.Validate();
        foreach (var t in Trials)
        {
            if (t is null || t.Id.Length is < 1 or > 80 || t.Command is null || !Contexts.Contains(t.Context) || t.Notes.Length > 1000 || t.Error.Length > 2000 ||
                t.SentValue > 65535 || t.SourceProfileId.Length > 80 || t.Outcome is not ("unconfirmed" or "success" or "failure" or "other" or "uncertain") ||
                t.Effects is null || t.Effects.Any(a => !Actions.Contains(a)) || !Sessions.Any(s => s.Id == t.SessionId) ||
                !Commands.Any(c => c.Id == t.Command.Id && c.Code == t.Command.Code && c.Value == t.Command.Value))
                throw new InvalidDataException("Invalid pairing trial.");
            t.Command.Validate();
        }
        if (Bindings.Select(b => b?.Action).Distinct().Count() != Bindings.Count) throw new InvalidDataException("Duplicate OSD bindings.");
        foreach (var b in Bindings)
            if (b is null || !Actions.Contains(b.Action) || !Trials.Any(t => t.Id == b.TrialId && t.Command.Id == b.CommandId && t.WriteSucceeded &&
                t.SourceProfileId.Length == 0 && (t.Outcome is "success" or "other") && t.Effects.Contains(b.Action) && t.Context == b.Context))
                throw new InvalidDataException("OSD binding has no local observation.");
        foreach (var capture in EventCaptures)
            if (capture is null || capture.Label.Length > 160 || capture.Samples is null || capture.Samples.Count > 96 ||
                !Sessions.Any(s => s.Id == capture.SessionId) || capture.Samples.Any(s => s is null || s.Code is not (0x02 or 0x03 or 0x52) || s.Error.Length > 2000))
                throw new InvalidDataException("Invalid button capture.");
    }
}

public sealed class OsdPairingSession
{
    private readonly Func<byte, DiagnosticReply> read;
    private readonly Action<byte, uint> write;
    private uint? lastButtonFields;
    public OsdPairingSession(Func<byte, DiagnosticReply> read, Action<byte, uint> write) { this.read = read; this.write = write; }
    public OsdSample Read(byte code)
    {
        var sample = new OsdSample { Code = code }; var timer = Stopwatch.StartNew();
        try
        {
            var reply = read(code); sample.ErrorCode = reply.ErrorCode;
            if (reply.ErrorCode is null) { sample.Type = reply.Type; sample.Current = reply.Current; sample.Maximum = reply.Maximum; }
            if (code == 0xCA && sample.Succeeded && sample.Current <= 65535) lastButtonFields = sample.Current;
        }
        catch (Exception ex) { sample.Error = ex.Message; if (ex is Win32Exception native) sample.ErrorCode = native.NativeErrorCode; }
        sample.Milliseconds = timer.ElapsedMilliseconds; return sample;
    }
    public OsdTrial Send(OsdCommand command, string sessionId, string context, bool allowSessionFields, CancellationToken token)
    {
        command.Validate(); token.ThrowIfCancellationRequested();
        if (!OsdPairingProfile.Contexts.Contains(context)) throw new InvalidDataException("Invalid menu context.");
        var trial = new OsdTrial { Command = command.Copy(), SessionId = sessionId, Context = context }; var timer = Stopwatch.StartNew();
        try
        {
            uint value = command.Value;
            if (command.Code == 0xCA)
            {
                trial.Before = Read(0xCA);
                var raw = trial.Before.Succeeded ? trial.Before.Current : allowSessionFields ? lastButtonFields : null;
                if (raw is null || raw > 65535) throw new IOException(Strings.T("无法读取按键字段；可明确选择沿用本次会话的已知字段后重试。", "Cannot read button fields. You may explicitly reuse known fields from this session and retry."));
                trial.UsedSessionButtonFields = !trial.Before.Succeeded;
                value = (raw.Value & 0xFF00) | command.Value;
            }
            token.ThrowIfCancellationRequested(); trial.SentValue = value;
            write(command.Code, value); trial.WriteSucceeded = true;
            if (command.Code == 0xCA) lastButtonFields = value;
            // Preserve evidence of an already completed write even if the dialog closes.
            if (!token.IsCancellationRequested) trial.After = Read(command.Code);
        }
        catch (OperationCanceledException) when (!trial.SentValue.HasValue) { throw; }
        catch (Exception ex) { trial.Error = ex.Message; if (ex is Win32Exception native) trial.WriteErrorCode = native.NativeErrorCode; }
        trial.Milliseconds = timer.ElapsedMilliseconds; return trial;
    }
    public OsdEventCapture Observe(string sessionId, string label, CancellationToken token)
    {
        var result = new OsdEventCapture { SessionId = sessionId, Label = label };
        var failures = new Dictionary<byte, int>(); var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 5000 && result.Samples.Count < 96 && !token.IsCancellationRequested)
        {
            foreach (var code in new byte[] { 0x02, 0x52, 0x03 })
            {
                if (token.IsCancellationRequested) break;
                if (failures.GetValueOrDefault(code) >= 2) continue;
                var sample = Read(code); result.Samples.Add(sample);
                if (!sample.Succeeded) failures[code] = failures.GetValueOrDefault(code) + 1;
            }
            if (failures.Count == 3 && failures.Values.All(v => v >= 2)) break;
            if (token.WaitHandle.WaitOne(120)) break;
        }
        result.Cancelled = token.IsCancellationRequested; return result;
    }
}

public sealed class OsdPairingStore
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, MaxDepth = 32 };
    private readonly string directory;
    private readonly string instanceKey;
    public OsdPairingStore(string directory, string deviceId = "")
    {
        this.directory = directory;
        instanceKey = deviceId.Length == 0 ? "" : "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId)));
    }
    public static OsdPairingProfile Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("OSD pairing file is too large.");
        try
        {
            var result = JsonSerializer.Deserialize<OsdPairingProfile>(json, Json) ?? throw new InvalidDataException("Invalid OSD pairing file.");
            result.Validate(); return result;
        }
        catch (Exception ex) when (ex is JsonException or NullReferenceException or ArgumentException) { throw new InvalidDataException("Invalid OSD pairing file.", ex); }
    }
    public static string Serialize(OsdPairingProfile profile, bool includeRawCapabilities = false)
    {
        profile.Validate();
        var copy = JsonSerializer.Deserialize<OsdPairingProfile>(JsonSerializer.Serialize(profile, Json), Json)!;
        foreach (var session in copy.Sessions) if (session.Baseline is not null) session.Baseline = session.Baseline.ForExport(includeRawCapabilities);
        var result = JsonSerializer.Serialize(copy, Json);
        if (Encoding.UTF8.GetByteCount(result) > MaxBytes) throw new InvalidDataException("OSD pairing file is too large.");
        return result;
    }
    public OsdPairingProfile? Load(OsdIdentity identity, bool simulated)
    {
        var path = Path.Combine(directory, identity.StorageKey + instanceKey + ".json");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("OSD pairing file is too large.");
        var result = Parse(File.ReadAllText(path));
        return result.Identity.Matches(identity) && result.Simulated == simulated ? result : null;
    }
    public void Save(OsdPairingProfile profile)
    {
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        var json = Serialize(profile); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, profile.Identity.StorageKey + instanceKey + ".json");
        var temporary = path + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static bool TryHex(string text, uint maximum, out uint value) => uint.TryParse((text.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text.Trim()[2..] : text.Trim()), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value) && value <= maximum;
}
