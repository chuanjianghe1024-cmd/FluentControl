using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

// Python syntax is interpreted by the bundled bounded AST worker, not executed
// as arbitrary Python. Network effects are separately validated by this host.
internal static class FcsScripts
{
    public static string DirectoryPath => Environment.GetEnvironmentVariable("FCS_SCRIPTS") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "GatewayScripts");
    public static async Task<JsonElement> EvaluateAsync(string source, object signals, object settings, bool validate, CancellationToken cancel)
    {
        if (Encoding.UTF8.GetByteCount(source) > 16384) throw new InvalidDataException("Script source too large.");
        var worker = Path.Combine(AppContext.BaseDirectory, "fcs-script-runner.py");
        var python = Environment.GetEnvironmentVariable("FCS_PYTHON") ?? "python";
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-I"); start.ArgumentList.Add(worker);
        using var process = Process.Start(start) ?? throw new IOException("Python 3.10+ is required for Gateway scripts. Set FCS_PYTHON.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel); deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var input = JsonSerializer.Serialize(new { source, signals, settings, mode = validate ? "validate" : "test" });
            if (Encoding.UTF8.GetByteCount(input) > 65536) throw new InvalidDataException("Script input too large.");
            await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token); process.StandardInput.Close();
            var output = ReadBoundedAsync(process.StandardOutput, 16384, deadline.Token);
            var error = ReadBoundedAsync(process.StandardError, 4096, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var text = await output; await error;
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 24 });
            if (process.ExitCode != 0 || !doc.RootElement.GetProperty("ok").GetBoolean()) throw new InvalidDataException("Script rejected: " +
                (doc.RootElement.TryGetProperty("error", out var reason) ? reason.GetString() : "worker_failed"));
            return doc.RootElement.Clone();
        }
        finally { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken cancel)
    {
        var result = new StringBuilder(); var buffer = new char[1024]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancel)) > 0)
        { if (result.Length + count > limit) throw new InvalidDataException("Worker output exceeded limit."); result.Append(buffer, 0, count); }
        return result.ToString();
    }
    public static JsonElement Load(string path)
    {
        if (new FileInfo(path).Length > 32768) throw new InvalidDataException("Script package too large.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 16 });
        var r = doc.RootElement;
        if (r.GetProperty("format").GetString() != "fcs.gateway-script/1" || !FcsNode.ValidId(r.GetProperty("id").GetString()) ||
            r.GetProperty("enabled").ValueKind is not (JsonValueKind.True or JsonValueKind.False) || r.GetProperty("source").GetString()!.Length > 16384 ||
            r.GetProperty("inputs").GetArrayLength() is < 1 or > 8 || r.GetProperty("targets").ValueKind != JsonValueKind.Object ||
            r.GetProperty("settings").ValueKind != JsonValueKind.Object || r.GetProperty("holdMs").GetInt32() is < 0 or > 86400000 ||
            r.GetProperty("cooldownMs").GetInt32() is < 1000 or > 86400000) throw new InvalidDataException("Invalid script configuration.");
        if (r.TryGetProperty("rearmMode", out var rearm) && rearm.GetString() is not ("explicit" or "empty")) throw new InvalidDataException("Invalid rearm mode.");
        var names = new HashSet<string>();
        foreach (var input in r.GetProperty("inputs").EnumerateArray())
        {
            foreach (var key in new[] { "name", "deviceId", "sensorId", "signalId" })
                if (!FcsNode.ValidId(input.GetProperty(key).GetString())) throw new InvalidDataException("Invalid signal reference.");
            if (!names.Add(input.GetProperty("name").GetString()!) || input.GetProperty("name").GetString() == "clock") throw new InvalidDataException("Duplicate input name.");
        }
        return r.Clone();
    }
    public static async Task CliAsync(string[] args)
    {
        if (args.Length == 2 && args[0] == "--script-create")
        {
            var example = new { format = "fcs.gateway-script/1", id = "light-wake", enabled = false, rearmMode = "explicit", holdMs = 10000, cooldownMs = 300000,
                inputs = new[] { new { name = "desk", deviceId = "replace-device-id", sensorId = "ambient-01", signalId = "illuminance" } },
                settings = new { above = 300, below = 200 }, targets = new { desktop = new { kind = "wol", mac = "02:00:00:00:00:01", broadcast = "192.168.1.255", port = 9 } },
                source = "def run(signals, settings):\n    light = signals[\"desk\"]\n    if light[\"value\"] < settings[\"below\"]:\n        return [{\"action\": \"rearm\"}]\n    if light[\"value\"] > settings[\"above\"]:\n        return [{\"action\": \"wol\", \"target\": \"desktop\"}]\n    return []\n" };
            using var file = new FileStream(args[1], FileMode.CreateNew);
            JsonSerializer.Serialize(file, example, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine("Created disabled script. Edit inputs, targets and settings; validate and test before enabling."); return;
        }
        if (args.Length is 2 or 3 && args[0] is "--script-validate" or "--script-test")
        {
            var job = Load(args[1]); object signals = new { };
            if (args[0] == "--script-test")
            {
                if (args.Length != 3 || new FileInfo(args[2]).Length > 16384) throw new InvalidDataException("Provide a bounded signals fixture JSON file.");
                signals = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(args[2]));
            }
            var result = await EvaluateAsync(job.GetProperty("source").GetString()!, signals, job.GetProperty("settings"), args[0] == "--script-validate", default);
            if (args[0] == "--script-test") foreach (var action in result.GetProperty("actions").EnumerateArray()) ValidateAction(job, action);
            Console.WriteLine(result.GetRawText()); return;
        }
        throw new InvalidDataException("Usage: --script-create FILE | --script-validate FILE | --script-test FILE SIGNALS.json");
    }
    public static JsonElement ValidateAction(JsonElement job, JsonElement action)
    {
        var kind = action.GetProperty("action").GetString();
        if (kind == "rearm" && action.EnumerateObject().Count() == 1) return default;
        var name = action.GetProperty("target").GetString()!;
        if (!FcsNode.ValidId(name) || !job.GetProperty("targets").TryGetProperty(name, out var target) || target.GetProperty("kind").GetString() != kind)
            throw new InvalidDataException("Action target not configured.");
        if (kind == "wol")
        {
            WakePacket(target.GetProperty("mac").GetString()!);
            if (!PrivateAddress(target.GetProperty("broadcast").GetString()!, out _) || target.GetProperty("port").GetInt32() is not (7 or 9))
                throw new InvalidDataException("Invalid WoL destination.");
        }
        else if (kind == "https")
        {
            if (!Uri.TryCreate(target.GetProperty("url").GetString(), UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length > 0 || url.Fragment.Length > 0)
                throw new InvalidDataException("HTTPS target required.");
            if (!action.TryGetProperty("data", out var data) || Encoding.UTF8.GetByteCount(data.GetRawText()) > 8192) throw new InvalidDataException("Upload data limit.");
        }
        else throw new InvalidDataException("Unsupported action.");
        return target;
    }
    internal static byte[] WakePacket(string mac)
    {
        var bytes = Convert.FromHexString(mac.Replace(":", "").Replace("-", ""));
        if (bytes.Length != 6 || (bytes[0] & 1) != 0 || bytes.All(b => b == 0)) throw new InvalidDataException("Invalid MAC.");
        var packet = Enumerable.Repeat((byte)255, 102).ToArray();
        for (int i = 0; i < 16; i++) bytes.CopyTo(packet, 6 + i * 6);
        return packet;
    }
    internal static bool PrivateAddress(string value, out IPAddress? ip)
    {
        if (!IPAddress.TryParse(value, out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes(); return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168;
    }
    public static async Task DispatchAsync(JsonElement job, JsonElement action, CancellationToken cancel)
    {
        var target = ValidateAction(job, action);
        if (action.GetProperty("action").GetString() == "wol")
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            await udp.SendAsync(WakePacket(target.GetProperty("mac").GetString()!),
                new IPEndPoint(IPAddress.Parse(target.GetProperty("broadcast").GetString()!), target.GetProperty("port").GetInt32()), cancel);
        }
        else
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel); deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
            using var request = new HttpRequestMessage(HttpMethod.Post, target.GetProperty("url").GetString());
            if (target.TryGetProperty("tokenFile", out var tokenFile))
            {
                var path = tokenFile.GetString()!;
                if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > 2048) throw new InvalidDataException("Invalid token file.");
                request.Headers.Authorization = new("Bearer", (await File.ReadAllTextAsync(path, deadline.Token)).Trim());
            }
            request.Content = new StringContent(action.GetProperty("data").GetRawText(), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
        }
    }
}

