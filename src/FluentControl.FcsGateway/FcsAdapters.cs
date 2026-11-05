using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Buffers.Binary;

// Explicit, local-only producer grants. Public Gateway readers cannot publish,
// add resources or grant device-management authority. Samples live only in RAM.
internal sealed class FcsAdapters
{
    internal sealed record Signal
    {
        public required string DeviceId { get; init; }
        public required string SensorId { get; init; }
        public required string SignalId { get; init; }
        public string ValueType { get; init; } = "number";
        public string Unit { get; init; } = "";
        public int StaleMilliseconds { get; init; } = 5000;
        [JsonIgnore] public string Key => DeviceId + "/" + SensorId + "/" + SignalId;
    }
    internal sealed record Grant
    {
        public required string Id { get; init; }
        public required string Kind { get; init; }
        public bool Enabled { get; init; }
        public Signal[] Signals { get; init; } = [];
    }
    internal sealed record Configuration
    {
        public int ListenPort { get; init; }
        public Grant[] Adapters { get; init; } = [];
    }
    private sealed record Sample(JsonElement Value, bool Valid, long Sequence, DateTimeOffset ReceivedAtUtc, long ReceivedTicks, double InitialAge, long Uptime);
    private sealed class Producer(Grant grant, string token)
    {
        public readonly Grant Grant = grant;
        public readonly HashSet<string> SignalKeys = grant.Signals.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        public readonly string Token = token;
        public string Boot = "";
        public long Sequence = -1, LastBatchTicks;
        public readonly HashSet<string> RetiredBoots = new(StringComparer.Ordinal);
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Producer> producers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Signal> signals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Sample> samples = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> owners = new(StringComparer.Ordinal);
    private readonly long started = Stopwatch.GetTimestamp();
    public int Port { get; }
    public static bool IsExternal(string id) => id.StartsWith("ext-", StringComparison.Ordinal);
    public static string WindowsDirectory => Environment.GetEnvironmentVariable("FCS_DATA") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "FcsGateway");
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public FcsAdapters(string directory)
    {
        var path = Path.Combine(directory, "adapters.json");
        if (!File.Exists(path)) return; // No listener, token or discovery by default.
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Adapter configuration exceeds 64 KiB.");
        var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Missing adapter configuration.");
        if (config.Adapters is null || config.Adapters.Length > 16 || config.ListenPort is < 0 or > 65535)
            throw new InvalidDataException("Invalid adapter limits.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in config.Adapters)
        {
            if (grant is null || !FcsNode.ValidId(grant.Id) || grant.Id.Length > 32 || !ids.Add(grant.Id) ||
                grant.Kind is not ("ros2" or "socketcan" or "opcua" or "custom") || grant.Signals is null || grant.Signals.Length is < 1 or > 64)
                throw new InvalidDataException("Invalid adapter grant.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var signal in grant.Signals)
            {
                if (signal is null || !FcsNode.ValidId(signal.DeviceId) || !signal.DeviceId.StartsWith("ext-" + grant.Id + "-", StringComparison.Ordinal) ||
                    !FcsNode.ValidId(signal.SensorId) || !FcsNode.ValidId(signal.SignalId) || signal.SignalId.Length > 64 ||
                    signal.ValueType is not ("number" or "integer" or "boolean" or "string") || signal.Unit is null || signal.Unit.Length > 64 ||
                    signal.StaleMilliseconds is < 500 or > 60000 || !keys.Add(signal.Key)) throw new InvalidDataException("Invalid adapter signal.");
                if (!grant.Enabled) continue;
                if (owners.TryGetValue(signal.DeviceId, out var owner) && owner != grant.Id)
                    throw new InvalidDataException("A virtual device must belong to exactly one enabled adapter.");
                signals.Add(signal.Key, signal); owners[signal.DeviceId] = grant.Id;
            }
            if (grant.Enabled) producers.Add(grant.Id, new Producer(grant, ""));
        }
        if (signals.Count > 256 || owners.Count > 32 || producers.Count > 0 && config.ListenPort == 0)
            throw new InvalidDataException("Adapter port required; at most 256 signals / 32 devices.");
        Port = producers.Count > 0 ? config.ListenPort : 0;
        if (Port == 0) return;
        var tokens = Path.Combine(directory, "adapter-tokens"); Directory.CreateDirectory(tokens);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tokens, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var id in producers.Keys.ToArray())
        {
            var tokenPath = Path.Combine(tokens, id + ".token");
            if (!File.Exists(tokenPath))
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using var file = new FileStream(tokenPath, options); file.Write(System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()));
            }
            if (new FileInfo(tokenPath).Length > 256) throw new InvalidDataException("Invalid adapter token.");
            var token = File.ReadAllText(tokenPath).Trim();
            if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid adapter token.");
            producers[id] = new Producer(producers[id].Grant, token);
        }
    }
    private static string Text(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing adapter field.");
    private static void Fields(JsonElement item, params string[] allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in item.EnumerateObject())
            if (!allowed.Contains(field.Name) || !seen.Add(field.Name)) throw new InvalidDataException("Unknown or repeated adapter field.");
        if (seen.Count != allowed.Length) throw new InvalidDataException("Missing adapter field.");
    }
    private static bool ValidValue(JsonElement value, string type) => type switch {
        "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && double.IsFinite(n),
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "string" => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 4096, _ => false };
    internal void Ingest(JsonElement frame)
    {
        lock (gate)
        {
            Fields(frame, "protocol", "adapterId", "token", "bootId", "sequence", "data");
            if (Text(frame, "protocol") != "fcs-adapter/1" || !producers.TryGetValue(Text(frame, "adapterId"), out var producer)) throw new InvalidDataException("Unauthorized adapter.");
            var token = Text(frame, "token");
            if (token.Length != 64 || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(token), System.Text.Encoding.ASCII.GetBytes(producer.Token)))
                throw new InvalidDataException("Unauthorized adapter.");
            var boot = Text(frame, "bootId"); var sequence = frame.GetProperty("sequence").GetInt64();
            if (!Guid.TryParseExact(boot, "N", out _) || sequence < 0 || producer.RetiredBoots.Contains(boot) ||
                boot == producer.Boot && sequence <= producer.Sequence || producer.RetiredBoots.Count >= 256 && boot != producer.Boot)
                throw new InvalidDataException("Replayed adapter batch or excessive restarts.");
            if (producer.LastBatchTicks != 0 && Stopwatch.GetElapsedTime(producer.LastBatchTicks).TotalMilliseconds < 90)
                throw new InvalidDataException("Adapter rate exceeds 10 batches/sec.");
            var data = frame.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() is < 1 or > 64) throw new InvalidDataException("Invalid adapter batch size.");
            var pending = new Dictionary<string, Sample>(StringComparer.Ordinal); var now = DateTimeOffset.UtcNow; var ticks = Stopwatch.GetTimestamp();
            foreach (var item in data.EnumerateArray())
            {
                Fields(item, "deviceId", "sensorId", "signalId", "sequence", "receivedAtUtc", "valid", "value");
                var key = Text(item, "deviceId") + "/" + Text(item, "sensorId") + "/" + Text(item, "signalId");
                if (!producer.SignalKeys.Contains(key) || !signals.TryGetValue(key, out var signal) ||
                    owners[signal.DeviceId] != producer.Grant.Id || pending.ContainsKey(key)) throw new InvalidDataException("Signal outside adapter grant.");
                var valid = item.GetProperty("valid").GetBoolean(); var value = item.GetProperty("value");
                if (valid ? !ValidValue(value, signal.ValueType) : value.ValueKind != JsonValueKind.Null) throw new InvalidDataException("Invalid adapter value.");
                var received = item.GetProperty("receivedAtUtc").GetDateTimeOffset();
                var age = (now - received).TotalMilliseconds; var sampleSequence = item.GetProperty("sequence").GetInt64();
                if (age < -1000 || age > signal.StaleMilliseconds || sampleSequence < 0) throw new InvalidDataException("Stale or invalid adapter sample.");
                if (boot == producer.Boot && samples.TryGetValue(key, out var old) && sampleSequence <= old.Sequence)
                {
                    if (sampleSequence < old.Sequence || received != old.ReceivedAtUtc || valid != old.Valid || value.GetRawText() != old.Value.GetRawText())
                        throw new InvalidDataException("Replayed or changed sample.");
                    pending.Add(key, old); // A heartbeat must not refresh the sample's age.
                }
                else pending.Add(key, new Sample(value.Clone(), valid, sampleSequence, received, ticks, Math.Max(0, age), (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
            }
            if (producer.Boot != boot)
            {
                if (producer.Boot.Length > 0) producer.RetiredBoots.Add(producer.Boot);
                foreach (var signal in producer.Grant.Signals) samples.Remove(signal.Key);
            }
            foreach (var pair in pending) samples[pair.Key] = pair.Value;
            producer.Boot = boot; producer.Sequence = sequence; producer.LastBatchTicks = ticks;
        }
    }
    private static double Age(Sample s) => s.InitialAge + Stopwatch.GetElapsedTime(s.ReceivedTicks).TotalMilliseconds;
    private bool Fresh(Signal signal) => samples.TryGetValue(signal.Key, out var s) && s.Valid && Age(s) <= signal.StaleMilliseconds;
    public object[] Snapshot()
    {
        lock (gate) return owners.Keys.Order().Select(id => (object)new { deviceId = id, ip = "", usbPort = (string?)null,
            status = signals.Values.Any(s => s.DeviceId == id && Fresh(s)) ? "online" : "offline", lux = (double?)null,
            lastSampleUtc = samples.Where(s => s.Key.StartsWith(id + "/", StringComparison.Ordinal)).Select(s => (DateTimeOffset?)s.Value.ReceivedAtUtc).Max(),
            transport = "adapter", adapterKind = producers[owners[id]].Grant.Kind }).ToArray();
    }
    public object Status()
    {
        lock (gate) return new { protocol = "fcs-gateway/1", adapters = producers.Values.Select(p => new {
            id = p.Grant.Id, kind = p.Grant.Kind, signals = p.Grant.Signals.Length,
            onlineSignals = p.Grant.Signals.Count(Fresh), readOnly = true }).ToArray() };
    }
    public object Node(string id, string operation, JsonElement arguments)
    {
        arguments = FcsNode.Arguments(operation, arguments);
        lock (gate)
        {
            if (!owners.TryGetValue(id, out var owner) || operation is not ("node.info" or "sensors.list" or "operations.list" or "data.read"))
                throw new InvalidDataException("Adapter resource or operation unavailable.");
            var selected = signals.Values.Where(s => s.DeviceId == id).ToArray();
            var kind = producers[owner].Grant.Kind;
            var sensors = selected.GroupBy(s => s.SensorId).Select((g, index) => new { id = g.Key, index, kind = "adapter", model = kind, physical = false,
                driver = new { status = "ready" }, signals = g.Select(s => new { id = s.SignalId, unit = s.Unit, valueType = s.ValueType, readable = true, minIntervalMs = 100 }).ToArray(),
                extensions = new Dictionary<string, object> { ["fcs.adapter"] = new { adapterId = owner, kind } } }).ToArray();
            var operations = new[] { "node.info", "sensors.list", "operations.list", "data.read" }.Select(op => new { id = op, state = "available", access = "read", scope = "node", transports = new[] { "gateway" }, parameters = Array.Empty<object>(), extensions = new { } }).ToArray();
            var frame = new Dictionary<string, object?> { ["protocol"] = "fcs-node/2", ["deviceId"] = id, ["ok"] = true };
            var age = 0d; var maximum = selected.Min(s => s.StaleMilliseconds); DateTimeOffset? received = null;
            switch (operation)
            {
                case "node.info":
                    frame["hardware"] = new { sensors, board = "gateway-adapter", physical = false };
                    frame["software"] = new { version = "fcs-adapter/1", operations };
                    frame["extensions"] = new Dictionary<string, object> { ["fcs.adapter"] = new { adapterId = owner, kind, virtualDevice = true } }; break;
                case "sensors.list": frame["sensors"] = sensors; break;
                case "operations.list": frame["operations"] = operations; break;
                case "data.read":
                    string? Filter(string name) => arguments.TryGetProperty(name, out var p) ? p.GetString() : null;
                    selected = selected.Where(s => (Filter("sensorId") is not { } sensor || sensor == s.SensorId) && (Filter("signalId") is not { } signal || signal == s.SignalId)).ToArray();
                    if (selected.Length == 0) throw new InvalidDataException("Unknown signal.");
                    maximum = selected.Min(s => s.StaleMilliseconds);
                    frame["data"] = selected.Select(s => {
                        samples.TryGetValue(s.Key, out var sample); var fresh = Fresh(s); var latest = Filter("mode") == "latest";
                        age = Math.Max(age, sample == null ? s.StaleMilliseconds + 1 : Age(sample));
                        if (sample != null && (received == null || sample.ReceivedAtUtc < received)) received = sample.ReceivedAtUtc;
                        return new { sensorId = s.SensorId, signalId = s.SignalId, valueType = s.ValueType, unit = s.Unit,
                            value = fresh ? (object?)sample!.Value : null, valid = fresh, fresh = fresh && !latest,
                            sequence = sample?.Sequence ?? 0, uptimeMs = sample?.Uptime ?? 0,
                            status = fresh ? (latest ? "cached" : "ready") : "adapter_unavailable",
                            extensions = new Dictionary<string, object> { ["fcs.adapter"] = new { adapterId = owner, kind, timestampBasis = "adapter-receipt",
                                receivedAtUtc = sample?.ReceivedAtUtc,
                                sampleAgeMilliseconds = (int)Math.Min(int.MaxValue, sample == null ? s.StaleMilliseconds + 1 : Age(sample)) } } };
                    }).ToArray(); break;
            }
            return new { protocol = "fcs-gateway/1", result = frame, transport = "adapter", receivedAtUtc = received,
                sampleAgeMilliseconds = (int)Math.Min(int.MaxValue, age), maxAgeMilliseconds = maximum };
        }
    }
    public Task RunAsync(CancellationToken cancel)
    {
        if (Port == 0) return Task.CompletedTask;
        var listener = new TcpListener(IPAddress.Loopback, Port); listener.Start(4);
        return AcceptAsync(listener, cancel);
    }
    private async Task AcceptAsync(TcpListener listener, CancellationToken cancel)
    {
        using var active = new SemaphoreSlim(4); var tasks = new List<Task>();
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancel);
                if (!active.Wait(0)) { client.Dispose(); continue; }
                tasks.RemoveAll(t => t.IsCompleted);
                tasks.Add(Serve(client, active, cancel));
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        finally { listener.Stop(); await Task.WhenAll(tasks); }
    }
    private async Task Serve(TcpClient client, SemaphoreSlim active, CancellationToken cancel)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
        {
            deadline.CancelAfter(3000);
            try
            {
                using var stream = client.GetStream(); var header = new byte[4]; await stream.ReadExactlyAsync(header, deadline.Token);
                int size = BinaryPrimitives.ReadInt32BigEndian(header);
                if (size is < 1 or > 32768) return;
                var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, deadline.Token);
                bool ok;
                try { using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 }); Ingest(doc.RootElement); ok = true; }
                catch (Exception e) when (e is InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException) { ok = false; }
                var response = JsonSerializer.SerializeToUtf8Bytes(new { ok }); BinaryPrimitives.WriteInt32BigEndian(header, response.Length);
                await stream.WriteAsync(header, deadline.Token); await stream.WriteAsync(response, deadline.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException) { }
            finally { active.Release(); }
        }
    }
}
