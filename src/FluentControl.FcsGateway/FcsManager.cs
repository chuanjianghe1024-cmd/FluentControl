using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Local UI only: loopback, random bearer in URL fragment, strict Host/Origin,
// no CORS, no externally supplied command lines or device URLs.
internal static class FcsManager
{
    public static string Imports => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "RecoveryImports");
    public static async Task RunAsync(bool launch = true)
    {
        var executable = Environment.ProcessPath ?? throw new IOException("Missing executable.");
        Process.Start(new ProcessStartInfo(executable, "--serve") { UseShellExecute = false, CreateNoWindow = true });
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var origin = "http://127.0.0.1:" + port;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var listener = new HttpListener(); listener.Prefixes.Add(origin + "/"); listener.Start();
        if (launch) Process.Start(new ProcessStartInfo(origin + "/#" + token) { UseShellExecute = true });
        else Console.WriteLine(origin + "/#" + token); // Explicit headless diagnostic mode, not a persistent log.
        using var life = new CancellationTokenSource(TimeSpan.FromHours(8));
        using var stop = life.Token.Register(listener.Stop);
        while (!life.IsCancellationRequested)
        {
            HttpListenerContext c;
            try { c = await listener.GetContextAsync(); } catch (HttpListenerException) when (life.IsCancellationRequested) { break; }
            _ = HandleAsync(c, origin, token, life.Token);
        }
    }
    internal static bool Authorized(string? host, string? suppliedOrigin, string? authorization, string origin, string token) =>
        host == new Uri(origin).Authority && (suppliedOrigin is null || suppliedOrigin == origin) && authorization == "Bearer " + token;
    private static async Task HandleAsync(HttpListenerContext context, string origin, string token, CancellationToken cancel)
    {
        var q = context.Request; var r = context.Response;
        r.Headers["Cache-Control"] = "no-store"; r.Headers["X-Content-Type-Options"] = "nosniff";
        r.Headers["Referrer-Policy"] = "no-referrer";
        r.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        try
        {
            if (q.Headers["Host"] != new Uri(origin).Authority) { r.StatusCode = 403; return; }
            var path = q.Url!.AbsolutePath;
            if (q.HttpMethod == "GET" && path is "/" or "/manager.js")
            {
                var resource = path == "/" ? "manager.html" : "manager.js";
                using var stream = typeof(FcsManager).Assembly.GetManifestResourceStream("FcsManager." + resource) ?? throw new IOException("Missing UI.");
                r.ContentType = resource.EndsWith("html") ? "text/html; charset=utf-8" : "text/javascript; charset=utf-8";
                await stream.CopyToAsync(r.OutputStream, cancel); return;
            }
            if (!Authorized(q.Headers["Host"], q.Headers["Origin"], q.Headers["Authorization"], origin, token)) { r.StatusCode = 403; return; }
            if (q.HttpMethod != "POST") { r.StatusCode = 405; return; }
            using var data = new MemoryStream(); var buffer = new byte[8192]; int n;
            var limit = path == "/package" ? 6 * 1024 * 1024 : 4096;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel); deadline.CancelAfter(TimeSpan.FromSeconds(45));
            while ((n = await q.InputStream.ReadAsync(buffer, deadline.Token)) > 0)
            { if (data.Length + n > limit) throw new InvalidDataException("Request too large."); data.Write(buffer, 0, n); }
            string output;
            if (path == "/package")
            {
                Directory.CreateDirectory(Imports);
                var bytes = data.ToArray(); var name = Convert.ToHexString(SHA256.HashData(bytes)) + ".zip";
                var file = Path.Combine(Imports, name);
                await File.WriteAllBytesAsync(file, bytes, deadline.Token);
                try { using var verified = FcsRecovery.ReadPackage(file); }
                catch { File.Delete(file); throw; }
                output = JsonSerializer.Serialize(new { path = file });
            }
            else if (path == "/rpc")
            {
                var json = Encoding.UTF8.GetString(data.ToArray()); using var doc = JsonDocument.Parse(json);
                var operation = doc.RootElement.GetProperty("operation").GetString();
                if (operation is not ("list" or "discover" or "usb.discover" or "usb.manage" or "node.request" or
                    "scripts.list" or "recovery.ports" or "recovery.start" or "recovery.status")) throw new InvalidDataException("Unsupported manager operation.");
                output = await QueryAsync(json, deadline.Token);
            }
            else { r.StatusCode = 404; return; }
            r.ContentType = "application/json";
            await r.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(output), deadline.Token);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException or ArgumentException)
        {
            r.StatusCode = 400;
            try { await r.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"error\":\"Operation failed; check the device and selected package.\"}"), cancel); } catch { }
        }
        finally { r.Close(); }
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
