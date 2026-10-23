using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

const string PipeName = "FluentControl.FcsGateway.v1";
const string Version = "fcs-gateway/1";
if (args.Length == 1 && args[0] == "--self-test")
{
    GatewayChecks.Run();
    return;
}
if (args.Length == 1 && args[0] == "--stop")
{
    // Explicit same-user shutdown for upgrades and standalone deployments.
    try
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await pipe.ConnectAsync(1500, timeout.Token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync("{\"operation\":\"shutdown\"}");
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) { }
    return;
}
if (args.Length > 1 || args.Length == 1 && args[0] != "--serve")
{
    Console.Error.WriteLine("Usage: FluentControl.FcsGateway.exe [--serve|--self-test|--stop]");
    return;
}
// One gateway per logged-in Windows user, regardless of whether FC is open.
using var instance = new Mutex(true, @"Local\FluentControl.FcsGateway", out bool first);
if (!first) return;
using var lifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
using var service = new GatewayRegistry();
using var usb = new FcsUsb();
_ = Task.Run(() => service.RunDiscoveryLoopAsync(lifetime.Token));
while (!lifetime.IsCancellationRequested)
{
    try
    {
        using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(lifetime.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        var line = await reader.ReadLineAsync(timeout.Token);
        if (line is null || line.Length > 4096) continue;
        var response = await FcsGatewayCommands.RunAsync(line, service, usb, timeout.Token);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response.Payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        if (response.Shutdown) lifetime.Cancel();
    }
    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or SocketException)
    { /* Cancelled clients and temporary network failure do not stop the gateway. */ }
}

internal sealed record GatewayDevice(string DeviceId, string Ip, string Status,
    double? Lux, DateTimeOffset? LastSampleUtc, DateTimeOffset LastSeenUtc,
    string? UsbPort = null, string? Transport = null);

