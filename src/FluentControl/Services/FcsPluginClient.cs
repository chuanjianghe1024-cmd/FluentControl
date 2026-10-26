using System.Security.Cryptography;
using System.Text.Json;
namespace FluentControl.Services;

public static class FcsPluginClient
{
    public static async Task<JsonElement> RequestAsync(string id, string command, object? arguments, CancellationToken cancel)
    {
        using var response = await FcsGatewayClient.QueryAsync("node.request", cancel, id, "AUTO", command, arguments ?? new { }, localOnly: true);
        var frame = response.RootElement.GetProperty("result");
        if (frame.GetProperty("protocol").GetString() != "fcs-node/2" || frame.GetProperty("deviceId").GetString() != id || !frame.GetProperty("ok").GetBoolean())
            throw new IOException("Device rejected plugin operation.");
        return frame.Clone();
    }
    public static async Task<JsonElement> InstallAsync(string id, string path, CancellationToken cancel)
    {
        if (new FileInfo(path).Length is <= 0 or > 12288) throw new InvalidDataException("Plugin package exceeds node limit.");
        var data = await File.ReadAllBytesAsync(path, cancel);
        using var package = JsonDocument.Parse(data);
        var name = package.RootElement.GetProperty("id").GetString();
        var response = await RequestAsync(id, "plugin.begin", new { id = name, size = data.Length, sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() }, cancel);
        var token = response.GetProperty("token").GetString();
        try
        {
            for (var offset = 0; offset < data.Length; offset += 512)
            {
                var size = Math.Min(512, data.Length - offset);
                response = await RequestAsync(id, "plugin.chunk", new { token, offset, hex = Convert.ToHexString(data.AsSpan(offset, size)).ToLowerInvariant() }, cancel);
                if (response.GetProperty("offset").GetInt32() != offset + size) throw new IOException("Upload offset mismatch.");
            }
            return await RequestAsync(id, "plugin.commit", new { token }, cancel);
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try { await RequestAsync(id, "plugin.abort", new { token }, cleanup.Token); } catch { }
            throw;
        }
    }
}
