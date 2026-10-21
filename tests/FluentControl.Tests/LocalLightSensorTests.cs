using FluentControl.Services;
using System.Text.Json;

internal static class LocalLightSensorTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid local sensor data was accepted.");
    }
    private static JsonElement Frame(object value) => JsonSerializer.SerializeToElement(value);
    internal static void Run()
    {
        foreach (var id in new[] { "VID_2E8A&PID_0005", "VID_303A&PID_1001&MI_00", "vid_1209&pid_c550" })
            Check(LocalLightSensor.IsAutomaticCandidate(id), "Known USB candidate");
        foreach (var id in new[] { "VID_1209&PID_C551", "VID_303A&PID_10010", "VID_1A86&PID_7523", "VID_1209", "" })
            Check(!LocalLightSensor.IsAutomaticCandidate(id), "Unrelated USB device excluded");
        Check(LocalLightProtocol.PortFor("fc-usb://auto") == "AUTO", "Automatic port selection");
        Check(LocalLightProtocol.PortFor("fc-usb://com5") == "COM5", "Explicit port selection");
        foreach (var url in new[] { "fc-usb://com0", "fc-usb://auto/run", "fc-usb://user@auto", "fc-usb://auto?command=READ", "http://auto", "fc-usb://auto:123" })
            Reject(() => LocalLightProtocol.PortFor(url));
        var hello = Frame(new { protocol = "fc-light-usb/1", sourceId = "board-A" });
        Check(LocalLightProtocol.Handshake(hello) == "board-A", "Require firmware protocol identity");
        Reject(() => LocalLightProtocol.Handshake(Frame(new { protocol = "other", sourceId = "board-A" })));
        var now = DateTimeOffset.UtcNow;
        var frame = Frame(new { sourceId = "board-A", valid = true, lux = 42.5 });
        var sample = LocalLightProtocol.Sample(frame, "board-A", now);
        Check(sample.Lux == 42.5 && sample.Timestamp == now && !sample.Simulated, "Real sample with receipt time");
        Reject(() => LocalLightProtocol.Sample(frame, "board-B", now));
        Reject(() => LocalLightProtocol.Sample(Frame(new { sourceId = "board-A", valid = false, lux = 0 }), "board-A", now));
        Reject(() => LocalLightProtocol.Sample(Frame(new { sourceId = "board-A", valid = true, lux = -1 }), "board-A", now));
        var settings = new AdaptiveSettings();
        settings.LightSources.Add(new() { Id = "usb", Name = "Probe", Url = "fc-usb://auto", DeviceId = "board-A", PollMilliseconds = 600000 });
        settings.ActiveSourceId = "usb";
        AdaptivePresets.Normalize(settings);
        Check(settings.LightSources.Single().DeviceId == "board-A" && settings.LightSources[0].PollMilliseconds == 600000, "Persist local identity and ten-minute interval");
        sample.Timestamp = now.AddMinutes(-10);
        Check(LightFeed.Parse(LightFeed.Serialize(sample), now, 600000).Lux == 42.5, "Long sampling intervals retain truthful timestamps");
        sample.Timestamp = now.AddMinutes(-31);
        Reject(() => LightFeed.Parse(LightFeed.Serialize(sample), now, 600000));
        Console.WriteLine("PASS: local sensor identity, endpoints, measurements, persistence and long-interval freshness.");
    }
}
