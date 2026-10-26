using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class FcsPluginCli
{
    public static async Task<JsonElement> RequestAsync(string id, string command, object arguments)
    {
        var values = JsonSerializer.SerializeToElement(arguments);
        FcsNode.Arguments(command, values);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var pipe = new NamedPipeClientStream(".", "FluentControl.FcsGateway.v1", PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(2000, timeout.Token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { operation = "node.request", deviceId = id, command, arguments = values, port = "AUTO" }));
        var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Gateway disconnected.");
        if (line.Length > 65536) throw new InvalidDataException("Reply too large.");
        using var doc = JsonDocument.Parse(line);
        if (doc.RootElement.TryGetProperty("error", out _)) throw new IOException("Gateway operation failed.");
        var frame = FcsNode.Validate(doc.RootElement.GetProperty("result"), id);
        if (!frame.GetProperty("ok").GetBoolean()) throw new IOException("Node operation failed: " + frame.GetProperty("error").GetProperty("code").GetString());
        return frame.Clone();
    }
    public static async Task InstallAsync(string id, string file)
    {
        if (new FileInfo(file).Length is <= 0 or > 12288) throw new InvalidDataException("Package limit: 12288 bytes.");
        var bytes = await File.ReadAllBytesAsync(file);
        using var package = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var name = package.RootElement.GetProperty("id").GetString()!;
        var begin = await RequestAsync(id, "plugin.begin", new { id = name, size = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        var token = begin.GetProperty("token").GetString()!;
        try
        {
            for (int offset = 0; offset < bytes.Length; offset += 512)
            {
                var size = Math.Min(512, bytes.Length - offset);
                var frame = await RequestAsync(id, "plugin.chunk", new { token, offset, hex = Convert.ToHexString(bytes.AsSpan(offset, size)).ToLowerInvariant() });
                if (frame.GetProperty("offset").GetInt32() != offset + size) throw new IOException("Upload offset mismatch.");
            }
            Console.WriteLine((await RequestAsync(id, "plugin.commit", new { token })).GetRawText());
        }
        catch
        {
            // Abort is a cleanup operation, never retry commit or the action itself.
            try { await RequestAsync(id, "plugin.abort", new { token }); } catch { }
            throw;
        }
    }
    public static async Task<bool> RunAsync(string[] args)
    {
        if (args.Length == 3 && args[0] == "--plugin-install") { await InstallAsync(args[1], args[2]); return true; }
        if (args.Length is 3 or 4 && args[0] == "--node")
        {
            using var doc = JsonDocument.Parse(args.Length == 4 ? await ReadArguments(args[3]) : "{}");
            Console.WriteLine((await RequestAsync(args[1], args[2], doc.RootElement)).GetRawText()); return true;
        }
        return false;
    }
    private static async Task<string> ReadArguments(string path)
    {
        if (new FileInfo(path).Length > 8192) throw new InvalidDataException("Arguments too large.");
        return await File.ReadAllTextAsync(path);
    }
}
