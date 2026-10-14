using System.Runtime.InteropServices;

namespace FluentControl.Services;

internal sealed record SystemBrightnessReading(string InstanceName, byte Current, byte[] Levels);
internal interface ISystemBrightnessBackend
{
    IReadOnlyList<SystemBrightnessReading> Read();
    bool IsConnected(string devicePath);
    void Write(string instanceName, byte value);
}

// Windows brightness is independent of DDC. Only exact PnP instance matches
// authorize a channel; neither model names nor enumeration order are identities.
internal sealed class SystemBrightnessController(ISystemBrightnessBackend backend, Action<string>? log = null) : IDisposable
{
    private readonly object sync = new();
    private bool disposed;
    internal static string? DeviceInstance(string path)
    {
        if (!path.StartsWith(@"\\?\", StringComparison.Ordinal)) return null;
        var parts = path[4..].Split('#');
        return parts.Length == 4 && parts[0].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase)
            && parts[1].Length > 0 && parts[2].Length > 0 && Guid.TryParse(parts[3], out _)
            ? string.Join("\\", parts.Take(3)) : null;
    }
    internal static bool Matches(string path, string instance)
    {
        var expected = DeviceInstance(path);
        if (expected is null) return false;
        var suffix = instance.LastIndexOf('_');
        return suffix > 0 && suffix < instance.Length - 1 && instance[(suffix + 1)..].All(char.IsAsciiDigit)
            && expected.Equals(instance[..suffix], StringComparison.OrdinalIgnoreCase);
    }
    internal static byte Target(double value, IReadOnlyList<byte> levels)
    {
        if (!double.IsFinite(value) || value < 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value));
        var valid = levels.Where(x => x <= 100).Distinct().Order().ToArray();
        if (valid.Length == 0) throw new InvalidDataException("Windows brightness levels unavailable.");
        return valid.OrderBy(x => Math.Abs(x - value)).First();
    }
    private SystemBrightnessReading Require(string path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!backend.IsConnected(path)) throw new IOException(Strings.T("屏幕连接已改变，请重新读取设备。", "Display connection changed. Refresh devices."));
        var matches = backend.Read().Where(r => Matches(path, r.InstanceName)).ToArray();
        if (matches.Length != 1 || matches[0].Current > 100 || matches[0].Levels.Length == 0 || matches[0].Levels.Any(x => x > 100))
            throw new IOException(Strings.T("Windows 亮度不可用，请检查显示驱动并重新读取设备。", "Windows brightness unavailable. Check the display driver and refresh devices."));
        return matches[0];
    }
    internal ControlChannel? Create(string path, IReadOnlyList<SystemBrightnessReading> readings)
    {
        var matches = readings.Where(r => Matches(path, r.InstanceName)).ToArray();
        if (matches.Length != 1 || matches[0].Current > 100 || matches[0].Levels.Length == 0 || matches[0].Levels.Any(x => x > 100)) return null;
        var initial = matches[0];
        return new()
        {
            Name = Strings.T("亮度", "Brightness"), Detail = Strings.T("Windows 内屏亮度", "Windows panel brightness"), Glyph = "\uE706",
            DeviceId = path, PropertyKey = "brightness", Minimum = 0, Maximum = 100, Value = initial.Current,
            Read = () => { lock (sync) return Require(path).Current; },
            Write = value =>
            {
                lock (sync)
                {
                    var current = Require(path);
                    var target = Target(value, current.Levels);
                    backend.Write(current.InstanceName, target); // Exactly one write, never retry or fall back to DDC.
                    log?.Invoke($"Windows panel brightness write: requested={value}, target={target}");
                }
            }
        };
    }
    public void Dispose() { lock (sync) disposed = true; }
}

// Use the Windows-supplied WMI automation API; no PowerShell process, new runtime
// or persistent cross-thread COM objects. Each operation owns/releases its RCWs.
internal sealed class WindowsSystemBrightnessBackend : ISystemBrightnessBackend
{
    public bool IsConnected(string devicePath) => new WindowsDisplayHdrBackend().ReadActive()
        .Count(t => t.DevicePath.Equals(devicePath, StringComparison.OrdinalIgnoreCase)) == 1;

