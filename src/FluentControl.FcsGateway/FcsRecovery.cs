using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed class RecoveryPackage : IDisposable
{
    public readonly byte[] Firmware;
    public RecoveryPackage(byte[] firmware) => Firmware = firmware;
    public void Dispose() { }
}
internal static class FcsRecovery
{
    private static readonly object Sync = new();
    private static string state = "idle", message = "", jobId = "";
    public static bool Busy { get { lock (Sync) return state == "running"; } }
    public static object Status { get { lock (Sync) return new { state, message, jobId }; } }
    public static RecoveryPackage ReadPackage(string path)
    {
        if (new FileInfo(path).Length is < 1 or > 6 * 1024 * 1024) throw new InvalidDataException("Invalid recovery archive size.");
        using var zip = ZipFile.OpenRead(path);
        var allowed = new HashSet<string> { "manifest.json", "firmware.bin", "LICENSE-MicroPython.txt", "README.txt" };
        if (zip.Entries.Count != allowed.Count || zip.Entries.Any(e => !allowed.Remove(e.FullName) || e.Length > 4 * 1024 * 1024))
            throw new InvalidDataException("Invalid recovery archive entries.");
        var entry = zip.GetEntry("manifest.json")!;
        if (entry.Length > 4096) throw new InvalidDataException("Manifest too large.");
        using var stream = entry.Open(); using var doc = JsonDocument.Parse(stream);
        var m = doc.RootElement;
        if (m.GetProperty("format").GetString() != "fcs.recovery/1" || m.GetProperty("chip").GetString() != "esp32c3" ||
            m.GetProperty("board").GetString() != "fcs-c3-bh1750-gpio4-3" || m.GetProperty("flashBytes").GetInt32() != 4194304 ||
            m.GetProperty("offset").GetInt32() != 0 || m.GetProperty("core").GetString() != "frozen-mpy")
            throw new InvalidDataException("Recovery package is for a different board.");
        var firmware = zip.GetEntry("firmware.bin")!;
        if (firmware.Length < 65536 || firmware.Length > 4 * 1024 * 1024 || firmware.Length != m.GetProperty("size").GetInt32())
            throw new InvalidDataException("Invalid firmware size.");
        using var content = firmware.Open(); using var bytes = new MemoryStream(); content.CopyTo(bytes); var data = bytes.ToArray();
        if (!Convert.ToHexString(SHA256.HashData(data)).Equals(m.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase) || data[0] != 0xe9)
            throw new InvalidDataException("Firmware hash/header mismatch.");
        return new(data);
    }
    public static object Start(string port, JsonElement args, FcsUsb usb)
    {
        if (!FcsUsb.ValidPort(port) || port.Equals("AUTO", StringComparison.OrdinalIgnoreCase) ||
            !args.GetProperty("confirmErase").GetBoolean()) throw new InvalidDataException("Explicit USB port and erase confirmation required.");
        var path = Path.GetFullPath(args.GetProperty("path").GetString()!);
        if (Path.GetDirectoryName(path) != Path.GetFullPath(FcsManager.Imports)) throw new InvalidDataException("Select an imported recovery package.");
        using var package = ReadPackage(path);
        var firmware = package.Firmware; // Verified bytes, not a path reopened after validation.
        var tool = Path.Combine(AppContext.BaseDirectory, "esptool", "esptool.exe");
        if (!File.Exists(tool)) throw new IOException("Bundled esptool is missing. Use the complete Gateway package.");
        lock (Sync)
        {
            if (state == "running") throw new IOException("Recovery already running.");
            state = "running"; jobId = Guid.NewGuid().ToString("N"); message = "Waiting for USB access";
        }
        _ = Task.Run(async () =>
        {
            var temporary = Path.Combine(Path.GetTempPath(), "fcs-recovery-" + Guid.NewGuid().ToString("N"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            try
            {
                Directory.CreateDirectory(temporary); var image = Path.Combine(temporary, "firmware.bin");
                await File.WriteAllBytesAsync(image, firmware, timeout.Token);
                await usb.ExclusiveAsync(async () =>
                {
                    // Probe chip and flash before any destructive command. --chip refuses other ESP chips.
                    var info = await RunAsync(tool, port, new[] { "flash-id" }, timeout.Token);
                    if (!info.Contains("4MB", StringComparison.OrdinalIgnoreCase) && !info.Contains("4 MB", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Expected a 4 MB flash device; no erase performed.");
                    SetMessage("Erasing settings and firmware; keep USB connected");
                    await RunAsync(tool, port, new[] { "erase-flash" }, timeout.Token);
                    SetMessage("Writing firmware; keep USB connected");
                    await RunAsync(tool, port, new[] { "write-flash", "--flash-size", "4MB", "0x0", image }, timeout.Token);
                    SetMessage("Verifying firmware");
                    await RunAsync(tool, port, new[] { "verify-flash", "0x0", image }, timeout.Token);
                }, timeout.Token);
                lock (Sync) { state = "succeeded"; message = "Firmware verified. Press RESET if needed, scan devices and configure Wi-Fi again. Sensor hardware has not been verified by flashing."; }
            }
            catch (Exception ex)
            {
                lock (Sync) { state = "failed"; message = ex is OperationCanceledException ? "Recovery timed out. Re-enter BOOT mode and retry." : ex.Message; }
            }
            finally { try { Directory.Delete(temporary, true); } catch (IOException) { } }
        });
        return Status;
    }
    private static void SetMessage(string value) { lock (Sync) message = value; }
    private static async Task<string> RunAsync(string tool, string port, string[] command, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "--chip", "esp32c3", "--port", port, "--baud", "115200" }.Concat(command)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start esptool.");
        async Task<string> Read(StreamReader reader)
        {
            var output = new StringBuilder(); var buffer = new char[1024]; int n;
            while ((n = await reader.ReadAsync(buffer.AsMemory(), cancel)) > 0)
            { if (output.Length + n > 65536) throw new IOException("Unexpected tool output."); output.Append(buffer, 0, n); }
            return output.ToString();
        }
        try
        {
            var stdout = Read(process.StandardOutput); var stderr = Read(process.StandardError);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(cancel));
            if (process.ExitCode != 0) throw new IOException("USB flashing failed. Close other serial tools, enter BOOT mode, then retry. No automatic retry was performed.");
            return await stdout;
        }
        finally { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    }
}
