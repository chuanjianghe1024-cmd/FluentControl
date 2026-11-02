using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Opc.Ua;

namespace FluentControl.FcsOpcUa;

internal sealed class GatewayReader(Options options, ResourceManager resources, string pipeName = "FluentControl.FcsGateway.v1") : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(6) };
    private readonly Dictionary<string, (long Sequence, long Uptime, DateTime Timestamp, long Received)> previous = new();
    public async Task<JsonElement> Read(string device, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        var request = JsonSerializer.Serialize(new { operation = "node.request", deviceId = device, command = "data.read", arguments = new { mode = "fresh" } });
        Stream input;
        if (options.WindowsPipe)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync(deadline.Token);
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), deadline.Token);
                await pipe.FlushAsync(deadline.Token);
            }
            catch { pipe.Dispose(); throw; }
            input = pipe;
        }
        else
        {
            if (new FileInfo(options.GatewayTokenFile!).Length > 256) throw new InvalidDataException("Oversized Gateway token file.");
            var token = (await File.ReadAllTextAsync(options.GatewayTokenFile!, deadline.Token)).Trim();
            if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid Gateway token file.");
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.GatewayUrl!), "rpc"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            message.Content = new StringContent(request, Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
            return await Parse(body, false, deadline.Token);
        }
        await using (input) return await Parse(input, true, deadline.Token);
    }
    private static async Task<JsonElement> Parse(Stream input, bool line, CancellationToken cancellation)
    {
        using var output = new MemoryStream(); var buffer = new byte[4096];
        while (true)
        {
            int count = await input.ReadAsync(buffer, cancellation); if (count == 0) break;
            int end = line ? Array.IndexOf(buffer, (byte)'\n', 0, count) : -1;
            output.Write(buffer, 0, end < 0 ? count : end);
            if (output.Length > 262144) throw new InvalidDataException("Gateway response exceeds 256 KiB.");
            if (end >= 0) break;
        }
        using var document = JsonDocument.Parse(output.ToArray()); return document.RootElement.Clone();
    }
    public DataValue Convert(JsonElement envelope, Signal signal)
    {
        if (envelope.GetProperty("protocol").GetString() != "fcs-gateway/1") throw new InvalidDataException("Invalid Gateway protocol.");
        var frame = envelope.GetProperty("result");
        if (frame.GetProperty("protocol").GetString() != "fcs-node/2" || frame.GetProperty("deviceId").GetString() != signal.DeviceId || !frame.GetProperty("ok").GetBoolean())
            throw new InvalidDataException("Invalid node identity or result.");
        var matches = frame.GetProperty("data").EnumerateArray().Where(x => x.GetProperty("sensorId").GetString() == signal.SensorId && x.GetProperty("signalId").GetString() == signal.SignalId).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Missing or duplicate signal.");
        var item = matches[0];
        var sample = new DataValue { StatusCode = StatusCodes.BadSensorFailure, SourceTimestamp = DateTime.MinValue };
        if (!item.GetProperty("valid").GetBoolean() || item.GetProperty("status").GetString() is not ("ready" or "cached")) return sample;
        if (item.GetProperty("valueType").GetString() != signal.ValueType || item.GetProperty("unit").GetString() != signal.Unit)
            throw new InvalidDataException("Signal schema changed; review export configuration.");
        var value = item.GetProperty("value");
        sample.Value = signal.ValueType switch {
            "number" when value.TryGetDouble(out var number) && double.IsFinite(number) => number,
            "integer" => value.GetInt64(), "boolean" => value.GetBoolean(),
            "string" when value.GetString() is { Length: <= 4096 } s => s,
            _ => throw new InvalidDataException("Invalid signal value.")
        };
        long sequence = item.GetProperty("sequence").GetInt64(), uptime = item.GetProperty("uptimeMs").GetInt64();
        if (sequence < 0 || uptime < 0) throw new InvalidDataException("Invalid sample counters.");
        double age = envelope.TryGetProperty("sampleAgeMilliseconds", out var a) ? a.GetDouble() : 0;
        if (!double.IsFinite(age) || age < 0) throw new InvalidDataException("Invalid sample age.");
        var now = DateTime.UtcNow;
        // Never turn C3 uptime into a fabricated UTC sensor timestamp.
        var timestamp = envelope.TryGetProperty("receivedAtUtc", out var time) ? time.GetDateTime().ToUniversalTime() : now;
        if (timestamp > now.AddSeconds(5)) throw new InvalidDataException("Future Gateway receipt timestamp.");
        var key = signal.Key(options.SystemId, options.GatewayId);
        lock (previous)
        {
            if (previous.TryGetValue(key, out var old) && sequence == old.Sequence && uptime == old.Uptime)
            { timestamp = old.Timestamp; age = Math.Max(age, Stopwatch.GetElapsedTime(old.Received).TotalMilliseconds); }
            else previous[key] = (sequence, uptime, timestamp, Stopwatch.GetTimestamp());
        }
        sample.SourceTimestamp = timestamp;
        sample.StatusCode = item.GetProperty("status").GetString() == "ready" && item.GetProperty("fresh").GetBoolean() && age <= options.StaleMilliseconds && now - timestamp <= TimeSpan.FromMilliseconds(options.StaleMilliseconds)
            ? StatusCodes.Good : StatusCodes.BadNoCommunication;
        return sample;
    }
    public async Task Run(CancellationToken cancellation)
    {
        var groups = options.Signals.GroupBy(s => s.DeviceId).ToArray();
        while (!cancellation.IsCancellationRequested)
        {
            await Parallel.ForEachAsync(groups, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellation }, async (group, ct) => {
                try
                {
                    var envelope = await Read(group.Key, ct);
                    foreach (var signal in group)
                    {
                        try { resources.Update(signal.Key(options.SystemId, options.GatewayId), Convert(envelope, signal)); }
                        catch (Exception e) when (e is not OperationCanceledException) { resources.Fail(signal.Key(options.SystemId, options.GatewayId), StatusCodes.BadDecodingError); }
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                { foreach (var signal in group) resources.Fail(signal.Key(options.SystemId, options.GatewayId), StatusCodes.BadNotConnected); }
            });
            await Task.Delay(options.PollMilliseconds, cancellation);
        }
    }
    public void Dispose() => http.Dispose();
}
