using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal sealed record HostNode(string DeviceId, string Ip);
internal sealed record HostConfig
{
    public HostNode[] Nodes { get; init; } = [];
    public int PollMilliseconds { get; init; } = 2000;
    public bool Discover { get; init; }
    public void Validate()
    {
        if (PollMilliseconds is < 1000 or > 60000 || Nodes.Length > 32 ||
            Nodes.Select(n => n.DeviceId).Distinct(StringComparer.Ordinal).Count() != Nodes.Length ||
            Nodes.Any(n => !FcsNode.ValidId(n.DeviceId) || !LocalIp(n.Ip)))
            throw new InvalidDataException("Invalid nodes.json configuration.");
    }
    public static bool LocalIp(string value)
    {
        if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168;
    }
}

// Bounded persistent history, independent of live state. Historical records are
// never restored into the live cache after a restart. At most one minute is lost
// on power failure; graceful shutdown flushes, atomic replacement preserves the old file.
internal sealed class HostHistory
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private readonly Queue<string> records = new();
    private readonly object gate = new();
    private int bytes;
    private readonly string path;
    public HostHistory(string directory)
    {
        path = Path.Combine(directory, "history.jsonl");
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("History exceeds storage limit.");
        foreach (var line in File.ReadLines(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 20 });
                if (FcsNode.ValidId(doc.RootElement.GetProperty("deviceId").GetString()) &&
                    doc.RootElement.GetProperty("receivedAtUtc").TryGetDateTimeOffset(out _)) AddLine(line);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
    }
    private void AddLine(string line)
    {
        var size = Encoding.UTF8.GetByteCount(line) + 1;
        if (size > 70000) return;
        lock (gate)
        {
            records.Enqueue(line); bytes += size;
            while (bytes > MaxBytes || records.Count > 4096) bytes -= Encoding.UTF8.GetByteCount(records.Dequeue()) + 1;
        }
    }
    public void Add(string id, DateTimeOffset received, JsonElement frame) =>
        AddLine(JsonSerializer.Serialize(new { deviceId = id, receivedAtUtc = received, frame }));
    public JsonElement[] Read(string id) { lock (gate) return records.Select(line => JsonSerializer.Deserialize<JsonElement>(line))
        .Where(r => r.GetProperty("deviceId").GetString() == id).TakeLast(64).ToArray(); }
    public void Flush()
    {
        lock (gate)
        {
            using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                writer.NewLine = "\n";
                foreach (var line in records) writer.WriteLine(line);
                writer.Flush(); stream.Flush(true);
            }
            File.Move(path + ".tmp", path, true);
        }
    }
}

