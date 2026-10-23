using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FluentControl.Services;

public sealed record FcsDeviceStatus(string DeviceId, string Ip, string Status, double? Lux, DateTimeOffset? LastSampleUtc);

public static class FcsGatewayClient
{
    private const string PipeName = "FluentControl.FcsGateway.v1";
    private static readonly object StartLock = new();
    private static DateTimeOffset lastStart;
    public static bool EnsureStarted()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var exe = Path.Combine(AppContext.BaseDirectory, "FluentControl.FcsGateway.exe");
        if (!File.Exists(exe)) return false;
        lock (StartLock)
        {
            // Protect FC start/rebuild loops; the Gateway's named mutex enforces single instance.
            if (DateTimeOffset.UtcNow - lastStart < TimeSpan.FromSeconds(10)) return true;
            lastStart = DateTimeOffset.UtcNow;
            try
            {
                Process.Start(new ProcessStartInfo(exe, "--serve")
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory });
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return false; }
        }
    }
    public static async Task<JsonDocument> QueryAsync(string operation, CancellationToken cancellation = default)
    {
        if (operation is not ("status" or "list" or "discover")) throw new ArgumentOutOfRangeException(nameof(operation));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(operation == "discover" ? 7 : 3));
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(2000, timeout.Token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { operation }));
        var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Gateway disconnected.");
        if (line.Length > 64 * 1024) throw new InvalidDataException("Gateway response too large.");
        var json = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 12 });
        if (json.RootElement.GetProperty("protocol").GetString() != "fcs-gateway/1")
        { json.Dispose(); throw new InvalidDataException("Unexpected gateway protocol."); }
        return json;
    }
    public static async Task<IReadOnlyList<FcsDeviceStatus>> ListAsync(bool refresh, CancellationToken cancellation = default)
    {
        using var response = await QueryAsync(refresh ? "discover" : "list", cancellation);
        var found = new List<FcsDeviceStatus>();
        foreach (var d in response.RootElement.GetProperty("devices").EnumerateArray())
        {
            var id = d.GetProperty("deviceId").GetString();
            var ip = d.GetProperty("ip").GetString();
            var state = d.GetProperty("status").GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(ip) || state is not ("online" or "offline")) continue;
            var lx = d.GetProperty("lux");
            DateTimeOffset? at = null;
            if (d.TryGetProperty("lastSampleUtc", out var timestamp) && timestamp.ValueKind == JsonValueKind.String && timestamp.TryGetDateTimeOffset(out var parsed)) at = parsed;
            found.Add(new(id, ip, state!, lx.ValueKind == JsonValueKind.Number ? lx.GetDouble() : null, at));
        }
        return found;
    }
}
