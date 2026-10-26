using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class HostChecks
{
    public static bool Authorized(string header, string token) => header.Length == 71 && header.StartsWith("Bearer ", StringComparison.Ordinal) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(header[7..]), Encoding.ASCII.GetBytes(token));
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Host self-test failed."); }
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fcs-host-test-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            Check(Authorized("Bearer " + new string('a', 64), new string('a', 64)));
            Check(!Authorized("Bearer " + new string('b', 64), new string('a', 64)));
            Check(!Authorized("", new string('a', 64)));
            Check(!HostConfig.LocalIp("127.0.0.1") && !HostConfig.LocalIp("8.8.8.8") && HostConfig.LocalIp("192.168.1.8"));
            try { new HostConfig { Nodes = [new("../bad", "192.168.1.8")] }.Validate(); throw new Exception("Accepted invalid config"); }
            catch (InvalidDataException) { }
            using var handler = new FakeNode();
            using var host = new GatewayHost(new HostConfig { PollMilliseconds = 1000, Nodes = [new("test-node", "192.168.1.8")] }, directory, handler);
            using var lifetime = new CancellationTokenSource(); var loop = host.RunAsync(lifetime.Token);
            await handler.Read.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Poll publishes immediately after receiving the response; yield until visible.
            for (var i = 0; i < 100 && host.History.Read("test-node").Length == 0; i++) await Task.Delay(10);
            using var args = JsonDocument.Parse("{\"sensorId\":\"ambient-01\",\"signalId\":\"illuminance\",\"mode\":\"fresh\"}");
            var first = JsonSerializer.SerializeToElement(host.Node("test-node", "data.read", args.RootElement));
            var count = handler.Requests;
            for (var i = 0; i < 10; i++) host.Node("test-node", "data.read", args.RootElement);
            Check(handler.Requests == count); // Client count does not multiply physical collection.
            Check(first.GetProperty("result").GetProperty("data").GetArrayLength() == 1);
            Check(first.GetProperty("receivedAtUtc").TryGetDateTimeOffset(out _));
            try { host.Node("test-node", "sampling.set", JsonSerializer.SerializeToElement(new { sensorId = "ambient-01", enabled = true, intervalMs = 1000 })); throw new Exception("Write exposed"); }
            catch (InvalidDataException) { }
            handler.WrongIdentity = true;
            await Task.Delay(1300);
            try { host.Node("test-node", "data.read", args.RootElement); throw new Exception("Stale data exposed"); } catch (IOException) { }
            lifetime.Cancel(); await loop;
            var history = new HostHistory(directory); Check(history.Read("test-node").Length > 0);
            using var restarted = new GatewayHost(new HostConfig { Nodes = [new("test-node", "192.168.1.8")] }, directory);
            try { restarted.Node("test-node", "data.read", args.RootElement); throw new Exception("History restored as live"); } catch (IOException) { }
            for (var i = 0; i < 4200; i++) history.Add("test-node", DateTimeOffset.UtcNow, first);
            history.Flush(); Check(File.ReadLines(Path.Combine(directory, "history.jsonl")).Count() == 4096);
            Check(new FileInfo(Path.Combine(directory, "history.jsonl")).Length <= 8 * 1024 * 1024);
            Check(new HostHistory(directory).Read("test-node").Length == 64);
            var large = JsonSerializer.SerializeToElement(new { payload = new string('x', 20000) });
            for (var i = 0; i < 500; i++) history.Add("large-node", DateTimeOffset.UtcNow, large);
            history.Flush();
            Check(new FileInfo(Path.Combine(directory, "history.jsonl")).Length <= 8 * 1024 * 1024);
            Check(File.ReadLines(Path.Combine(directory, "history.jsonl")).Count() < 500);
            File.AppendAllText(Path.Combine(directory, "history.jsonl"), "{damaged-tail\n");
            Check(new HostHistory(directory).Read("large-node").Length == 64);
            try { await GatewayHost.ReadJsonAsync(new MemoryStream(new byte[4097]), 4096, default); throw new Exception("Oversize accepted"); }
            catch (InvalidDataException) { }
            Console.WriteLine("FCS host: auth, configuration, identity, read-only boundary, single collection, stale data, restart, history bounds passed.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class FakeNode : HttpMessageHandler
    {
        public readonly TaskCompletionSource Read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WrongIdentity;
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Interlocked.Increment(ref Requests);
            var root = new Dictionary<string, object> { ["protocol"] = "fcs-node/2", ["deviceId"] = WrongIdentity ? "other" : "test-node", ["ok"] = true };
            if (request.RequestUri!.AbsolutePath == "/v2/operations") root["operations"] = FcsNode.Routes.Keys.Select(id => new { id, state = "available", access = "read", transports = new[] { "lan" } });
            if (request.RequestUri.AbsolutePath == "/v2/data")
            { root["data"] = new[] { new { sensorId = "ambient-01", signalId = "illuminance", value = 42, unit = "lx", valueType = "number", valid = true, fresh = true } }; Read.TrySetResult(); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(root)) });
        }
    }
}
