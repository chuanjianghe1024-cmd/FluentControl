using System.Text.Json;

// Shared manager contract. Host capabilities never grant a node operation:
// the transport and the node's own operation catalog still enforce permissions.
internal static class FcsManagement
{
    public static object Capabilities(bool usb, bool configureNodes) => new
    {
        protocol = "fcs-gateway/1", managerVersion = 1,
        platform = usb ? "windows" : "linux-host",
        capabilities = new { usbDiscovery = usb, usbManagement = usb, recovery = usb,
            lanDiscovery = true, nodeConfiguration = configureNodes, scriptsList = true },
        scriptCommand = usb ? "FluentControl.FcsGateway.exe" : "./FluentControl.FcsHost",
        limitations = usb
            ? "设备管理和重装需要将 USB 接到此 Windows 网关。"
            : "此网关通过局域网读取设备；USB 配网、设备插件安装和重装请使用 Windows 网关。Docker 桥接网络可能无法广播发现，可手动添加设备。"
    };

    public static object Scripts(string directory)
    {
        var scripts = new List<object>();
        if (Directory.Exists(directory))
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order().Take(32))
        {
            try
            {
                var job = FcsScripts.Load(file);
                scripts.Add(new { id = job.GetProperty("id").GetString(), enabled = job.GetProperty("enabled").GetBoolean(), status = "configured" });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException)
            { scripts.Add(new { id = Path.GetFileNameWithoutExtension(file), enabled = false, status = "invalid" }); }
        }
        return new { protocol = "fcs-gateway/1", directory, scripts };
    }
}
