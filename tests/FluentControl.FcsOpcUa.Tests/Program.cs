using System.Net;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentControl.FcsOpcUa;
using Opc.Ua;
using Opc.Ua.Client;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException) { Check(true, name); return; } throw new Exception(name); }
int Port() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return port; }
var temp = Path.Combine(Path.GetTempPath(), "fcs-ua-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
var signal = new Signal { DeviceId = "c3-test", SensorId = "ambient-01", SignalId = "illuminance", Unit = "lx" };
var key = signal.Key("house", "gateway");
var origin = new Options { SystemId = "house", GatewayId = "gateway", Endpoint = $"opc.tcp://localhost:{Port()}/fcs", PkiDirectory = Path.Combine(temp, "origin"),
    GatewayUrl = $"http://127.0.0.1:{Port()}/", GatewayTokenFile = Path.Combine(temp, "gateway.token"), Signals = [signal], PollMilliseconds = 500, StaleMilliseconds = 2000 };
var mount = new Mount { SystemId = "house", GatewayId = "gateway", Endpoint = origin.Endpoint, Signals = [signal] };
var aggregate = new Options { SystemId = "office", GatewayId = "nas", Endpoint = $"opc.tcp://localhost:{Port()}/fcs", PkiDirectory = Path.Combine(temp, "aggregate"), Mounts = [mount], PollMilliseconds = 500, StaleMilliseconds = 2000 };
var clientOptions = new Options { SystemId = "client", GatewayId = "test", PkiDirectory = Path.Combine(temp, "client") };
origin.Validate(); aggregate.Validate();
Reject(() => (origin with { SystemId = "../oops" }).Validate(), "invalid identity rejected");
Reject(() => (origin with { GatewayUrl = "http://192.168.1.1/" }).Validate(), "HTTP bearer restricted to loopback");
Reject(() => (origin with { Signals = [signal, signal] }).Validate(), "duplicate resources rejected");
Reject(() => (origin with { Mounts = [mount] }).Validate(), "self mount rejected");
Reject(() => (origin with { Signals = [signal with { ValueType = "object" }] }).Validate(), "unsupported complex type rejected");
await File.WriteAllTextAsync(origin.GatewayTokenFile!, new string('a', 64));
using var cts = new CancellationTokenSource();
using var http = new HttpListener(); http.Prefixes.Add(origin.GatewayUrl!); http.Start();
long sequence = 0; int requests = 0; bool frozen = false;
var sampleTimes = new System.Collections.Concurrent.ConcurrentDictionary<long, DateTime>();
JsonElement Envelope(long seq, bool fresh = true, bool valid = true, double value = 42) => JsonSerializer.SerializeToElement(new {
    protocol = "fcs-gateway/1", subscriptionProtocol = "fcs-subscription/1", cursor = "fixture." + seq, changed = true, waitedMilliseconds = 0,
    samples = new[] { new { deviceId = signal.DeviceId, sensorId = signal.SensorId, signalId = signal.SignalId, valueType = "number", unit = "lx", value,
        valid, fresh, status = !valid ? "unavailable" : fresh ? "ready" : "stale", sequence = seq, uptimeMs = seq * 500,
        sampleAgeMilliseconds = 0, maxAgeMilliseconds = 2000, receivedAtUtc = sampleTimes.GetOrAdd(seq, _ => DateTime.UtcNow) } }
});
var fixture = Task.Run(async () => {
    try {
        while (!cts.IsCancellationRequested) {
            var context = await http.GetContextAsync().WaitAsync(cts.Token);
            using var body = new StreamReader(context.Request.InputStream);
            using var request = JsonDocument.Parse(await body.ReadToEndAsync(cts.Token));
            var json = request.RootElement;
            if (context.Request.Headers["Authorization"] != "Bearer " + new string('a', 64) ||
                json.GetProperty("operation").GetString() != "subscriptions.read" ||
                json.GetProperty("arguments").GetProperty("signals")[0].GetProperty("deviceId").GetString() != signal.DeviceId)
                throw new Exception("Unexpected Gateway request or credentials.");
            Interlocked.Increment(ref requests);
            var payload = Encoding.UTF8.GetBytes(Envelope(frozen ? sequence : Interlocked.Increment(ref sequence)).GetRawText());
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, cts.Token); context.Response.Close();
        }
    } catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
});
try
{
    var sourceConfig = await UaConfiguration.Create(origin);
    var aggregateConfig = await UaConfiguration.Create(aggregate);
    var clientConfig = await UaConfiguration.Create(clientOptions);
    async Task Trust(ApplicationConfiguration receiver, ApplicationConfiguration peer)
    {
        using var store = receiver.SecurityConfiguration.TrustedPeerCertificates.OpenStore(UaConfiguration.Telemetry);
        var certificate = await peer.SecurityConfiguration.ApplicationCertificate.FindAsync(true, null, UaConfiguration.Telemetry);
        await store.AddAsync(certificate);
        await receiver.CertificateValidator.UpdateAsync(receiver);
    }
    await Trust(sourceConfig, aggregateConfig); await Trust(aggregateConfig, sourceConfig);
    await Trust(clientConfig, sourceConfig); await Trust(clientConfig, aggregateConfig);
    await Trust(sourceConfig, clientConfig); await Trust(aggregateConfig, clientConfig);
    using var source = new UaServer(origin); await source.StartAsync(sourceConfig);
    using var target = new UaServer(aggregate); await target.StartAsync(aggregateConfig);
    using var reader = new GatewayReader(origin, source.Resources);
    Check(reader.Convert(Envelope(1), signal).StatusCode == StatusCodes.Good, "typed FCS sample decoded");
    Check(reader.Convert(Envelope(2, fresh: false), signal).StatusCode == StatusCodes.BadNoCommunication, "latest cache not labeled real-time");
    Check(reader.Convert(Envelope(3, valid: false), signal).StatusCode == StatusCodes.BadSensorFailure, "unavailable driver not fabricated");
    var mixed = JsonNode.Parse(Envelope(100).GetRawText())!;
    var sibling = mixed["samples"]![0]!.DeepClone();
    sibling["signalId"] = "stale-sibling"; sibling["receivedAtUtc"] = DateTime.UtcNow.AddMinutes(-1);
    mixed["samples"]!.AsArray().Add(sibling);
    Check(reader.Convert(JsonSerializer.SerializeToElement(mixed), signal).StatusCode == StatusCodes.Good,
        "independent subscription timing survives a stale sibling");
    Check(MountReader.ValidateSample(signal, new DataValue { Value = null, StatusCode = StatusCodes.Good, SourceTimestamp = DateTime.UtcNow }, 2000).StatusCode == StatusCodes.BadTypeMismatch, "null mount value cannot resurrect last good value");
    Check(MountReader.ValidateSample(signal, new DataValue { Value = 1d, StatusCode = StatusCodes.Good, SourceTimestamp = DateTime.UtcNow.AddMinutes(-1) }, 2000).StatusCode == StatusCodes.BadNoCommunication, "mount preserves stale source age");
    if (OperatingSystem.IsWindows())
    {
        var pipeName = "fcs-ua-test-" + Guid.NewGuid().ToString("N");
        await using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            var pipeFixture = Task.Run(async () => {
                await pipe.WaitForConnectionAsync(cts.Token);
                using var input = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var message = JsonDocument.Parse((await input.ReadLineAsync(cts.Token))!);
                if (message.RootElement.GetProperty("operation").GetString() != "subscriptions.read") throw new Exception("Unexpected pipe command");
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(Envelope(99).GetRawText() + "\n"), cts.Token);
                await pipe.FlushAsync(cts.Token);
            });
            using var pipeReader = new GatewayReader(origin with { WindowsPipe = true, GatewayUrl = null }, source.Resources, pipeName);
            var fromPipe = await pipeReader.Read(signal.DeviceId, cts.Token);
            Check(pipeReader.Convert(fromPipe, signal).StatusCode == StatusCodes.Good, "same-user named pipe transport reads node data");
            await pipeFixture;
        }
    }
    var collection = reader.Run(cts.Token);
    var watchdog = source.Resources.Watchdog(origin.StaleMilliseconds, cts.Token);
    var mountTask = MountReader.Run(aggregate, mount, aggregateConfig, target.Resources, cts.Token);
    var mountWatchdog = target.Resources.Watchdog(aggregate.StaleMilliseconds, cts.Token);
    using var session = await MountReader.Connect(clientConfig, origin.Endpoint);
    using var targetSession = await MountReader.Connect(clientConfig, aggregate.Endpoint);
    NodeId Node(ISession s, string id) => new(id, checked((ushort)s.NamespaceUris.GetIndex(ResourceManager.NamespaceUri)));
    async Task<DataValue> WaitValue(ISession s, Func<DataValue, bool> predicate, int timeout = 12000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < until) {
            var response = await s.ReadAsync(null, 0, TimestampsToReturn.Both, new ReadValueIdCollection { new ReadValueId { NodeId = Node(s, key), AttributeId = Attributes.Value } }, cts.Token);
            var value = response.Results[0]; if (predicate(value)) return value;
            await Task.Delay(100);
        }
        throw new Exception("Timed out waiting for expected UA data/status.");
    }
    var good = await WaitValue(session, v => StatusCode.IsGood(v.StatusCode));
    Check((double)good.Value == 42 && requests > 0, "real HTTP Gateway to encrypted OPC UA read");
    var browse = await session.BrowseAsync(null, null, Node(session, "FCS"), 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true, 0, cts.Token);
    var children = browse.Item3;
    Check(children.Any(n => n.DisplayName.Text == "house"), "browse system hierarchy");
    var write = new WriteValueCollection { new WriteValue { NodeId = Node(session, key), AttributeId = Attributes.Value, Value = new DataValue(999d) } };
    var written = await session.WriteAsync(null, write, cts.Token);
    var results = written.Results;
    Check(results[0] == StatusCodes.BadNotWritable || results[0] == StatusCodes.BadUserAccessDenied, "remote signal writes denied");
    var notification = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 100 };
    var item = new MonitoredItem(subscription.DefaultItem) { StartNodeId = Node(session, key), AttributeId = Attributes.Value,
        SamplingInterval = 100, QueueSize = 1, Filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValueTimestamp } };
    item.Notification += (_, _) => { foreach (var v in item.DequeueValues()) if (StatusCode.IsGood(v.StatusCode)) notification.TrySetResult(true); };
    subscription.AddItem(item); session.AddSubscription(subscription); await subscription.CreateAsync(cts.Token);
    Check(await notification.Task.WaitAsync(TimeSpan.FromSeconds(10)), "real OPC UA subscription notification");
    var imported = await WaitValue(targetSession, v => StatusCode.IsGood(v.StatusCode));
    Check((double)imported.Value == 42 && imported.SourceTimestamp > DateTime.UtcNow.AddSeconds(-5), "encrypted gateway cascade preserves origin sample timestamp");
    Check((string)(await targetSession.ReadValueAsync(Node(targetSession, key + "/ExportKind"), cts.Token)).Value == "mounted", "mounted resource cannot be implicitly re-exported");
    frozen = true;
    await WaitValue(session, v => v.StatusCode == StatusCodes.BadNoCommunication);
    await WaitValue(targetSession, v => v.StatusCode == StatusCodes.BadNoCommunication);
    Check(true, "unchanged counters expire across source and mount");
    frozen = false;
    await WaitValue(targetSession, v => StatusCode.IsGood(v.StatusCode));
    Check(true, "fresh samples recover subscription after stale state");
    var untrusted = await UaConfiguration.Create(clientOptions with { SystemId = "stranger", PkiDirectory = Path.Combine(temp, "stranger") });
    await Trust(untrusted, sourceConfig);
    bool denied = false;
    try { using var stranger = await MountReader.Connect(untrusted, origin.Endpoint); } catch (ServiceResultException) { denied = true; }
    Check(denied, "untrusted application certificate denied");
    http.Stop();
    await WaitValue(session, v => v.StatusCode == StatusCodes.BadNotConnected);
    await WaitValue(targetSession, v => StatusCode.IsBad(v.StatusCode));
    Check(true, "Gateway loss reported downstream");
    cts.Cancel();
    try { await Task.WhenAll(collection, watchdog, mountTask, mountWatchdog); } catch (OperationCanceledException) { }
    await source.StopAsync(); await target.StopAsync();
}
finally
{
    cts.Cancel(); http.Stop();
    try { await fixture; } catch (Exception e) when (e is HttpListenerException or OperationCanceledException or ObjectDisposedException) { }
    Directory.Delete(temp, true);
}
Console.WriteLine($"{passed} OPC UA checks passed.");
