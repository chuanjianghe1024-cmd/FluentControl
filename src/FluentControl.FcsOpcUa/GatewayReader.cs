using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Opc.Ua;

namespace FluentControl.FcsOpcUa;

internal sealed class GatewayReader(Options options, ResourceManager resources, string pipeName = "FluentControl.FcsGateway.v1") : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(6) };
    private readonly ConcurrentDictionary<string, string> cursors = new();
    public Task<JsonElement> Read(string device, CancellationToken cancellation) =>
        ReadBatch(options.Signals.Where(s => s.DeviceId == device).ToArray(), cancellation);
    private async Task<JsonElement> ReadBatch(Signal[] signals, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        var batch = string.Join("|", signals.Select(s => s.DeviceId + "/" + s.SensorId + "/" + s.SignalId));
        cursors.TryGetValue(batch, out var cursor);
        var request = JsonSerializer.Serialize(new { operation = "subscriptions.read", arguments = new {
            signals = signals.Select(s => new { deviceId = s.DeviceId, sensorId = s.SensorId, signalId = s.SignalId }).ToArray(),
            cursor, waitMilliseconds = 1000 } });
        JsonElement Accept(JsonElement result)
        {
            if (result.GetProperty("protocol").GetString() != "fcs-gateway/1" || result.GetProperty("subscriptionProtocol").GetString() != "fcs-subscription/1")
                throw new InvalidDataException("Gateway subscription protocol required.");
            cursors[batch] = result.GetProperty("cursor").GetString()!;
            return result;
        }
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
            return Accept(await Parse(body, false, deadline.Token));
        }
        await using (input) return Accept(await Parse(input, true, deadline.Token));
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
        if (envelope.GetProperty("subscriptionProtocol").GetString() != "fcs-subscription/1")
            throw new InvalidDataException("Gateway subscription protocol required.");
        var matches = envelope.GetProperty("samples").EnumerateArray().Where(x => x.GetProperty("deviceId").GetString() == signal.DeviceId &&
            x.GetProperty("sensorId").GetString() == signal.SensorId && x.GetProperty("signalId").GetString() == signal.SignalId).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Missing or duplicate signal.");
        var item = matches[0];
        var sample = new DataValue { StatusCode = StatusCodes.BadSensorFailure, SourceTimestamp = DateTime.MinValue };
        if (!item.GetProperty("valid").GetBoolean() || !item.GetProperty("fresh").GetBoolean())
        { sample.StatusCode = item.GetProperty("status").GetString() == "stale" ? StatusCodes.BadNoCommunication : StatusCodes.BadSensorFailure; return sample; }
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
        var age = item.GetProperty("sampleAgeMilliseconds").GetDouble();
        var maximum = item.GetProperty("maxAgeMilliseconds").GetInt32();
        if (!double.IsFinite(age) || age < 0 || maximum <= 0) throw new InvalidDataException("Invalid sample age.");
        var now = DateTime.UtcNow;
        var timestamp = item.GetProperty("receivedAtUtc").GetDateTime().ToUniversalTime();
        if (timestamp > now.AddSeconds(5)) throw new InvalidDataException("Future Gateway receipt timestamp.");
        sample.SourceTimestamp = timestamp;
        sample.StatusCode = item.GetProperty("status").GetString() == "ready" && item.GetProperty("fresh").GetBoolean() && age <= Math.Min(maximum, options.StaleMilliseconds) && now - timestamp <= TimeSpan.FromMilliseconds(Math.Min(maximum, options.StaleMilliseconds))
            ? StatusCodes.Good : StatusCodes.BadNoCommunication;
        return sample;
    }
    public Task Run(CancellationToken cancellation) => Task.WhenAll(options.Signals.Chunk(16).Select(async batch =>
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                var envelope = await ReadBatch(batch, cancellation);
                foreach (var signal in batch)
                {
                    try { resources.Update(signal.Key(options.SystemId, options.GatewayId), Convert(envelope, signal)); }
                    catch (Exception e) when (e is not OperationCanceledException)
                    { resources.Fail(signal.Key(options.SystemId, options.GatewayId), StatusCodes.BadDecodingError); }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellation.IsCancellationRequested)
            { foreach (var signal in batch) resources.Fail(signal.Key(options.SystemId, options.GatewayId), StatusCodes.BadNotConnected); }
            // Long polling wakes on changes; a small floor bounds retries on failure.
            await Task.Delay(Math.Min(options.PollMilliseconds, 100), cancellation);
        }
    }));
    public void Dispose() => http.Dispose();
}
