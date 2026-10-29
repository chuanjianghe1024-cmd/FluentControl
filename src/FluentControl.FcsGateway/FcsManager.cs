using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Local UI only. A bounded one-request TCP connection avoids HTTP.sys URL ACLs
// and administrator rights. No proxy, chunked requests, CORS, or keep-alive.
internal static class FcsManager
{
    public static string Imports => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "RecoveryImports");
    public static async Task RunAsync(bool launch = true)
    {
        var executable = Environment.ProcessPath ?? throw new IOException("Missing executable.");
        Process.Start(new ProcessStartInfo(executable, "--serve") { UseShellExecute = false, CreateNoWindow = true });
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var origin = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        if (launch) Process.Start(new ProcessStartInfo(origin + "/#" + token) { UseShellExecute = true });
        else Console.WriteLine(origin + "/#" + token);
        using var life = new CancellationTokenSource(TimeSpan.FromHours(8));
        using var capacity = new SemaphoreSlim(8);
        try
        {
            while (!life.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(life.Token);
                if (!await capacity.WaitAsync(0, life.Token)) { client.Dispose(); continue; }
                _ = ServeAsync(client, capacity, origin, token, life.Token);
            }
        }
        catch (OperationCanceledException) when (life.IsCancellationRequested) { }
        finally { listener.Stop(); }
    }
    internal static bool Authorized(string? host, string? suppliedOrigin, string? authorization, string origin, string token) =>
        host == new Uri(origin).Authority && (suppliedOrigin is null || suppliedOrigin == origin) && authorization == "Bearer " + token;
    private static async Task ServeAsync(TcpClient client, SemaphoreSlim capacity, string origin, string token, CancellationToken cancel)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(45)); cancel = deadline.Token;
            var stream = client.GetStream();
            async Task Respond(int status, byte[] body, string type = "application/json")
            {
                var header = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancel); await stream.WriteAsync(body, cancel);
            }
            try
            {
                var header = new List<byte>(); var one = new byte[1];
                while (true)
                {
                    if (header.Count >= 8192 || await stream.ReadAsync(one, cancel) != 1) throw new IOException("Invalid HTTP header.");
                    header.Add(one[0]);
                    var n = header.Count;
                    if (n >= 4 && header[n-4] == 13 && header[n-3] == 10 && header[n-2] == 13 && header[n-1] == 10) break;
                }
                var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n"); var first = lines[0].Split(' ');
                if (first.Length != 3 || first[2] != "HTTP/1.1") throw new IOException("Invalid HTTP request.");
                var headers = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1).Where(x => x.Length != 0))
                {
                    var index = line.IndexOf(':');
                    if (index <= 0 || char.IsWhiteSpace(line[0]) || !headers.TryAdd(line[..index], line[(index+1)..].Trim())) throw new IOException("Invalid HTTP headers.");
                }
                string? Get(string key) => headers.GetValueOrDefault(key);
                if (headers.ContainsKey("Transfer-Encoding")) throw new IOException("Chunked requests not supported.");
                var method = first[0]; var path = first[1];
                var limit = path == "/package" ? 6 * 1024 * 1024 : 4096;
                if (!int.TryParse(Get("Content-Length") ?? "0", out int length) || length < 0 || length > limit) throw new IOException("Invalid request length.");
                // Drain the bounded body before rejecting: closing a socket with unread
                // request data causes Windows to reset it and discard the HTTP error body.
                var data = new byte[length]; await stream.ReadExactlyAsync(data, cancel);
                if (Get("Host") != new Uri(origin).Authority) { await Respond(403, "{}"u8.ToArray()); return; }
                if (method == "GET" && path is "/" or "/manager.js")
                {
                    var resource = path == "/" ? "manager.html" : "manager.js";
                    using var input = typeof(FcsManager).Assembly.GetManifestResourceStream("FcsManager." + resource) ?? throw new IOException("Missing UI.");
                    using var content = new MemoryStream(); await input.CopyToAsync(content, cancel);
                    await Respond(200, content.ToArray(), path == "/" ? "text/html; charset=utf-8" : "text/javascript; charset=utf-8"); return;
                }
                if (!Authorized(Get("Host"), Get("Origin"), Get("Authorization"), origin, token)) { await Respond(403, "{}"u8.ToArray()); return; }
                if (method != "POST") { await Respond(405, "{}"u8.ToArray()); return; }
                string output;
                if (path == "/package")
                {
                    Directory.CreateDirectory(Imports);
                    var file = Path.Combine(Imports, Convert.ToHexString(SHA256.HashData(data)) + ".zip");
                    await File.WriteAllBytesAsync(file, data, cancel);
                    try { using var verified = FcsRecovery.ReadPackage(file); }
                    catch { File.Delete(file); throw; }
                    output = JsonSerializer.Serialize(new { path = file });
                }
                else if (path == "/rpc")
                {
                    var json = Encoding.UTF8.GetString(data); using var doc = JsonDocument.Parse(json);
                    var operation = doc.RootElement.GetProperty("operation").GetString();
                    if (operation is not ("manager.capabilities" or "list" or "discover" or "usb.discover" or "usb.manage" or "node.request" or
                        "scripts.list" or "recovery.inspect" or "recovery.ports" or "recovery.start" or "recovery.status")) throw new InvalidDataException("Unsupported manager operation.");
                    output = await QueryAsync(json, cancel);
                }
                else { await Respond(404, "{}"u8.ToArray()); return; }
                await Respond(200, Encoding.UTF8.GetBytes(output));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException or ArgumentException or SocketException)
            {
                try { await Respond(400, "{\"error\":\"Operation failed; check the device and selected package.\"}"u8.ToArray()); } catch { }
            }
            finally { capacity.Release(); }
        }
    }
    public static async Task<string> QueryAsync(string json, CancellationToken cancel)
    {
        using var pipe = new NamedPipeClientStream(".", "FluentControl.FcsGateway.v1", PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(4000, cancel);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
        await writer.WriteLineAsync(json);
        return await reader.ReadLineAsync(cancel) ?? throw new IOException("Gateway disconnected.");
    }
}
