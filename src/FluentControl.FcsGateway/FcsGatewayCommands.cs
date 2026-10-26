using System.Net.Http;
using System.Text.Json;

internal sealed record GatewayCommandResult(object Payload, bool Shutdown = false);

// A small typed IPC control plane. No arbitrary serial command, LAN URL or executable
// can be supplied by the caller. Only USB management may mutate device state.
internal static class FcsGatewayCommands
{
    private const string Protocol = "fcs-gateway/1";
    private static string Field(JsonElement root, string name, string fallback = "")
    {
        if (!root.TryGetProperty(name, out var p) ||
            p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return fallback;
        if (p.ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid " + name);
        var value = p.GetString() ?? fallback;
        if (value.Length > 120) throw new InvalidDataException("Field too long.");
        return value;
    }
    private static async Task<(UsbReading Sample, string Transport)> ReadSampleAsync(
        string deviceId, string port, GatewayRegistry registry, FcsUsb usb, CancellationToken cancel)
    {
        if (!GatewayRegistry.ValidId(deviceId)) throw new InvalidDataException("Invalid device identity.");
        // Prefer LAN once USB is no longer recently seen, avoiding stale COM probes.
        if (registry.HasLan(deviceId) && !registry.HasRecentUsb(deviceId))
        {
            try { return (await registry.ReadPublicAsync(deviceId, cancel), "lan"); }
            catch (Exception ex) when (ex is IOException or HttpRequestException or
                InvalidDataException or TaskCanceledException) { }
        }
        try
        {
            var sample = await usb.ReadAsync(port, deviceId, cancel);
            registry.ReportUsbSample(sample, sample.Port ?? port);
            return (sample, "usb");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or
            UnauthorizedAccessException or InvalidDataException)
        {
            return (await registry.ReadPublicAsync(deviceId, cancel), "lan");
        }
    }

    private static async Task<FcsDeviceCapabilities> CapabilitiesAsync(
        string deviceId, string port, GatewayRegistry registry, FcsUsb usb, CancellationToken cancel)
    {
        if (!GatewayRegistry.ValidId(deviceId)) throw new InvalidDataException("Invalid device identity.");
        try { return await registry.ReadCapabilitiesAsync(deviceId, cancel); }
        catch (Exception ex) when (ex is IOException or HttpRequestException or
            InvalidDataException or TaskCanceledException)
        {
            return await usb.CapabilitiesAsync(port, deviceId, cancel);
        }
    }

    private static object SafeStatus(JsonElement frame, string expected)
    {
        string? Str(string key) => frame.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        bool? Flag(string key) => frame.TryGetProperty(key, out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
        if (Str("sourceId") != expected) throw new InvalidDataException("Unexpected USB identity.");
        // Never disclose bindingKey, setupPassword, networks, credentials or raw
        // management payloads through generic/AI read-only endpoints.
        return new { deviceId = expected, state = Str("state"), ip = Str("ip"),
            apActive = Flag("apActive"), bleEnabled = Flag("bleEnabled"),
            bleSupported = Flag("bleSupported") };
    }
    public static async Task<GatewayCommandResult> RunAsync(string json, GatewayRegistry registry, FcsUsb usb, CancellationToken cancel)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid request.");
            var op = Field(root, "operation");
            var port = Field(root, "port", "AUTO");
            var identity = Field(root, "deviceId");
            var arguments = root.TryGetProperty("arguments", out var args) ? args : default;
            switch (op)
            {
                case "node.request":
                {
                    var command = Field(root, "command");
                    arguments = FcsNode.Arguments(command, arguments);
                    if (!GatewayRegistry.ValidId(identity)) throw new InvalidDataException("Invalid device identity.");
                    JsonElement frame;
                    string via;
                    if (FcsNode.Routes.ContainsKey(command) && registry.HasLan(identity) && !registry.HasRecentUsb(identity))
                    { frame = await registry.ReadNodeAsync(identity, command, arguments, cancel); via = "lan"; }
                    else
                    {
                        try
                        {
                            var reply = await usb.NodeAsync(port, identity, command, arguments, cancel);
                            frame = reply.Frame; via = "usb";
                            registry.ReportUsb(new[] { new UsbDevice(reply.Port, identity) });
                        }
                        catch (Exception ex) when (FcsNode.Routes.ContainsKey(command) && registry.HasLan(identity) &&
                            ex is IOException or TimeoutException or UnauthorizedAccessException)
                        { frame = await registry.ReadNodeAsync(identity, command, arguments, cancel); via = "lan"; }
                    }
                    return new(new { protocol = Protocol, result = frame, transport = via });
                }
                case "list":
                    return new(new { protocol = Protocol, devices = registry.Snapshot() });
                case "discover":
                    await registry.DiscoverAsync(cancel);
                    return new(new { protocol = Protocol, devices = registry.Snapshot() });
                case "status":
                    return new(new { protocol = Protocol, running = true, devices = registry.Snapshot().Length });
                case "capabilities":
                    return new(new { protocol = Protocol, deviceModel = "Device/Sensor/Signal/Endpoint",
                        transports = new[] { "usb", "lan" },
                        usbOperations = new[] { "discover", "read", "manage" },
                        lanOperations = new[] { "discover", "read.public" },
                        signalOperations = new[] { "device.capabilities", "signal.read" },
                        mcp = "stdio", cloudSync = "not-implemented",
                        pluginRuntime = "usb-micropython-v1", gatewayScriptRuntime = "fcs-python-subset/1", deviceCodeInstall = "usb-only",
                        wirelessRules = "planned" });
                case "cloud.capabilities":
                    return new(new { protocol = Protocol, supported = true, defaultEnabled = false,
                        mode = "explicit-https-script-targets", schema = "fcs-cloud-envelope/1",
                        message = "HTTPS export requires an explicitly enabled local script and configured target; no hosted cloud account service is provided." });
                case "device.capabilities":
                    return new(new { protocol = Protocol, device = await CapabilitiesAsync(identity, port, registry, usb, cancel) });
                case "device.status":
                {
                    var managed = await usb.ManageAsync(port, identity, "status", default, cancel);
                    registry.ReportUsb(new[] { new UsbDevice(managed.Port, identity) });
                    return new(new { protocol = Protocol, device = SafeStatus(managed.Frame, identity) });
                }
                case "signal.read":
                {
                    var sensor = Field(root, "sensorId");
                    var signal = Field(root, "signalId");
                    var model = await CapabilitiesAsync(identity, port, registry, usb, cancel);
                    if (!FcsSignalCatalog.Supports(model, sensor, signal))
                        return new(new { protocol = Protocol, error = "unknown_signal" });
                    // Existing firmware exposes only the illuminance READ frame.
                    // Other capabilities are discoverable but not pretended readable.
                    if (signal != "illuminance")
                        return new(new { protocol = Protocol, error = "signal_read_not_implemented" });
                    var (reading, via) = await ReadSampleAsync(identity, port, registry, usb, cancel);
                    return new(new { protocol = Protocol, deviceId = identity, sensorId = sensor,
                        signalId = signal, unit = "lx", value = reading.Lux,
                        timestamp = reading.Timestamp, transport = via });
                }
                case "usb.discover":
                {
                    var items = await usb.DiscoverAsync(port, cancel);
                    registry.ReportUsb(items);
                    return new(new { protocol = Protocol, usbDevices = items, devices = registry.Snapshot() });
                }
                case "usb.manage":
                {
                    var managed = await usb.ManageAsync(port, identity, Field(root, "command"), arguments, cancel);
                    // AUTO is a selection rule, never the physical COM port.
                    registry.ReportUsb(new[] { new UsbDevice(managed.Port, identity) });
                    return new(new { protocol = Protocol, result = managed.Frame });
                }
                case "usb.read":
                {
                    var result = await usb.ReadAsync(port, identity, cancel);
                    registry.ReportUsbSample(result, result.Port ?? port);
                    return new(new { protocol = Protocol, sample = result, transport = "usb" });
                }
                case "sample.read":
                {
                    var (reading, via) = await ReadSampleAsync(identity, port, registry, usb, cancel);
                    return new(new { protocol = Protocol, sample = reading, transport = via });
                }
                case "shutdown":
                    return new(new { protocol = Protocol, running = false }, true);
                default:
                    return new(new { protocol = Protocol, error = "unsupported" });
            }
        }
        catch (OperationCanceledException)
        { return new(new { protocol = Protocol, error = "timeout" }); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or TimeoutException or UnauthorizedAccessException
             or JsonException or InvalidOperationException or KeyNotFoundException or HttpRequestException)
        { return new(new { protocol = Protocol, error = "device_unavailable" }); }
    }
}
