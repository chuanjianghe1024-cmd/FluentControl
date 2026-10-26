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
            try
            {
                if (Mutex.TryOpenExisting(@"Local\FluentControl.FcsGateway", out var running))
                { running.Dispose(); return true; }
            }
            catch (UnauthorizedAccessException) { return false; }
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
        string? deviceId = null, string port = "AUTO", string? command = null, object? arguments = null, bool localOnly = false)
    {
        if (operation is not ("status" or "list" or "discover" or "capabilities" or
            "usb.discover" or "usb.manage" or "usb.read" or "sample.read" or "node.request"))
            throw new ArgumentOutOfRangeException(nameof(operation));
        if (!localOnly && FcsRemoteGateway.Enabled && operation is "status" or "list" or "discover" or "node.request")
            return await FcsRemoteGateway.QueryAsync(operation, deviceId, command, arguments, cancellation);
        if (!EnsureStarted()) throw new IOException("FCS Gateway is not installed.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(operation == "usb.discover" ? 34 :
            operation == "node.request" ? 40 : operation == "usb.manage" ? 22 : operation == "sample.read" ? 24 :
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

    public static async Task<JsonElement> NodeAsync(string deviceId, string operation, object? arguments = null,
        CancellationToken cancellation = default, string port = "AUTO")
    {
        using var reply = await QueryAsync("node.request", cancellation, deviceId, port, operation, arguments ?? new { });
        var frame = reply.RootElement.GetProperty("result");
        if (frame.GetProperty("protocol").GetString() != "fcs-node/2" || frame.GetProperty("deviceId").GetString() != deviceId)
            throw new InvalidDataException("Node identity changed.");
        if (!frame.GetProperty("ok").GetBoolean()) throw new IOException("Node operation: " + frame.GetProperty("error").GetProperty("code").GetString());
        return frame.Clone();
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
        return await ReadDeviceAsync(source.DeviceId, port, source.SensorId, source.SignalId, cancellation);
    }
    public static Task<LightSample> ReadLanAutoAsync(LightSourceSubscription source, CancellationToken cancellation)
    {
        if (!NetworkSignalSensor.IsPublicLightEndpoint(source.Url) || string.IsNullOrWhiteSpace(source.DeviceId))
            throw new InvalidDataException("Expected a public FCS source.");
        return ReadDeviceAsync(source.DeviceId, "AUTO", source.SensorId, source.SignalId, cancellation);
    }
    private static async Task<LightSample> ReadDeviceAsync(string id, string port, string sensorId, string signalId, CancellationToken cancellation)
    {
        if (signalId != "illuminance") throw new InvalidDataException("Only illuminance drives brightness.");
        var requestedAt = DateTimeOffset.UtcNow;
        var remote = FcsRemoteGateway.Enabled;
        using var reply = await QueryAsync("node.request", cancellation, id, port, "data.read", new { sensorId, signalId, mode = "fresh" });
        var frame = reply.RootElement.GetProperty("result");
        if (frame.GetProperty("protocol").GetString() != "fcs-node/2" || frame.GetProperty("deviceId").GetString() != id ||
            !frame.GetProperty("ok").GetBoolean()) throw new InvalidDataException("Invalid node reply.");
        var timestamp = DateTimeOffset.UtcNow;
        if (remote || reply.RootElement.TryGetProperty("sampleAgeMilliseconds", out _))
        {
            var age = reply.RootElement.GetProperty("sampleAgeMilliseconds").GetInt32();
            var maxAge = reply.RootElement.GetProperty("maxAgeMilliseconds").GetInt32();
            if (age < 0 || maxAge <= 0 || age > Math.Min(maxAge, 10000)) throw new InvalidDataException("Stale Gateway sample.");
            timestamp = requestedAt.AddMilliseconds(-age);
            if (DateTimeOffset.UtcNow - timestamp > TimeSpan.FromSeconds(10)) throw new InvalidDataException("Delayed Gateway sample.");
        }
        var matches = frame.GetProperty("data").EnumerateArray().Where(item =>
            item.GetProperty("sensorId").GetString() == sensorId && item.GetProperty("signalId").GetString() == signalId).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Ambiguous signal.");
        var value = matches[0];
        if (!value.GetProperty("valid").GetBoolean() || !value.GetProperty("fresh").GetBoolean() ||
            value.GetProperty("unit").GetString() != "lx" || value.GetProperty("valueType").GetString() != "number")
            throw new InvalidDataException("Signal is not a fresh light sample.");
        var lux = value.GetProperty("value").GetDouble();
        if (!double.IsFinite(lux) || lux < 0 || lux > 200000) throw new InvalidDataException("Invalid lux.");
        return new LightSample { Format = "fluentcontrol.light-sample", Version = 1, SourceId = id,
            Timestamp = timestamp, Lux = lux, Valid = true };
    }
}
