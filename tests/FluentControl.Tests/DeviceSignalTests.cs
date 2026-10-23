using FluentControl.Services;
using System.Net;
using System.Net.Http;
using System.Text.Json;
internal static class DeviceSignalTests
{
    private static void Check(bool b) { if (!b) throw new Exception("Device signal regression"); }
    internal static async Task RunAsync()
    {
        Check(WifiSetupNetwork.TryUrl(JsonSerializer.SerializeToElement(new { state = "connected", ip = "192.168.1.25" })) == "http://192.168.1.25:8766/v1/sample");
        foreach (var status in new[] { "configuring", "idle", "failed_restored", "disabled" })
            Check(WifiSetupNetwork.TryUrl(JsonSerializer.SerializeToElement(new { state = status, ip = "192.168.1.25" })) is null);
        foreach (var ip in new[] { "", "0.0.0.0", "127.0.0.1", "224.0.0.1", "169.254.1.1", "::1", "http://evil.test/" })
            Check(WifiSetupNetwork.TryUrl(JsonSerializer.SerializeToElement(new { state = "connected", ip })) is null);
        var key = new string('1', 64); var nonce = new string('a', 32);
        Check(DeviceSignals.Verify(key, "message", DeviceSignals.Mac(key, "message")));
        Check(!DeviceSignals.Verify(key, "changed", DeviceSignals.Mac(key, "message")));
        Check(!DeviceSignals.Verify(key, "message", "not hex"));
        var link = DeviceSignals.SubscriptionLink("c3-test_01"); Check(DeviceSignals.LinkIdentity(link) == "c3-test_01");
        foreach (var s in new[] { "fluentcontrol://subscribe?device=x&operation=reset", "fluentcontrol://subscribe?device=../x", "fluentcontrol://subscribe?device=x#reset", "https://subscribe?device=x" }) Check(DeviceSignals.LinkIdentity(s) is null);
        var frame = JsonSerializer.SerializeToElement(new { protocol = "fc-device/1", sourceId = "board-A", signal = "temperature", unit = "Cel", value = 21.5, valid = true, sequence = 1, uptimeMs = 200 });
        Check(DeviceSignals.Parse(frame).Value == 21.5);
        try { DeviceSignals.Light(frame, "board-A", DateTimeOffset.UtcNow); throw new Exception("Temperature accepted as lux"); } catch (InvalidDataException) { }
        frame = JsonSerializer.SerializeToElement(new { protocol = "fc-device/1", sourceId = "board-A", signal = "illuminance", unit = "lx", value = 21.5, valid = true, sequence = 1, uptimeMs = 200 });
        Check(DeviceSignals.Light(frame, "board-A", DateTimeOffset.UtcNow).Lux == 21.5);
        try { DeviceSignals.Light(frame, "board-B", DateTimeOffset.UtcNow); throw new Exception("Identity mismatch accepted"); } catch (InvalidDataException) { }
        Check(NetworkSignalSensor.IsPublicLightEndpoint("http://192.168.1.7:8766/v1/sample"));
        Check(!NetworkSignalSensor.IsPublicLightEndpoint("http://192.168.1.7:8766/v1/management"));
        Check(!NetworkSignalSensor.IsPublicLightEndpoint("http://192.168.1.7:9999/v1/sample"));
        using (var publicClient = new HttpClient(new PublicReply()))
        {
            var read = await NetworkSignalSensor.ReadPublicAsync(publicClient,
                new() { DeviceId = "public-sensor", Url = "http://192.0.2.1:8766/v1/sample" }, CancellationToken.None);
            Check(read.Lux == 71);
            try
            {
                await NetworkSignalSensor.ReadPublicAsync(publicClient,
                    new() { DeviceId = "unknown-sensor", Url = "http://192.0.2.1:8766/v1/sample" }, CancellationToken.None);
                throw new Exception("Public LAN accepted a mismatched identity");
            }
            catch (InvalidDataException) { }
        }
        if (OperatingSystem.IsWindows())
        {
            var id = "fixture-" + Guid.NewGuid().ToString("N");
            try
            {
                DeviceKeyStore.Save(id, key); Check(DeviceKeyStore.Read(id) == key);
                using var client = new HttpClient(new Reply(key, id));
                var result = await NetworkSignalSensor.ReadAsync(client, new() { DeviceId = id, SignedDevice = true, Url = "http://192.0.2.1:8766/v1/sample" }, CancellationToken.None);
                Check(result.Lux == 42);
                using var tampered = new HttpClient(new Reply(key, id, true));
                try { await NetworkSignalSensor.ReadAsync(tampered, new() { DeviceId = id, SignedDevice = true, Url = "http://192.0.2.1:8766/v1/sample" }, CancellationToken.None); throw new Exception("Forged response accepted"); } catch (InvalidDataException) { }
            }
            finally { DeviceKeyStore.Remove(id); }
        }
        Console.WriteLine("PASS: signal identity/units, deep links, signed responses and protected device binding.");
    }
    private sealed class PublicReply : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.Method == HttpMethod.Get && request.Headers.Contains("X-FC-Signature") == false);
            var frame = JsonSerializer.Serialize(new { protocol = "fc-device/1", sourceId = "public-sensor", signal = "illuminance", unit = "lx", value = 71, valid = true, sequence = 1, uptimeMs = 200 });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(frame) });
        }
    }
    private sealed class Reply(string key, string id, bool tamper = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var nonce = request.Headers.GetValues("X-FC-Nonce").Single();
            Check(DeviceSignals.Verify(key, "READ\n" + id + "\n" + nonce, request.Headers.GetValues("X-FC-Signature").Single()));
            var body = JsonSerializer.Serialize(new { protocol = "fc-device/1", sourceId = id, signal = "illuminance", unit = "lx", value = 42, valid = true, sequence = 1, uptimeMs = 20 });
            var reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            reply.Headers.Add("X-FC-Signature", DeviceSignals.Mac(key, (tamper ? "wrong" : nonce) + "\n" + body)); return Task.FromResult(reply);
        }
    }
}
