using System.Text.Json;

int passed = 0;
void Check(bool yes, string name) { if (!yes) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
void Reject(Action action, string name)
{ try { action(); } catch (InvalidDataException) { Check(true, name); return; } throw new Exception(name); }
FcsAdapters.Signal Signal(string device, string signal) => new() { DeviceId = device, SensorId = "ambient", SignalId = signal, Unit = "lx" };
FcsAdapters.Grant Grant(string id, params FcsAdapters.Signal[] signals) => new() { Id = id, Kind = "custom", Enabled = true, Signals = signals };
var root = Path.Combine(Path.GetTempPath(), "fcs-grants-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
FcsAdapters Load(string name, params FcsAdapters.Grant[] grants)
{
    var path = Path.Combine(root, name); Directory.CreateDirectory(path);
    File.WriteAllText(Path.Combine(path, "adapters.json"), JsonSerializer.Serialize(new FcsAdapters.Configuration { ListenPort = 18767, Adapters = grants }, FcsAdapters.Json));
    return new FcsAdapters(path);
}
try
{
    var a = Grant("a", Signal("ext-a-b-main", "first"));
    var b = Grant("a-b", Signal("ext-a-b-main", "second"));
    foreach (var (name, grants) in new[] { ("forward", new[] { a, b }), ("reverse", new[] { b, a }) })
    {
        Reject(() => Load(name, grants), "conflicting device owners rejected: " + name);
        Check(!Directory.Exists(Path.Combine(root, name, "adapter-tokens")), "invalid configuration creates no producer tokens: " + name);
    }
    Reject(() => Load("same-signal", a, b with { Signals = a.Signals }), "same device and signal conflict rejected explicitly");
    var disabled = Load("disabled", a, b with { Enabled = false });
    Check(disabled.Snapshot().Length == 1, "disabled grants cannot replace active device ownership");
    var first = Signal("ext-a-main", "first"); var sibling = Signal("ext-a-main", "second");
    var other = Signal("ext-a-b-main", "other");
    var adapters = Load("valid", Grant("a", first, sibling), Grant("a-b", other));
    Check(adapters.Snapshot().Length == 2, "overlapping prefixes with distinct devices remain valid");
    string Token(string id) => File.ReadAllText(Path.Combine(root, "valid", "adapter-tokens", id + ".token"));
    object Value(FcsAdapters.Signal signal, int value) => new { deviceId = signal.DeviceId, sensorId = signal.SensorId, signalId = signal.SignalId,
        sequence = 1, receivedAtUtc = DateTimeOffset.UtcNow, valid = true, value };
    JsonElement Frame(string id, params object[] data) => JsonSerializer.SerializeToElement(new {
        protocol = "fcs-adapter/1", adapterId = id, token = Token(id), bootId = Guid.NewGuid().ToString("N"), sequence = 0, data });
    JsonElement Read(FcsAdapters.Signal signal) => JsonSerializer.SerializeToElement(adapters.Node(signal.DeviceId, "data.read",
        JsonSerializer.SerializeToElement(new { sensorId = signal.SensorId, signalId = signal.SignalId, mode = "fresh" }))).GetProperty("result").GetProperty("data")[0];
    Reject(() => adapters.Ingest(Frame("a-b", Value(other, 9), Value(first, 99))), "producer cannot publish another grant's signal");
    Check(!Read(other).GetProperty("valid").GetBoolean(), "rejected mixed-authority batch commits no partial data");
    adapters.Ingest(Frame("a", Value(first, 1), Value(sibling, 2)));
    adapters.Ingest(Frame("a-b", Value(other, 3)));
    Check(Read(first).GetProperty("value").GetInt32() == 1 && Read(sibling).GetProperty("value").GetInt32() == 2 &&
        Read(other).GetProperty("value").GetInt32() == 3, "multiple signals under one owner and separate producers retain authority");
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"{passed} adapter ownership checks passed.");