internal sealed class GatewayRegistry : IDisposable
{
    private sealed class Entry
    {
        public string Id = "", Ip = "", UsbPort = "", Transport = "";
        public DateTimeOffset Seen, UsbSeen, SampleAt;
        public double? Lux;
    }
    private readonly ConcurrentDictionary<string, Entry> devices = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim discovery = new(1, 1);
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    { Timeout = TimeSpan.FromSeconds(2) };
    public static bool IsLocalAddress(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var a = ip.GetAddressBytes();
        return a[0] == 10 || a[0] == 172 && a[1] >= 16 && a[1] <= 31 ||
               a[0] == 192 && a[1] == 168;
    }
    public static bool ValidId(string? id) => id is { Length: > 0 and <= 100 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static bool TryLight(JsonElement frame, string identity, out double lux)
    {
        lux = 0;
        try
        {
            if (frame.GetProperty("protocol").GetString() != "fc-device/1" ||
                frame.GetProperty("sourceId").GetString() != identity ||
                frame.GetProperty("signal").GetString() != "illuminance" ||
                frame.GetProperty("unit").GetString() != "lx" ||
                !frame.GetProperty("valid").GetBoolean()) return false;
            lux = frame.GetProperty("value").GetDouble();
            return double.IsFinite(lux) && lux >= 0 && lux <= 200000;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return false; }
    }
    public void ReportUsb(IEnumerable<UsbDevice> discovered)
    {
        foreach (var device in discovered)
        {
            var entry = devices.GetOrAdd(device.Identity, id => new Entry { Id = id });
            entry.UsbPort = device.Port;
            entry.UsbSeen = DateTimeOffset.UtcNow;
        }
    }
    public void ReportUsbSample(UsbReading sample, string port)
    {
        var entry = devices.GetOrAdd(sample.DeviceId, id => new Entry { Id = id });
        entry.UsbPort = port;
        entry.UsbSeen = sample.Timestamp;
        entry.Lux = sample.Lux;
        entry.SampleAt = sample.Timestamp;
        entry.Transport = "usb";
    }
    public bool HasLan(string id) => devices.TryGetValue(id, out var e) &&
        !string.IsNullOrEmpty(e.Ip) && DateTimeOffset.UtcNow - e.Seen < TimeSpan.FromMinutes(10);
    public bool HasRecentUsb(string id) => devices.TryGetValue(id, out var e) &&
        !string.IsNullOrEmpty(e.UsbPort) && DateTimeOffset.UtcNow - e.UsbSeen < TimeSpan.FromSeconds(15);
    public async Task<UsbReading> ReadPublicAsync(string id, CancellationToken cancel)
    {
        if (!devices.TryGetValue(id, out var e) || string.IsNullOrEmpty(e.Ip) ||
            DateTimeOffset.UtcNow - e.Seen > TimeSpan.FromMinutes(10))
            throw new IOException("No current LAN endpoint for device.");
        // This endpoint is fixed; callers cannot supply arbitrary URLs or network destinations.
        using var response = await client.GetAsync("http://" + e.Ip + ":8766/v1/sample",
            HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 4096) throw new InvalidDataException("LAN frame too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int size;
        while ((size = await stream.ReadAsync(chunk, cancel)) > 0)
        {
            if (buffer.Length + size > 4096) throw new InvalidDataException("LAN frame too large.");
            buffer.Write(chunk, 0, size);
        }
        using var doc = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
        if (!TryLight(doc.RootElement, id, out var lux)) throw new InvalidDataException("Invalid LAN sample.");
        var stamp = DateTimeOffset.UtcNow;
        e.Lux = lux; e.SampleAt = stamp; e.Transport = "lan";
        return new UsbReading(id, lux, stamp);
    }
    public GatewayDevice[] Snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return devices.Values.Select(e => new GatewayDevice(e.Id, e.Ip,
            e.SampleAt == default || now - e.SampleAt > TimeSpan.FromSeconds(12) ? "offline" : "online",
            e.SampleAt == default || now - e.SampleAt > TimeSpan.FromSeconds(12) ? null : e.Lux,
            e.SampleAt == default ? null : e.SampleAt,
            e.Seen >= e.UsbSeen ? e.Seen : e.UsbSeen,
            now - e.UsbSeen < TimeSpan.FromSeconds(15) ? e.UsbPort : null,
            now - e.SampleAt < TimeSpan.FromSeconds(12) ? e.Transport : null))
            .OrderBy(e => e.DeviceId, StringComparer.Ordinal).ToArray();
    }
    public async Task RunDiscoveryLoopAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try { await DiscoverAsync(cancel); }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException) { }
            try { await Task.Delay(TimeSpan.FromSeconds(5), cancel); }
            catch (OperationCanceledException) { break; }
        }
    }
    public async Task DiscoverAsync(CancellationToken cancel)
    {
        if (!await discovery.WaitAsync(0, cancel)) return;
        try
        {
            using var udp = new UdpClient(0) { EnableBroadcast = true };
            var request = Encoding.ASCII.GetBytes("FC-DISCOVER/1");
            await udp.SendAsync(request, new IPEndPoint(IPAddress.Broadcast, 42735), cancel);
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timer.CancelAfter(TimeSpan.FromMilliseconds(1400));
            try
            {
                while (!timer.IsCancellationRequested && devices.Count < 64)
                {
                    var response = await udp.ReceiveAsync(timer.Token);
                    if (!IsLocalAddress(response.RemoteEndPoint.Address) || response.Buffer.Length > 1024) continue;
                    try
                    {
                        using var json = JsonDocument.Parse(response.Buffer, new JsonDocumentOptions { MaxDepth = 6 });
                        var value = json.RootElement;
                        if (value.GetProperty("protocol").GetString() != "fc-device/1") continue;
                        var id = value.GetProperty("sourceId").GetString();
                        if (!ValidId(id)) continue;
                        var entry = devices.GetOrAdd(id!, _ => new Entry { Id = id! });
                        entry.Ip = response.RemoteEndPoint.Address.ToString();
                        entry.Seen = DateTimeOffset.UtcNow;
                    }
                    catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
                }
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { }
            // Refresh only validated discovered addresses; IP cannot be supplied by caller over IPC.
            foreach (var e in devices.Values.ToArray())
            {
                cancel.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow - e.Seen > TimeSpan.FromMinutes(10)) continue;
                try
                {
                    using var response = await client.GetAsync("http://" + e.Ip + ":8766/v1/sample",
                        HttpCompletionOption.ResponseHeadersRead, cancel);
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 4096) continue;
                    var raw = await response.Content.ReadAsByteArrayAsync(cancel);
                    if (raw.Length > 4096) continue;
                    using var frame = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 8 });
                    if (!TryLight(frame.RootElement, e.Id, out var lux)) continue;
                    e.Lux = lux; e.SampleAt = DateTimeOffset.UtcNow; e.Transport = "lan";
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException) { }
            }
        }
        finally { discovery.Release(); }
    }
    public void Dispose() { client.Dispose(); discovery.Dispose(); }
}

