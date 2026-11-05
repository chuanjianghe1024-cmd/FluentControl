using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

int passed = 0;
void Check(bool yes, string name) { if (!yes) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
var key = new { deviceId = "c3-test", sensorId = "ambient", signalId = "illuminance" };
JsonElement Args(string? cursor = null, int wait = 1500, object? signals = null) => JsonSerializer.SerializeToElement(new {
    signals = signals ?? new[] { key }, cursor, waitMilliseconds = wait });
JsonElement Sample(JsonElement reply) => reply.GetProperty("samples")[0];
async Task<JsonElement> Read(FcsSubscriptions hub, JsonElement args, CancellationToken ct = default) => JsonSerializer.SerializeToElement(await hub.ReadAsync(args, ct));
async Task Reject(Func<Task> action, string name)
{ try { await action(); } catch (InvalidDataException) { Check(true, name); return; } throw new Exception(name); }
JsonElement Envelope(string id, long sequence, double value = 42, bool badType = false) => JsonSerializer.SerializeToElement(new {
    protocol = "fcs-gateway/1", transport = "usb", maxAgeMilliseconds = 500,
    result = new { protocol = "fcs-node/2", deviceId = id, ok = true, data = new[] {
        new { sensorId = "ambient", signalId = "illuminance", unit = "lx", valueType = "number", value = badType ? (object)"bad" : value,
            valid = true, fresh = true, status = "ready", sequence, uptimeMs = sequence * 100 },
        new { sensorId = "ambient", signalId = "other", unit = "V", valueType = "number", value = (object)3.3,
            valid = true, fresh = true, status = "ready", sequence, uptimeMs = sequence * 100 }
    } } });
long sequence = 1; int calls = 0; bool offline = false, bad = false; double number = 42;
var hub = new FcsSubscriptions((id, ct) => {
    Interlocked.Increment(ref calls);
    if (offline) throw new IOException("offline");
    return Task.FromResult(Envelope(id, sequence, number, bad));
}) { PollMilliseconds = 50 };
using var lifetime = new CancellationTokenSource();
var run = hub.RunAsync(lifetime.Token);
try
{
    await Task.Delay(100);
    Check(calls == 0, "idle Gateway does not acquire unselected devices");
    var both = new[] { key, new { deviceId = "c3-test", sensorId = "ambient", signalId = "other" } };
    var simultaneous = await Task.WhenAll(Read(hub, Args(signals: both)), Read(hub, Args(signals: both)));
    Check(calls <= 2 && simultaneous.All(r => r.GetProperty("samples").GetArrayLength() == 2), "multiple clients and signals share device acquisition");
    var initial = await Read(hub, Args()); var cursor = initial.GetProperty("cursor").GetString();
    var unchanged = await Read(hub, Args(cursor, 100));
    Check(!unchanged.GetProperty("changed").GetBoolean() && Sample(unchanged).GetProperty("value").GetDouble() == 42,
        "unchanged source does not create notifications or refresh timestamps");
    var waiting = Read(hub, Args(cursor, 1500));
    sequence++; number = 51; hub.Notify(key.deviceId);
    var changed = await waiting;
    Check(changed.GetProperty("changed").GetBoolean() && Sample(changed).GetProperty("value").GetDouble() == 51,
        "long poll wakes on a new source observation");
    var expired = await Read(hub, Args(changed.GetProperty("cursor").GetString(), 1500));
    Check(!Sample(expired).GetProperty("valid").GetBoolean() && Sample(expired).GetProperty("value").ValueKind == JsonValueKind.Null,
        "repeated fresh reads with frozen counters expire and notify");
    sequence++; hub.Notify(key.deviceId);
    var recovered = await Read(hub, Args(expired.GetProperty("cursor").GetString(), 1500));
    Check(Sample(recovered).GetProperty("valid").GetBoolean(), "new counters recover stale data");
    offline = true; hub.Notify(key.deviceId);
    var failed = await Read(hub, Args(recovered.GetProperty("cursor").GetString(), 1500));
    Check(!Sample(failed).GetProperty("valid").GetBoolean(), "source loss invalidates all consumers");
    offline = false; bad = true; sequence++; hub.Notify(key.deviceId);
    await Task.Delay(80);
    Check(!Sample(await Read(hub, Args())).GetProperty("valid").GetBoolean(), "invalid scalar type cannot become a live value");
    await Reject(async () => await hub.ReadAsync(Args(signals: new[] { key, key }), default), "duplicate selectors rejected");
    await Reject(async () => await hub.ReadAsync(Args(wait: 2001), default), "unbounded waits rejected");
    await Reject(async () => await hub.ReadAsync(JsonSerializer.SerializeToElement(new { signals = new[] { key }, command = "recovery.start" }), default), "control fields cannot enter subscriptions");
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    bool canceledRead = false;
    try { await Read(hub, Args(), canceled.Token); } catch (OperationCanceledException) { canceledRead = true; }
    Check(canceledRead, "canceled reader exits without blocking the source");
    var newHub = new FcsSubscriptions((id, ct) => Task.FromResult(Envelope(id, 100)));
    using var newLife = new CancellationTokenSource(); var newRun = newHub.RunAsync(newLife.Token);
    var reset = await Read(newHub, Args(initial.GetProperty("cursor").GetString(), 0));
    Check(reset.GetProperty("reset").GetBoolean() && !Sample(reset).GetProperty("valid").GetBoolean(), "restart resets cursor without restoring old live data");
    newLife.Cancel(); await newRun;
}
finally { lifetime.Cancel(); await run; }

int idleCalls = 0;
var leased = new FcsSubscriptions((id, ct) => { Interlocked.Increment(ref idleCalls); return Task.FromResult(Envelope(id, idleCalls)); })
    { PollMilliseconds = 50, LeaseMilliseconds = 300 };
using var leaseLife = new CancellationTokenSource(); var leaseRun = leased.RunAsync(leaseLife.Token);
await Read(leased, Args(wait: 200)); await Task.Delay(450); int stopped = idleCalls; await Task.Delay(150);
Check(idleCalls == stopped, "expired consumer lease stops device acquisition");
leaseLife.Cancel(); await leaseRun;

var slow = new FcsSubscriptions(async (id, ct) => {
    if (id == "slow-device") await Task.Delay(2500, ct);
    return Envelope(id, 1);
});
using var slowLife = new CancellationTokenSource(); var slowRun = slow.RunAsync(slowLife.Token);
await Read(slow, Args(wait: 0, signals: new[] { new { deviceId = "slow-device", sensorId = "ambient", signalId = "illuminance" } }));
var timer = Stopwatch.StartNew();
var fast = await Read(slow, Args(signals: new[] { new { deviceId = "ext-ros-light", sensorId = "ambient", signalId = "illuminance" } }));
Check(timer.ElapsedMilliseconds < 1000 && Sample(fast).GetProperty("valid").GetBoolean(), "slow physical device does not stall ROS/CAN cache delivery");
slowLife.Cancel(); await slowRun;

long isolatedSequence = 1;
var isolated = new FcsSubscriptions((id, ct) => id == "ext-missing"
    ? Task.FromException<JsonElement>(new InvalidDataException("Adapter resource unavailable."))
    : Task.FromResult(Envelope(id, isolatedSequence))) { PollMilliseconds = 50 };
using var isolatedLife = new CancellationTokenSource(); var isolatedRun = isolated.RunAsync(isolatedLife.Token);
try
{
    var missing = await Read(isolated, Args(signals: new[] { new { deviceId = "ext-missing", sensorId = "ambient", signalId = "illuminance" } }));
    Check(Sample(missing).GetProperty("status").GetString() == "unavailable" && !isolatedRun.IsCompleted,
        "invalid source selection becomes unavailable without stopping subscriptions");
    var healthy = await Read(isolated, Args());
    isolatedSequence++; isolated.Notify(key.deviceId);
    var next = await Read(isolated, Args(healthy.GetProperty("cursor").GetString()));
    Check(Sample(next).GetProperty("valid").GetBoolean() && Sample(next).GetProperty("sequence").GetInt64() == isolatedSequence && !isolatedRun.IsCompleted,
        "healthy subscribers still receive updates after another source fails");
}
finally { isolatedLife.Cancel(); await isolatedRun; }
Console.WriteLine($"{passed} shared subscription checks passed.");