internal sealed record HostSample(JsonElement Frame, DateTimeOffset ReceivedAtUtc);
internal sealed class GatewayHost : IDisposable
{
    private sealed class State(HostNode node)
    {
        public readonly HostNode Node = node;
        public readonly ConcurrentDictionary<string, JsonElement> Metadata = new();
        public HostSample? Latest;
        public bool Failed = true;
    }
    private readonly ConcurrentDictionary<string, State> nodes = new(StringComparer.Ordinal);
    private readonly HttpClient client;
    private readonly HostConfig config;
    public readonly HostHistory History;
    public TimeSpan Freshness => TimeSpan.FromMilliseconds(config.PollMilliseconds * 3 + 4000);
    public GatewayHost(HostConfig config, string directory, HttpMessageHandler? handler = null)
    {
        config.Validate(); this.config = config;
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
            { Timeout = TimeSpan.FromSeconds(3) };
        foreach (var node in config.Nodes) nodes[node.DeviceId] = new State(node);
        History = new(directory);
    }
    public static async Task<JsonElement> ReadJsonAsync(Stream stream, int maximum, CancellationToken cancel)
    {
        using var bytes = new MemoryStream(); var chunk = new byte[2048]; int count;
        while ((count = await stream.ReadAsync(chunk, cancel)) > 0)
        { if (bytes.Length + count > maximum) throw new InvalidDataException("Frame too large."); bytes.Write(chunk, 0, count); }
        using var doc = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 20 });
        return doc.RootElement.Clone();
    }
    private async Task<JsonElement> ReadAsync(HostNode node, string op, CancellationToken cancel)
    {
        using var response = await client.GetAsync($"http://{node.Ip}:8766{FcsNode.Routes[op]}" +
            (op == "data.read" ? "?mode=fresh" : ""), HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancel);
        var frame = FcsNode.Validate(await ReadJsonAsync(body, 65536, cancel), node.DeviceId);
        if (!frame.GetProperty("ok").GetBoolean()) throw new InvalidDataException("Node operation failed.");
        return frame;
    }
    private async Task PollAsync(State state, CancellationToken cancel)
    {
        try
        {
            // Identity and available operations are rechecked on every collection.
            var info = await ReadAsync(state.Node, "node.info", cancel);
            var operations = await ReadAsync(state.Node, "operations.list", cancel);
            FcsNode.Require(operations, "data.read", "lan");
            foreach (var op in new[] { "sensors.list", "sampling.get" })
            {
                FcsNode.Require(operations, op, "lan");
                state.Metadata[op] = await ReadAsync(state.Node, op, cancel);
            }
            var frame = await ReadAsync(state.Node, "data.read", cancel);
            if (frame.GetProperty("data").ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid data frame.");
            state.Metadata["node.info"] = info; state.Metadata["operations.list"] = operations;
            var received = DateTimeOffset.UtcNow;
            Volatile.Write(ref state.Latest, new(frame, received));
            History.Add(state.Node.DeviceId, received, frame);
            Volatile.Write(ref state.Failed, false);
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or IOException or OperationCanceledException or
            JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { Volatile.Write(ref state.Failed, true); }
    }
    private bool Online(State state) => !Volatile.Read(ref state.Failed) && Volatile.Read(ref state.Latest) is { } s &&
        DateTimeOffset.UtcNow - s.ReceivedAtUtc <= Freshness;
    public object[] Snapshot() => nodes.Values.OrderBy(s => s.Node.DeviceId).Select(s => (object)new {
        deviceId = s.Node.DeviceId, ip = s.Node.Ip, status = Online(s) ? "online" : "offline",
        lux = (double?)null, lastSampleUtc = Volatile.Read(ref s.Latest)?.ReceivedAtUtc, transport = "lan"
    }).ToArray();
    public object Node(string id, string op, JsonElement arguments)
    {
        arguments = FcsNode.Arguments(op, arguments);
        if (!FcsNode.Routes.ContainsKey(op)) throw new InvalidDataException("USB required.");
        if (!nodes.TryGetValue(id, out var state) || !Online(state)) throw new IOException("Device unavailable.");
        if (op != "data.read")
        {
            // Filtered sampling metadata is not represented as an unfiltered reply.
            if (arguments.EnumerateObject().Any()) throw new InvalidDataException("Filtered metadata is unavailable.");
            return new { protocol = "fcs-gateway/1", result = state.Metadata[op], transport = "lan" };
        }
        var sample = Volatile.Read(ref state.Latest)!;
        string? Filter(string name) => arguments.TryGetProperty(name, out var p) ? p.GetString() : null;
        var sensor = Filter("sensorId"); var signal = Filter("signalId");
        var filtered = sample.Frame.GetProperty("data").EnumerateArray().Where(d =>
            (sensor == null || d.GetProperty("sensorId").GetString() == sensor) &&
            (signal == null || d.GetProperty("signalId").GetString() == signal)).ToArray();
        if (filtered.Length == 0) throw new InvalidDataException("Unknown signal.");
        // Preserve every node envelope field and its sequence; transport timestamps
        // belong to the Gateway envelope, never fabricate a node sample timestamp.
        var result = sample.Frame.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value);
        result["data"] = filtered;
        return new { protocol = "fcs-gateway/1", result, transport = "lan", receivedAtUtc = sample.ReceivedAtUtc,
            sampleAgeMilliseconds = (int)Math.Max(0, (DateTimeOffset.UtcNow - sample.ReceivedAtUtc).TotalMilliseconds),
            maxAgeMilliseconds = (int)Freshness.TotalMilliseconds };
    }
    private async Task DiscoverAsync(CancellationToken cancel)
    {
        using var udp = new UdpClient(0) { EnableBroadcast = true };
        await udp.SendAsync(Encoding.ASCII.GetBytes("FC-DISCOVER/1"), new IPEndPoint(IPAddress.Broadcast, 42735), cancel);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel); deadline.CancelAfter(1500);
        try
        {
            while (nodes.Count < 32)
            {
                var reply = await udp.ReceiveAsync(deadline.Token);
                if (reply.Buffer.Length > 1024 || !HostConfig.LocalIp(reply.RemoteEndPoint.Address.ToString())) continue;
                try
                {
                    using var doc = JsonDocument.Parse(reply.Buffer);
                    var root = doc.RootElement; var id = root.GetProperty("sourceId").GetString();
                    if (root.GetProperty("protocol").GetString() == "fc-device/1" && FcsNode.ValidId(id))
                    {
                        var candidate = new HostNode(id!, reply.RemoteEndPoint.Address.ToString());
                        // The subsequent v2 poll validates identity before exposing anything online.
                        // Explicitly configured IPs always take precedence.
                        if (!config.Nodes.Any(n => n.DeviceId == id))
                            nodes.AddOrUpdate(id!, _ => new State(candidate), (_, old) => old.Node == candidate ? old : new State(candidate));
                    }
                }
                catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { }
    }
    public async Task RunAsync(CancellationToken cancel)
    {
        var discoveryAt = DateTimeOffset.MinValue; var flushAt = DateTimeOffset.UtcNow;
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                if (config.Discover && DateTimeOffset.UtcNow - discoveryAt > TimeSpan.FromSeconds(30))
                {
                    try { await DiscoverAsync(cancel); } catch (SocketException) { }
                    discoveryAt = DateTimeOffset.UtcNow;
                }
                await Parallel.ForEachAsync(nodes.Values, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancel },
                    async (state, token) => await PollAsync(state, token));
                if (DateTimeOffset.UtcNow - flushAt >= TimeSpan.FromMinutes(1)) { History.Flush(); flushAt = DateTimeOffset.UtcNow; }
                await Task.Delay(config.PollMilliseconds, cancel);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        finally { History.Flush(); }
    }
    public void Dispose() => client.Dispose();
}
