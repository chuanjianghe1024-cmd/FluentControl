using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// One live subscription core for FC, scripts and protocol exporters. Acquisition
// is shared by device; transport-specific readers never own freshness policy.
internal sealed class FcsSubscriptions(Func<string, CancellationToken, Task<JsonElement>> read)
{
    internal sealed record Key(string DeviceId, string SensorId, string SignalId)
    {
        public string Id => DeviceId + "/" + SensorId + "/" + SignalId;
    }
    private sealed record Observation(JsonElement Value, string ValueType, string Unit, long Sequence, long UptimeMs,
        DateTimeOffset ReceivedAtUtc, long ReceivedTicks, double InitialAge, int MaximumAge, string Transport);
    private sealed class Entry(Key key)
    {
        public readonly Key Key = key;
        public Observation? Sample;
        public string Status = "pending";
        public long Version, Lease = Stopwatch.GetTimestamp();
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> dirty = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim wake = new(0, 1);
    private TaskCompletionSource pulse = NewPulse();
    private readonly string epoch = Guid.NewGuid().ToString("N");
    private long revision;
    internal int PollMilliseconds { get; init; } = 500;
    internal int LeaseMilliseconds { get; init; } = 10000;
    private static TaskCompletionSource NewPulse() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void Pulse() { var old = pulse; pulse = NewPulse(); old.TrySetResult(); }
    public void Notify(string device)
    {
        lock (gate)
        {
            if (!entries.Values.Any(e => e.Key.DeviceId == device)) return;
            dirty.Add(device);
            if (wake.CurrentCount == 0) wake.Release();
        }
    }
    private static void Fields(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid subscription arguments.");
        var seen = new HashSet<string>();
        foreach (var field in value.EnumerateObject())
            if (!allowed.Contains(field.Name) || !seen.Add(field.Name)) throw new InvalidDataException("Unknown or repeated subscription field.");
    }
    private static Key[] Keys(JsonElement args)
    {
        var signals = args.GetProperty("signals");
        if (signals.ValueKind != JsonValueKind.Array || signals.GetArrayLength() is < 1 or > 16)
            throw new InvalidDataException("Subscribe to 1..16 signals per request.");
        var keys = new List<Key>();
        foreach (var item in signals.EnumerateArray())
        {
            Fields(item, "deviceId", "sensorId", "signalId");
            var key = new Key(item.GetProperty("deviceId").GetString()!, item.GetProperty("sensorId").GetString()!, item.GetProperty("signalId").GetString()!);
            if (!FcsNode.ValidId(key.DeviceId) || !FcsNode.ValidId(key.SensorId) || !FcsNode.ValidId(key.SignalId) || key.SignalId.Length > 64 || keys.Contains(key))
                throw new InvalidDataException("Invalid or duplicate subscription identity.");
            keys.Add(key);
        }
        return keys.OrderBy(k => k.Id, StringComparer.Ordinal).ToArray();
    }
    private void Sweep()
    {
        foreach (var key in entries.Where(p => Stopwatch.GetElapsedTime(p.Value.Lease).TotalMilliseconds > LeaseMilliseconds).Select(p => p.Key).ToArray())
            entries.Remove(key);
    }
    private static double Age(Observation sample) => sample.InitialAge + Stopwatch.GetElapsedTime(sample.ReceivedTicks).TotalMilliseconds;
    private void Status(Entry entry, string status)
    {
        if (entry.Status == status) return;
        entry.Status = status; entry.Version = ++revision; Pulse();
    }
    private void Expire(Entry entry)
    {
        if (entry.Status == "ready" && entry.Sample is { } sample && Age(sample) > sample.MaximumAge) Status(entry, "stale");
    }
    private string Cursor(Key[] keys) => epoch + "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", keys.Select(k => k.Id + ":" + entries[k.Id].Version))))).ToLowerInvariant();
    private object Snapshot(Entry entry)
    {
        var s = entry.Sample; var valid = entry.Status == "ready";
        return new { deviceId = entry.Key.DeviceId, sensorId = entry.Key.SensorId, signalId = entry.Key.SignalId,
            valueType = s?.ValueType, unit = s?.Unit, value = valid ? (object?)s!.Value : null, valid, fresh = valid,
            status = entry.Status, sequence = s?.Sequence ?? 0, uptimeMs = s?.UptimeMs ?? 0,
            receivedAtUtc = s?.ReceivedAtUtc, sampleAgeMilliseconds = s == null ? (int?)null : (int)Math.Min(int.MaxValue, Age(s)),
            maxAgeMilliseconds = s?.MaximumAge, transport = s?.Transport };
    }
    public async Task<object> ReadAsync(JsonElement args, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        Fields(args, "signals", "cursor", "waitMilliseconds");
        var keys = Keys(args);
        var cursor = args.TryGetProperty("cursor", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetString() : null;
        if (cursor is { Length: > 100 }) throw new InvalidDataException("Invalid subscription cursor.");
        var wait = 0;
        if (args.TryGetProperty("waitMilliseconds", out var w) && (w.ValueKind != JsonValueKind.Number || !w.TryGetInt32(out wait)))
            throw new InvalidDataException("Invalid subscription wait.");
        if (wait is < 0 or > 2000) throw new InvalidDataException("Subscription wait must be 0..2000 ms.");
        var started = Stopwatch.GetTimestamp();
        lock (gate)
        {
            Sweep();
            if (entries.Count + keys.Count(k => !entries.ContainsKey(k.Id)) > 128 ||
                entries.Values.Select(e => e.Key.DeviceId).Concat(keys.Select(k => k.DeviceId)).Distinct().Count() > 32)
                throw new InvalidDataException("Active subscription limit reached.");
            foreach (var key in keys)
            {
                if (!entries.TryGetValue(key.Id, out var entry))
                {
                    entries.Add(key.Id, entry = new Entry(key)); dirty.Add(key.DeviceId);
                    entry.Version = ++revision;
                }
                entry.Lease = Stopwatch.GetTimestamp();
            }
            if (wake.CurrentCount == 0) wake.Release();
        }
        while (true)
        {
            cancel.ThrowIfCancellationRequested(); Task changed; int remaining;
            lock (gate)
            {
                foreach (var key in keys) Expire(entries[key.Id]);
                var current = Cursor(keys);
                remaining = wait - (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                // First request waits for initial collection (up to its deadline).
                var pending = keys.Any(k => entries[k.Id].Status == "pending");
                if (remaining <= 0 || cursor != current && !(cursor == null && pending))
                    return new { protocol = "fcs-gateway/1", subscriptionProtocol = "fcs-subscription/1", cursor = current,
                        waitedMilliseconds = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        changed = cursor != current, reset = cursor != null && !cursor.StartsWith(epoch + ".", StringComparison.Ordinal),
                        samples = keys.Select(k => Snapshot(entries[k.Id])).ToArray() };
                changed = pulse.Task;
            }
            try { await changed.WaitAsync(TimeSpan.FromMilliseconds(remaining), cancel); }
            catch (TimeoutException) { }
        }
    }
    public async Task<JsonElement> ScriptFrameAsync(JsonElement input, CancellationToken cancel)
    {
        var key = new { deviceId = input.GetProperty("deviceId").GetString(), sensorId = input.GetProperty("sensorId").GetString(), signalId = input.GetProperty("signalId").GetString() };
        var response = JsonSerializer.SerializeToElement(await ReadAsync(JsonSerializer.SerializeToElement(new { signals = new[] { key }, waitMilliseconds = 1500 }), cancel));
        var sample = response.GetProperty("samples")[0];
        if (!sample.GetProperty("valid").GetBoolean() || sample.GetProperty("sampleAgeMilliseconds").GetInt32() > 5000)
            throw new IOException("Script input unavailable or too old.");
        return JsonSerializer.SerializeToElement(new { protocol = "fcs-node/2", deviceId = key.deviceId, ok = true, data = response.GetProperty("samples") });
    }
    private static string String(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing sample field.");
    private void Apply(Entry entry, JsonElement envelope)
    {
        if (String(envelope, "protocol") != "fcs-gateway/1") throw new InvalidDataException("Wrong Gateway envelope.");
        var frame = FcsNode.Validate(envelope.GetProperty("result"), entry.Key.DeviceId);
        if (!frame.GetProperty("ok").GetBoolean()) throw new InvalidDataException("Node unavailable.");
        var matches = frame.GetProperty("data").EnumerateArray().Where(v => String(v, "sensorId") == entry.Key.SensorId && String(v, "signalId") == entry.Key.SignalId).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Missing or duplicate signal.");
        var value = matches[0];
        if (!value.GetProperty("valid").GetBoolean()) { Status(entry, "unavailable"); return; }
        if (!value.GetProperty("fresh").GetBoolean()) { Status(entry, "stale"); return; }
        var type = String(value, "valueType"); var unit = String(value, "unit"); var scalar = value.GetProperty("value");
        if (unit.Length > 64 || !(type switch {
            "number" => scalar.ValueKind == JsonValueKind.Number && scalar.TryGetDouble(out var number) && double.IsFinite(number),
            "integer" => scalar.ValueKind == JsonValueKind.Number && scalar.TryGetInt64(out _),
            "boolean" => scalar.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "string" => scalar.ValueKind == JsonValueKind.String && scalar.GetString()!.Length <= 1024,
            _ => false })) throw new InvalidDataException("Invalid sample type or value.");
        var sequence = value.GetProperty("sequence").GetInt64(); var uptime = value.GetProperty("uptimeMs").GetInt64();
        if (sequence < 0 || uptime < 0) throw new InvalidDataException("Invalid sample counters.");
        var transport = String(envelope, "transport"); var timing = envelope;
        if (transport == "adapter" && value.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("fcs.adapter", out var adapter)) timing = adapter;
        var received = timing.TryGetProperty("receivedAtUtc", out var at) ? at.GetDateTimeOffset() : DateTimeOffset.UtcNow;
        double age = timing.TryGetProperty("sampleAgeMilliseconds", out var a) ? a.GetDouble() : 0;
        var maximum = timing.TryGetProperty("maxAgeMilliseconds", out var m) ? m.GetInt32() :
            envelope.TryGetProperty("maxAgeMilliseconds", out m) ? m.GetInt32() : 5000;
        if (!double.IsFinite(age) || age < 0 || maximum < 500 || received > DateTimeOffset.UtcNow.AddSeconds(1))
            throw new InvalidDataException("Invalid sample timing.");
        maximum = Math.Min(maximum, 60000); // A slower source cannot extend the live subscription ceiling.
        age = Math.Max(age, Math.Max(0, (DateTimeOffset.UtcNow - received).TotalMilliseconds));
        if (entry.Sample is { } old && old.Sequence == sequence && old.UptimeMs == uptime)
        {
            if (old.Value.GetRawText() != scalar.GetRawText() || old.Unit != unit || old.ValueType != type)
                throw new InvalidDataException("Sample changed without a new sequence.");
            // A repeated fetch can only shorten, never extend, the live lifetime.
            var oldAge = Age(old);
            entry.Sample = old with { InitialAge = Math.Max(age, oldAge), ReceivedTicks = Stopwatch.GetTimestamp(), MaximumAge = Math.Min(old.MaximumAge, maximum) };
        }
        else
        {
            entry.Sample = new(scalar.Clone(), type, unit, sequence, uptime, received, Stopwatch.GetTimestamp(), age, maximum, transport);
            entry.Version = ++revision; Pulse();
        }
        Status(entry, Age(entry.Sample) <= entry.Sample.MaximumAge ? "ready" : "stale");
    }
    private async Task Collect(string device, CancellationToken cancel)
    {
        JsonElement envelope = default;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel); deadline.CancelAfter(3000);
            envelope = await read(device, deadline.Token);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or HttpRequestException or OperationCanceledException or InvalidOperationException or JsonException or KeyNotFoundException or TimeoutException or UnauthorizedAccessException or System.Net.Sockets.SocketException) { }
        lock (gate)
        {
            foreach (var entry in entries.Values.Where(e => e.Key.DeviceId == device))
            {
                try { Apply(entry, envelope); }
                catch (Exception e) when (e is InvalidDataException or InvalidOperationException or JsonException or KeyNotFoundException or FormatException or OverflowException)
                { Status(entry, "unavailable"); }
            }
        }
    }
    public async Task RunAsync(CancellationToken cancel)
    {
        long lastPoll = 0;
        using var physicalSlots = new SemaphoreSlim(4);
        var running = new Dictionary<string, Task>(StringComparer.Ordinal);
        async Task CollectBounded(string id)
        {
            // Virtual adapters read RAM and must not wait behind an offline USB device.
            var physical = !id.StartsWith("ext-", StringComparison.Ordinal);
            if (physical) await physicalSlots.WaitAsync(cancel);
            try
            {
                lock (gate) if (!entries.Values.Any(e => e.Key.DeviceId == id)) return;
                await Collect(id, cancel);
            }
            finally { if (physical) physicalSlots.Release(); }
        }
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                foreach (var id in running.Where(p => p.Value.IsCompleted).Select(p => p.Key).ToArray())
                { await running[id]; running.Remove(id); }
                string[] devices;
                lock (gate)
                {
                    Sweep(); foreach (var entry in entries.Values) Expire(entry);
                    var periodic = lastPoll == 0 || Stopwatch.GetElapsedTime(lastPoll).TotalMilliseconds >= PollMilliseconds;
                    devices = entries.Values.Select(e => e.Key.DeviceId).Distinct().Where(id => periodic || dirty.Contains(id)).ToArray();
                    dirty.Clear(); if (periodic) lastPoll = Stopwatch.GetTimestamp();
                }
                foreach (var device in devices)
                    if (!running.ContainsKey(device)) running.Add(device, CollectBounded(device));
                await wake.WaitAsync(Math.Min(PollMilliseconds, 100), cancel);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        finally
        {
            try { await Task.WhenAll(running.Values); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        }
    }
}