    public IReadOnlyList<SystemBrightnessReading> Read()
    {
        using var scope = new WmiScope();
        var methods = new List<string>();
        scope.Query("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE", row => methods.Add((string)scope.Property(row, "InstanceName")));
        var result = new List<SystemBrightnessReading>();
        scope.Query("SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE", row =>
        {
            var instance = (string)scope.Property(row, "InstanceName");
            if (methods.Count(x => x.Equals(instance, StringComparison.OrdinalIgnoreCase)) != 1) return;
            var levels = scope.Property(row, "Level") as Array;
            result.Add(new(instance, Convert.ToByte(scope.Property(row, "CurrentBrightness")),
                levels?.Cast<object>().Select(Convert.ToByte).ToArray() ?? Array.Empty<byte>()));
        });
        return result;
    }
    public void Write(string instanceName, byte value)
    {
        using var scope = new WmiScope();
        var matches = new List<object>();
        scope.Query("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE", row =>
        {
            if (string.Equals((string)scope.Property(row, "InstanceName"), instanceName, StringComparison.OrdinalIgnoreCase)) matches.Add(row);
        });
        if (matches.Count != 1) throw new IOException(Strings.T("屏幕连接已改变，请重新读取设备。", "Display connection changed. Refresh devices."));
        dynamic target = matches[0];
        dynamic input = CreateInput(scope, target, value);
        dynamic output = scope.Own(target.ExecMethod_("WmiSetBrightness", input));
        var result = Convert.ToUInt32(scope.Property(output, "ReturnValue"));
        if (result != 0) throw new IOException(Strings.T("Windows 亮度写入失败", "Windows brightness write failed") + $" (0x{result:X8})");
    }
    private static object CreateInput(WmiScope scope, dynamic target, byte value)
    {
        dynamic methods = scope.Own(target.Methods_);
        dynamic method = scope.Own(methods.Item("WmiSetBrightness"));
        dynamic definition = scope.Own(method.InParameters);
        dynamic input = scope.Own(definition.SpawnInstance_());
        scope.SetProperty(input, "Timeout", "0"); // WMI automation represents CIM uint64 as a string.
        scope.SetProperty(input, "Brightness", value);
        return input;
    }
    // Read schema and construct (but never execute) method input even on a VM
    // without a panel. This checks COM enumeration, property access and binding
    // separately from optional hardware-provider availability.
    internal static void CheckAutomation()
    {
        using var scope = new WmiScope();
        var schemas = new List<object>();
        scope.Query("SELECT * FROM meta_class WHERE __CLASS = 'WmiMonitorBrightnessMethods'", row =>
        {
            if ((string)scope.Property(row, "__CLASS") == "WmiMonitorBrightnessMethods") schemas.Add(row);
        });
        if (schemas.Count != 1) throw new InvalidOperationException("Windows brightness WMI schema unavailable.");
        dynamic input = CreateInput(scope, schemas[0], 50);
        if (Convert.ToByte(scope.Property(input, "Brightness")) != 50 || Convert.ToUInt64(scope.Property(input, "Timeout")) != 0)
            throw new InvalidOperationException("WMI brightness input marshalling failed.");
    }
    private sealed class WmiScope : IDisposable
    {
        private readonly List<object> objects = new();
        private readonly dynamic services;
        internal dynamic Own(object value) { objects.Add(value); return value; }
        internal WmiScope()
        {
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                dynamic locator = Own(Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator", true)!)!);
                services = Own(locator.ConnectServer(".", @"root\wmi", null, null, null, null, 128));
            }
            catch { Dispose(); throw; }
        }
        internal object Property(dynamic row, string name)
        {
            dynamic properties = Own(row.Properties_);
            dynamic property = Own(properties.Item(name));
            return property.Value;
        }
        internal void SetProperty(dynamic row, string name, object value)
        {
            dynamic properties = Own(row.Properties_);
            dynamic property = Own(properties.Item(name));
            property.Value = value;
        }
        internal void Query(string query, Action<object> visit)
        {
            // Use the documented indexed collection API instead of the .NET
            // IDispatch IEnumerable adapter, which fails for schema queries too.
            dynamic rows = Own(services.ExecQuery(query, "WQL", 0));
            var count = (int)rows.Count;
            if (count > 256) throw new InvalidDataException("Too many WMI brightness endpoints.");
            for (var i = 0; i < count; i++) visit(Own(rows.ItemIndex(i)));
        }
        public void Dispose()
        {
            for (var i = objects.Count - 1; i >= 0; i--) if (Marshal.IsComObject(objects[i])) Marshal.ReleaseComObject(objects[i]);
            objects.Clear();
        }
    }
}
