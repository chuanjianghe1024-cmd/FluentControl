using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FluentControl.Services;

public sealed record FcsDeviceStatus(string DeviceId, string Ip, string Status, double? Lux,
    DateTimeOffset? LastSampleUtc, string? UsbPort = null, string? Transport = null);

public static class FcsGatewayClient
{
    private const string PipeName = "FluentControl.FcsGateway.v1";
    private const string Protocol = "fcs-gateway/1";
    private static readonly object StartLock = new();
    private static DateTimeOffset lastStart;

    public static bool EnsureStarted()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var executable = Path.Combine(AppContext.BaseDirectory, "FluentControl.FcsGateway.exe");
        if (!File.Exists(executable)) return false;
        lock (StartLock)
        {
            if (DateTimeOffset.UtcNow - lastStart < TimeSpan.FromSeconds(10)) return true;
            lastStart = DateTimeOffset.UtcNow;
            try
            {
                Process.Start(new ProcessStartInfo(executable, "--serve")
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory });
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { return false; }
        }
    }

    // Only typed Gateway commands are exposed. USB configuration travels only
    // over the current-user pipe; the Gateway never opens a LAN write endpoint.
    public static async Task<JsonDocument> QueryAsync(string operation, CancellationToken cancellation = default,
        string? deviceId = null, string port = "AUTO", string? command = null, object? arguments = null)
    {
        if (operation is not ("status" or "list" or "discover" or "capabilities" or
            "usb.discover" or "usb.manage" or "usb.read" or "sample.read"))
            throw new ArgumentOutOfRangeException(nameof(operation));
        if (!EnsureStarted()) throw new IOException("FCS Gateway is not installed.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(operation == "usb.discover" ? 34 :
            operation == "usb.manage" ? 22 : operation == "sample.read" ? 24 :
            operation == "discover" ? 7 : 4));
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(2000, timeout.Token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        var request = JsonSerializer.Serialize(new { operation, deviceId, port, command, arguments });
        if (Encoding.UTF8.GetByteCount(request) > 3072) throw new InvalidDataException("Gateway request too large.");
        await writer.WriteLineAsync(request);
        var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Gateway disconnected.");
        if (line.Length > 64 * 1024) throw new InvalidDataException("Gateway reply too large.");
        var json = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 12 });
        try
        {
            if (json.RootElement.GetProperty("protocol").GetString() != Protocol)
                throw new InvalidDataException("Unexpected Gateway protocol.");
            if (json.RootElement.TryGetProperty("error", out var error))
                throw new IOException("Gateway operation failed: " + error.GetString());
            return json;
        }
        catch { json.Dispose(); throw; }
    }

    public static async Task<IReadOnlyList<FcsDeviceStatus>> ListAsync(bool refresh, CancellationToken cancellation = default)
    {
        using var result = await QueryAsync(refresh ? "discover" : "list", cancellation);
        var found = new List<FcsDeviceStatus>();
        foreach (var d in result.RootElement.GetProperty("devices").EnumerateArray())
        {
            var id = d.GetProperty("deviceId").GetString();
            var ip = d.GetProperty("ip").GetString() ?? "";
            var state = d.GetProperty("status").GetString();
            if (string.IsNullOrWhiteSpace(id) || state is not ("online" or "offline")) continue;
            var lx = d.GetProperty("lux");
            DateTimeOffset? at = null;
            if (d.TryGetProperty("lastSampleUtc", out var timestamp) &&
                timestamp.ValueKind == JsonValueKind.String && timestamp.TryGetDateTimeOffset(out var parsed)) at = parsed;
            var port = d.TryGetProperty("usbPort", out var usb) && usb.ValueKind == JsonValueKind.String ? usb.GetString() : null;
            var transport = d.TryGetProperty("transport", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            found.Add(new(id, ip, state!, lx.ValueKind == JsonValueKind.Number ? lx.GetDouble() : null, at, port, transport));
        }
        return found;
    }

    public static async Task<IReadOnlyList<LocalLightDevice>> DiscoverUsbAsync(string selection, CancellationToken cancellation)
    {
        using var json = await QueryAsync("usb.discover", cancellation, port: selection.Trim().ToUpperInvariant());
        return json.RootElement.GetProperty("usbDevices").EnumerateArray()
            .Select(d => new LocalLightDevice(d.GetProperty("port").GetString()!,
                d.GetProperty("identity").GetString()!)).ToArray();
    }

    public static async Task<JsonElement> ManageUsbAsync(LocalLightDevice device, string operation, object? arguments,
        CancellationToken cancellation)
    {
        using var json = await QueryAsync("usb.manage", cancellation,
            deviceId: device.Identity, port: device.Port, command: operation, arguments: arguments);
        return json.RootElement.GetProperty("result").Clone();
    }

    // Reads by immutable device ID; prefers current USB then verified public LAN
    // endpoint from the Gateway registry. This enables charger-powered C3 fallback
    // without rewriting the subscription identity or fabricating sample timestamps.
    public static async Task<LightSample> ReadAutoAsync(LightSourceSubscription source, CancellationToken cancellation)
    {
        if (!LocalLightProtocol.IsLocal(source.Url) || string.IsNullOrWhiteSpace(source.DeviceId))
            throw new InvalidDataException("Expected a bound USB sensor identity.");
        var port = LocalLightProtocol.PortFor(source.Url);
        using var json = await QueryAsync("sample.read", cancellation, deviceId: source.DeviceId, port: port);
        var root = json.RootElement.GetProperty("sample");
        if (root.GetProperty("deviceId").GetString() != source.DeviceId)
            throw new InvalidDataException("Device identity changed.");
        var value = root.GetProperty("lux").GetDouble();
        if (!double.IsFinite(value) || value < 0 || value > 200000)
            throw new InvalidDataException("Invalid Gateway light sample.");
        if (!root.GetProperty("timestamp").TryGetDateTimeOffset(out var timestamp) ||
            timestamp < DateTimeOffset.UtcNow.AddMinutes(-1) ||
            timestamp > DateTimeOffset.UtcNow.AddSeconds(10))
            throw new InvalidDataException("Stale Gateway light sample.");
        return new LightSample { Format = "fluentcontrol.light-sample", Version = 1,
            SourceId = source.DeviceId, Timestamp = timestamp, Lux = value, Valid = true };
    }
}
