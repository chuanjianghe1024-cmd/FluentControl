using System.Diagnostics;
using System.IO.Ports;
using System.Text.Json;
using Microsoft.Win32;

namespace FluentControl.Services;

public sealed record LocalLightDevice(string Port, string Identity);

public static class LocalLightSensor
{
    private static readonly SemaphoreSlim SerialGate = new(1, 1);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> KnownPorts = new();
    // Enumeration alone sends nothing. Automatic probes are limited to RP2040 USB VID.
    // Other boards require an explicitly selected port and compatible firmware.
    public static string[] CandidatePorts(string selection)
    {
        if (selection != "AUTO") return new[] { selection };
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return Array.Empty<string>();
        using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usb is null) return Array.Empty<string>();
        foreach (var device in usb.GetSubKeyNames().Where(x => (x.StartsWith("VID_2E8A&", StringComparison.OrdinalIgnoreCase) || x.StartsWith("VID_303A&PID_1001", StringComparison.OrdinalIgnoreCase))))
        {
            using var key = usb.OpenSubKey(device);
            if (key is null) continue;
            foreach (var instance in key.GetSubKeyNames())
            {
                using var parameters = key.OpenSubKey(instance + @"\Device Parameters");
                if (parameters?.GetValue("PortName") is string port) found.Add(port);
            }
        }
        return SerialPort.GetPortNames().Where(found.Contains).Order(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    }
    private static SerialPort Open(string port)
    {
        var serial = new SerialPort(port, 115200) { ReadTimeout = 100, WriteTimeout = 500,
            DtrEnable = true, RtsEnable = false, Encoding = System.Text.Encoding.UTF8, NewLine = "\n" };
        try { serial.Open(); serial.DiscardInBuffer(); return serial; }
        catch { serial.Dispose(); throw; }
    }
    private static JsonElement Exchange(SerialPort port, string command, CancellationToken cancellation, string? payload = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var token = Guid.NewGuid().ToString("N");
        port.Write(command + " " + token + (payload is null ? "" : " " + payload) + "\n");
        var timer = Stopwatch.StartNew();
        var buffer = new List<byte>();
        while (timer.Elapsed < TimeSpan.FromSeconds(command == "FC" ? 12 : 2.5))
        {
            cancellation.ThrowIfCancellationRequested();
            int value;
            try { value = port.ReadByte(); } catch (TimeoutException) { continue; }
            if (value < 0) throw new IOException("Serial port closed.");
            if (value != '\n')
            {
                if (buffer.Count >= LightFeed.MaximumBytes) throw new InvalidDataException("Serial reply too large.");
                buffer.Add((byte)value); continue;
            }
            var bytes = buffer.ToArray(); buffer.Clear();
            try
            {
                using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
                var frame = document.RootElement;
                if (frame.ValueKind == JsonValueKind.Object && frame.TryGetProperty("requestId", out var id) &&
                    id.ValueKind == JsonValueKind.String && id.GetString() == token) return frame.Clone();
            }
            catch (JsonException) { /* MicroPython boot banners are not responses. */ }
        }
        throw new TimeoutException("Sensor did not answer; close Thonny and install compatible firmware.");
    }
    private sealed class Lease : IDisposable { public void Dispose() => SerialGate.Release(); }
    private static IDisposable Acquire(CancellationToken cancellation) { SerialGate.Wait(cancellation); return new Lease(); }
    public static Task<JsonElement> ManageAsync(LocalLightDevice device, string operation, object? arguments, CancellationToken cancellation) => Task.Run(() =>
    {
        // Physical USB transport is authoritative. No binding token is needed or checked here.
        var allowed = new[] { "status", "wifi.scan", "wifi.configure", "wifi.cancel", "wifi.forget", "wireless.disable", "wireless.enable", "binding.rotate", "reboot", "factory.reset" };
        if (!allowed.Contains(operation)) throw new InvalidDataException("Unsupported device operation.");
        using var lease = Acquire(cancellation);
        using var port = Open(device.Port);
        var hello = Exchange(port, "PING", cancellation);
        if (LocalLightProtocol.Handshake(hello) != device.Identity || !hello.TryGetProperty("management", out var version) || version.GetString() != DeviceSignals.Protocol)
            throw new InvalidDataException("Compatible device management firmware required.");
        var payload = JsonSerializer.Serialize(new { operation, arguments });
        if (System.Text.Encoding.UTF8.GetByteCount(payload) > 2048) throw new InvalidDataException();
        var reply = Exchange(port, "FC", cancellation, payload);
        if (LocalLightProtocol.Identity(reply) != device.Identity || !reply.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) throw new InvalidDataException("Device rejected operation.");
        return reply;
    }, cancellation);
    public static Task<IReadOnlyList<LocalLightDevice>> DiscoverAsync(string selection, CancellationToken cancellation) => Task.Run<IReadOnlyList<LocalLightDevice>>(() =>
    {
        var results = new List<LocalLightDevice>();
        foreach (var candidate in CandidatePorts(LocalLightProtocol.PortFor("fc-usb://" + selection)))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using var lease = Acquire(cancellation);
                using var port = Open(candidate);
                var identity = LocalLightProtocol.Handshake(Exchange(port, "PING", cancellation));
                KnownPorts[identity] = candidate;
                results.Add(new(candidate, identity));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException) { }
        }
        return results;
    }, cancellation);

    public static Task<LightSample> ReadAsync(LightSourceSubscription source, CancellationToken cancellation) => Task.Run(() =>
    {
        // Device identity is captured when adding the subscription, so re-enumeration cannot silently select another sensor.
        if (string.IsNullOrWhiteSpace(source.DeviceId)) throw new InvalidDataException("Discover and add the local sensor first.");
        foreach (var candidate in CandidatePorts(LocalLightProtocol.PortFor(source.Url)).OrderByDescending(p => KnownPorts.TryGetValue(source.DeviceId, out var known) && p == known))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using var lease = Acquire(cancellation);
                using var port = Open(candidate);
                var identity = LocalLightProtocol.Handshake(Exchange(port, "PING", cancellation));
                if (identity != source.DeviceId) continue;
                KnownPorts[identity] = candidate;
                return LocalLightProtocol.Sample(Exchange(port, "READ", cancellation), identity, DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException) { }
        }
        throw new IOException("Selected local light sensor is unavailable.");
    }, cancellation);
}