internal static class GatewayChecks
{
    public static void Run()
    {
        if (!GatewayRegistry.ValidId("c3-bh1750-1234") || GatewayRegistry.ValidId("../wrong"))
            throw new InvalidOperationException("Device identity validation failed");
        if (!GatewayRegistry.IsLocalAddress(IPAddress.Parse("192.168.1.7")) ||
            GatewayRegistry.IsLocalAddress(IPAddress.Parse("8.8.8.8")))
            throw new InvalidOperationException("Network scope validation failed");
        using var ok = JsonDocument.Parse("{\"protocol\":\"fc-device/1\",\"sourceId\":\"x\",\"signal\":\"illuminance\",\"unit\":\"lx\",\"valid\":true,\"value\":123.5}");
        using var bad = JsonDocument.Parse("{\"protocol\":\"fc-device/1\",\"sourceId\":\"other\",\"signal\":\"illuminance\",\"unit\":\"lx\",\"valid\":true,\"value\":123.5}");
        if (!GatewayRegistry.TryLight(ok.RootElement, "x", out var lux) || lux != 123.5 ||
            GatewayRegistry.TryLight(bad.RootElement, "x", out _))
            throw new InvalidOperationException("Light frame validation failed");
        var sample = new GatewayDevice("x", "192.168.1.7", "online", 123.5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var wire = JsonSerializer.Serialize(new { protocol = "fcs-gateway/1", devices = new[] { sample } },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var parsed = JsonDocument.Parse(wire);
        if (parsed.RootElement.GetProperty("devices")[0].GetProperty("deviceId").GetString() != "x" ||
            parsed.RootElement.GetProperty("devices")[0].GetProperty("lux").GetDouble() != 123.5)
            throw new InvalidOperationException("IPC device field casing changed");
        if (!FcsUsb.ValidPort("AUTO") || !FcsUsb.ValidPort("com12") ||
            FcsUsb.ValidPort("../COM1") || FcsUsb.ValidPort("COM0"))
            throw new InvalidOperationException("USB port validation failed");
        using var registry = new GatewayRegistry();
        using var serial = new FcsUsb();
        var capabilities = FcsGatewayCommands.RunAsync("{\"operation\":\"capabilities\",\"deviceId\":null,\"port\":\"AUTO\",\"command\":null,\"arguments\":null}",
            registry, serial, CancellationToken.None).GetAwaiter().GetResult();
        var cap = JsonSerializer.SerializeToElement(capabilities.Payload);
        if (cap.GetProperty("pluginRuntime").GetString() != "not-implemented" ||
            cap.GetProperty("deviceCodeInstall").GetString() != "usb-only")
            throw new InvalidOperationException("Gateway plugin capability boundary invalid.");
        var unsafePort = FcsGatewayCommands.RunAsync("{\"operation\":\"usb.discover\",\"port\":\"../COM1\"}",
            registry, serial, CancellationToken.None).GetAwaiter().GetResult();
        var rejected = JsonSerializer.SerializeToElement(unsafePort.Payload);
        if (!rejected.TryGetProperty("error", out _))
            throw new InvalidOperationException("Unsafe serial port selection not rejected.");
        Console.WriteLine("PASS: FCS gateway LAN/USB identity, IPC serialization and plugin management boundaries.");
    }
}