internal sealed class FcsScriptLoop(string directory, Func<JsonElement, CancellationToken, Task<JsonElement>> read)
{
    private sealed class State { public bool Armed, Failed; public long? Since, Last, Observed; public DateTime Stamp; }
    private readonly Dictionary<string, State> states = new();
    public async Task RunAsync(CancellationToken cancel)
    {
        try { Directory.CreateDirectory(directory); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        while (!cancel.IsCancellationRequested)
        {
            string[] paths;
            try { paths = Directory.EnumerateFiles(directory, "*.json").Where(p => Path.GetFileName(p) != "execution-status.json").Order(StringComparer.Ordinal).Take(8).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
            foreach (var old in states.Keys.Where(k => !paths.Contains(k)).ToArray()) states.Remove(old);
            foreach (var path in paths)
            {
                DateTime stamp;
                try { stamp = File.GetLastWriteTimeUtc(path); } catch (IOException) { continue; }
                if (!states.TryGetValue(path, out var state) || state.Stamp != stamp) states[path] = state = new State { Stamp = stamp };
                if (state.Failed) continue;
                try
                {
                    var job = FcsScripts.Load(path);
                    if (!job.GetProperty("enabled").GetBoolean()) continue;
                    var collectionStarted = Stopwatch.GetTimestamp();
                    var signals = new Dictionary<string, object>();
                    foreach (var input in job.GetProperty("inputs").EnumerateArray())
                    {
                        var frame = await read(input, cancel);
                        var signal = frame.GetProperty("data").EnumerateArray().Single(d => d.GetProperty("sensorId").GetString() == input.GetProperty("sensorId").GetString() && d.GetProperty("signalId").GetString() == input.GetProperty("signalId").GetString());
                        if (!signal.GetProperty("valid").GetBoolean() || !signal.GetProperty("fresh").GetBoolean()) throw new IOException("Input unavailable.");
                        signals[input.GetProperty("name").GetString()!] = signal;
                    }
                    signals["clock"] = new { utcHour = DateTimeOffset.UtcNow.Hour, utcMinute = DateTimeOffset.UtcNow.Minute };
                    var output = await FcsScripts.EvaluateAsync(job.GetProperty("source").GetString()!, signals, job.GetProperty("settings"), false, cancel);
                    if (Stopwatch.GetElapsedTime(collectionStarted).TotalSeconds > 5) throw new IOException("Inputs aged during evaluation.");
                    if (state.Observed is { } observed && Stopwatch.GetElapsedTime(observed).TotalSeconds > 5) { state.Armed = false; state.Since = null; }
                    state.Observed = Stopwatch.GetTimestamp();
                    var actions = output.GetProperty("actions").EnumerateArray().ToArray();
                    if (actions.Length == 1 && actions[0].GetProperty("action").GetString() == "rearm")
                    { FcsScripts.ValidateAction(job, actions[0]); state.Armed = true; state.Since = null; continue; }
                    if (actions.Length == 0) { if (!job.TryGetProperty("rearmMode", out var rearm) || rearm.GetString() != "explicit") state.Armed = true; state.Since = null; continue; }
                    if (actions.Any(a => a.GetProperty("action").GetString() == "rearm")) throw new InvalidDataException("Rearm cannot be combined with effects.");
                    foreach (var action in actions) FcsScripts.ValidateAction(job, action);
                    if (!state.Armed) continue;
                    state.Since ??= Stopwatch.GetTimestamp();
                    if (Stopwatch.GetElapsedTime(state.Since.Value).TotalMilliseconds < job.GetProperty("holdMs").GetInt32() ||
                        state.Last is { } last && Stopwatch.GetElapsedTime(last).TotalMilliseconds < job.GetProperty("cooldownMs").GetInt32()) continue;
                    state.Armed = false; state.Since = null; state.Last = Stopwatch.GetTimestamp();
                    // No action queue or automatic retries after failure/restart.
                    foreach (var action in actions) await FcsScripts.DispatchAsync(job, action, cancel);
                    Record(job.GetProperty("id").GetString()!, "sent");
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
                catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException)
                { state.Armed = false; state.Since = null; Record(Path.GetFileNameWithoutExtension(path), "unavailable_or_unconfirmed"); }
                catch (Exception)
                { state.Failed = true; state.Armed = false; state.Since = null; Record(Path.GetFileNameWithoutExtension(path), "invalid_or_failed_reload_required"); }
            }
            try { await Task.Delay(2000, cancel); } catch (OperationCanceledException) { break; }
        }
    }
    private void Record(string id, string status)
    {
        try { File.WriteAllText(Path.Combine(directory, "execution-status.json"), JsonSerializer.Serialize(new { id, status, at = DateTimeOffset.UtcNow })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
