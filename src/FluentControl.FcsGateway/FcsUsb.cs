using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

internal sealed record UsbDevice(string Port, string Identity);
internal sealed record UsbReading(string DeviceId, double Lux, DateTimeOffset Timestamp);

// The Gateway is the only FC-owned serial protocol host. Each command verifies a
// fresh physical USB handshake and never trusts a caller-provided identity alone.
internal sealed class FcsUsb : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, string> known = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "status", "wifi.scan", "wifi.configure", "wifi.cancel", "wifi.forget",
        "wireless.enable", "wireless.disable", "binding.rotate", "reboot",
        "factory.reset", "ap.start", "ap.stop", "ble.enable", "ble.disable"
    };
    public static bool ValidPort(string? port) => port is not null &&
        Regex.IsMatch(port, @"^(AUTO|COM[1-9][0-9]{0,4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static bool Candidate(string id)
    {
        var p = id.Split('&');
        return p.Length >= 2 &&
            (p[0].Equals("VID_2E8A", StringComparison.OrdinalIgnoreCase) ||
             p[0].Equals("VID_303A", StringComparison.OrdinalIgnoreCase) && p[1].Equals("PID_1001", StringComparison.OrdinalIgnoreCase) ||
             p[0].Equals("VID_1209", StringComparison.OrdinalIgnoreCase) && p[1].Equals("PID_C550", StringComparison.OrdinalIgnoreCase));
    }
    public static string[] Ports(string selection)
    {
        if (!ValidPort(selection)) throw new InvalidDataException("Invalid USB port selection.");
        if (!selection.Equals("AUTO", StringComparison.OrdinalIgnoreCase)) return new[] { selection.ToUpperInvariant() };
        var identified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usb is null) return Array.Empty<string>();
        foreach (var device in usb.GetSubKeyNames().Where(Candidate))
        {
            using var key = usb.OpenSubKey(device);
            if (key is null) continue;
            foreach (var instance in key.GetSubKeyNames())
            {
                using var parameters = usb.OpenSubKey(device + @"\" + instance + @"\Device Parameters");
                if (parameters?.GetValue("PortName") is string port && ValidPort(port)) identified.Add(port);
            }
        }
        return SerialPort.GetPortNames().Where(identified.Contains).Order(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    }
    private static SerialPort Open(string name)
    {
        var port = new SerialPort(name, 115200) { ReadTimeout = 100, WriteTimeout = 500,
            DtrEnable = true, RtsEnable = false, Encoding = Encoding.UTF8, NewLine = "\n" };
        try { port.Open(); port.DiscardInBuffer(); return port; }
        catch { port.Dispose(); throw; }
    }
    private static JsonElement Exchange(SerialPort port, string command, CancellationToken ct, string? payload = null)
    {
        ct.ThrowIfCancellationRequested();
        var token = Guid.NewGuid().ToString("N");
        port.Write(command + " " + token + (payload is null ? "" : " " + payload) + "\n");
        var timer = Stopwatch.StartNew();
        var buffer = new List<byte>();
        while (timer.Elapsed < TimeSpan.FromSeconds(command == "FC" ? 12 : 2.5))
        {
            ct.ThrowIfCancellationRequested();
            int c;
            try { c = port.ReadByte(); } catch (TimeoutException) { continue; }
            if (c < 0) throw new IOException("USB device disconnected.");
            if (c != '\n')
            {
                if (buffer.Count >= 8192) throw new InvalidDataException("USB response too large.");
                buffer.Add((byte)c); continue;
            }
            try
            {
                using var doc = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
                var frame = doc.RootElement;
                if (frame.TryGetProperty("requestId", out var received) && received.GetString() == token)
                    return frame.Clone();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
            buffer.Clear();
        }
        throw new TimeoutException("USB device did not respond. Close Thonny and check the cable.");
    }
    private static string Identity(JsonElement frame)
    {
        var id = frame.GetProperty("sourceId").GetString();
        if (!GatewayRegistry.ValidId(id)) throw new InvalidDataException("Invalid USB device identity.");
        return id!;
    }
    private static string Handshake(JsonElement frame)
    {
        if (frame.GetProperty("protocol").GetString() != "fc-light-usb/1") throw new InvalidDataException("Unsupported USB firmware.");
        return Identity(frame);
    }
    public async Task<UsbDevice[]> DiscoverAsync(string selection, CancellationToken cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            return await Task.Run(() =>
            {
                var result = new List<UsbDevice>();
                foreach (var candidate in Ports(selection))
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        using var port = Open(candidate);
                        var id = Handshake(Exchange(port, "PING", cancel));
                        known[id] = candidate;
                        result.Add(new UsbDevice(candidate, id));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException) { }
                }
                return result.ToArray();
            }, cancel);
        }
        finally { gate.Release(); }
    }
    public async Task<JsonElement> ManageAsync(string selection, string deviceId, string op, JsonElement arguments, CancellationToken cancel)
    {
        if (!Allowed.Contains(op) || !GatewayRegistry.ValidId(deviceId) || !ValidPort(selection))
            throw new InvalidDataException("Unsupported USB management request.");
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined))
            throw new InvalidDataException("Expected object arguments.");
        // Never echo or log wireless credentials.
        var payload = JsonSerializer.Serialize(new { operation = op, arguments = arguments.ValueKind == JsonValueKind.Object ? arguments : JsonSerializer.SerializeToElement(new { }) });
        if (Encoding.UTF8.GetByteCount(payload) > 2048) throw new InvalidDataException("USB management payload too large.");
        await gate.WaitAsync(cancel);
        try
        {
            return await Task.Run(() =>
            {
                foreach (var candidate in ResolvePorts(selection, deviceId))
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        using var port = Open(candidate);
                        var hello = Exchange(port, "PING", cancel);
                        if (Handshake(hello) != deviceId || hello.GetProperty("management").GetString() != "fc-device/1") continue;
                        known[deviceId] = candidate;
                        var result = Exchange(port, "FC", cancel, payload);
                        if (Identity(result) != deviceId || !result.GetProperty("ok").GetBoolean())
                            throw new InvalidDataException("Device rejected USB command.");
                        return result;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or JsonException or InvalidOperationException or KeyNotFoundException) { }
                }
                throw new IOException("USB management device unavailable.");
            }, cancel);
        }
        finally { gate.Release(); }
    }
    public async Task<UsbReading> ReadAsync(string selection, string deviceId, CancellationToken cancel)
    {
        if (!ValidPort(selection) || !GatewayRegistry.ValidId(deviceId)) throw new InvalidDataException("Invalid USB signal request.");
        await gate.WaitAsync(cancel);
        try
        {
            return await Task.Run(() =>
            {
                foreach (var candidate in ResolvePorts(selection, deviceId))
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        using var port = Open(candidate);
                        if (Handshake(Exchange(port, "PING", cancel)) != deviceId) continue;
                        var frame = Exchange(port, "READ", cancel);
                        if (Identity(frame) != deviceId || !frame.GetProperty("valid").GetBoolean() ||
                            !frame.GetProperty("lux").TryGetDouble(out var lux) || !double.IsFinite(lux) || lux < 0 || lux > 200000)
                            throw new InvalidDataException("Invalid USB light sample.");
                        known[deviceId] = candidate;
                        return new UsbReading(deviceId, lux, DateTimeOffset.UtcNow);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or JsonException or InvalidOperationException or KeyNotFoundException) { }
                }
                throw new IOException("USB sensor unavailable.");
            }, cancel);
        }
        finally { gate.Release(); }
    }
    private IEnumerable<string> ResolvePorts(string selection, string deviceId)
    {
        var choices = Ports(selection);
        return selection.Equals("AUTO", StringComparison.OrdinalIgnoreCase) && known.TryGetValue(deviceId, out var remembered)
            ? choices.OrderByDescending(p => p.Equals(remembered, StringComparison.OrdinalIgnoreCase))
            : choices;
    }
    public void Dispose() => gate.Dispose();
}
