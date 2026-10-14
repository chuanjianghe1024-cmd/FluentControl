using FluentControl.Services;

internal static class SystemBrightnessTests
{
    private const string Panel = @"\\?\DISPLAY#BOE1234#4&123&0&UID1#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string Instance = @"DISPLAY\BOE1234\4&123&0&UID1_0";
    private sealed class Backend : ISystemBrightnessBackend
    {
        internal bool Connected = true, FailRead, FailWrite;
        internal int Writes;
        internal byte Current = 40;
        public IReadOnlyList<SystemBrightnessReading> Read() => FailRead ? throw new IOException("read failed") : new[] { new SystemBrightnessReading(Instance, Current, new byte[] { 10, 40, 70, 100 }) };
        public bool IsConnected(string path) => Connected;
        public void Write(string instance, byte value)
        {
            if (instance != Instance) throw new Exception("Wrong panel");
            Writes++;
            if (FailWrite) throw new IOException("write failed");
            Current = value;
        }
    }
    internal static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Check(SystemBrightnessController.Matches(Panel.ToLowerInvariant(), Instance), "Exact identity should ignore case.");
        Check(!SystemBrightnessController.Matches(Panel.Replace("UID1", "UID2"), Instance), "Same model is not the same panel.");
        Check(!SystemBrightnessController.Matches(Panel, Instance + "bad"), "Reject invalid WMI suffix.");
        var backend = new Backend();
        using var controller = new SystemBrightnessController(backend);
        var readings = backend.Read();
        Check(controller.Create(Panel, readings.Concat(readings).ToArray()) is null, "Ambiguous instances must not authorize writes.");
        Check(controller.Create(Panel, Array.Empty<SystemBrightnessReading>()) is null, "No WMI support means no channel.");
        var channel = controller.Create(Panel, readings)!;
        Check(channel.Value == 40 && channel.VcpCode is null && backend.Writes == 0, "Discovery is read-only and does not claim VCP support.");
        Check(ControlOperations.Apply(new[] { channel }, 61).Count == 0 && channel.Value == 70 && backend.Writes == 1, "Snap to driver levels and read real brightness.");
        backend.Connected = false;
        Check(ControlOperations.Apply(new[] { channel }, 10).Count == 1 && backend.Writes == 1, "Detached panel must not receive commands.");
        backend.Connected = true; backend.FailWrite = true;
        Check(ControlOperations.Apply(new[] { channel }, 10).Count == 1 && backend.Writes == 2 && channel.Value == 70, "One failed write, no retry or invented value.");
        backend.FailWrite = false; backend.FailRead = true;
        Check(ControlOperations.Apply(new[] { channel }, 10).Count == 1 && backend.Writes == 2, "Unreadable endpoint must not be written.");
        backend.FailRead = false;
        var externalValue = 0d;
        var external = new ControlChannel { Name = "External", Detail = "", Glyph = "", PropertyKey = "brightness", VcpCode = 0x10, Write = v => externalValue = v };
        Check(ControlOperations.Apply(new[] { channel, external }, 40, true).Count == 0 && externalValue == 40 && channel.Value == 40, "Mixed WMI/DDC controls link by property.");
        var device = new MonitorDevice { Id = Panel, Model = "Panel", Connection = "test" };
        device.Features.Add(new() { Definition = VcpCatalog.Find("brightness")!, Channel = channel }); device.Channels.Add(channel);
        Check(MonitorHardwareInfo.Capture(device)!.Controls.Single().VcpCode is null, "Exports must not label WMI brightness as DDC VCP 10.");
        Check(MonitorLinking.DesktopGroups(device.Channels).Single().Key == "brightness", "Windows brightness is available in desktop controls.");
        controller.Dispose();
        var count = backend.Writes;
        Check(ControlOperations.Apply(new[] { channel }, 100).Count == 1 && backend.Writes == count, "Disposed generations must never write.");
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var native = new WindowsSystemBrightnessBackend().Read();
                Console.WriteLine($"PASS: native read-only WMI brightness query ({native.Count} endpoints).");
            }
            catch (System.Runtime.InteropServices.COMException ex) when (unchecked((uint)ex.HResult) is 0x8004100C or 0x80041010)
            { Console.WriteLine("SKIP: this Windows host has no WMI brightness provider."); }
        }
        Console.WriteLine("PASS: Windows panel brightness identity, levels, failures, mixed linking and lifecycle.");
    }
}
