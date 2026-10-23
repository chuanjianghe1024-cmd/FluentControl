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
        if (!root.TryGetProperty(name, out var p)) return fallback;
        if (p.ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid " + name);
        var value = p.GetString() ?? fallback;
        if (value.Length > 120) throw new InvalidDataException("Field too long.");
        return value;
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
                        pluginRuntime = "not-implemented", deviceCodeInstall = "usb-only",
                        wirelessRules = "planned" });
                case "usb.discover":
                {
                    var items = await usb.DiscoverAsync(port, cancel);
                    registry.ReportUsb(items);
                    return new(new { protocol = Protocol, usbDevices = items, devices = registry.Snapshot() });
                }
                case "usb.manage":
                {
                    var result = await usb.ManageAsync(port, identity, Field(root, "command"), arguments, cancel);
                    registry.ReportUsb(new[] { new UsbDevice(port, identity) });
                    return new(new { protocol = Protocol, result });
                }
                case "usb.read":
                {
                    var result = await usb.ReadAsync(port, identity, cancel);
                    registry.ReportUsbSample(result, port);
                    return new(new { protocol = Protocol, sample = result, transport = "usb" });
                }
                case "sample.read":
                {
                    if (!GatewayRegistry.ValidId(identity)) throw new InvalidDataException("Invalid device identity.");
                    UsbReading sample;
                    string transport;
                    try
                    {
                        sample = await usb.ReadAsync(port, identity, cancel);
                        registry.ReportUsbSample(sample, port);
                        transport = "usb";
                    }
                    catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException)
                    {
                        sample = await registry.ReadPublicAsync(identity, cancel);
                        transport = "lan";
                    }
                    return new(new { protocol = Protocol, sample, transport });
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
