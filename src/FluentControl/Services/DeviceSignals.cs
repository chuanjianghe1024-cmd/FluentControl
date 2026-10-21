using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluentControl.Services;

// Transport-neutral, bounded, numeric signal contract. No commands are taken from advertisements.
public sealed record SignalValue(string DeviceId, string Signal, string Unit, double Value, long Sequence, long UptimeMs);
public static class DeviceSignals
{
    public const string Protocol = "fc-device/1";
    public static SignalValue Parse(JsonElement frame)
    {
        if (frame.GetProperty("protocol").GetString() != Protocol || !frame.GetProperty("valid").GetBoolean()) throw new InvalidDataException("Invalid signal.");
        string id = LocalLightProtocol.Identity(frame), signal = frame.GetProperty("signal").GetString() ?? "", unit = frame.GetProperty("unit").GetString() ?? "";
        double value = frame.GetProperty("value").GetDouble();
        long seq = frame.GetProperty("sequence").GetInt64(), uptime = frame.GetProperty("uptimeMs").GetInt64();
        if (signal.Length is < 1 or > 64 || unit.Length > 24 || !double.IsFinite(value) || seq < 0 || uptime < 0) throw new InvalidDataException("Invalid signal fields.");
        return new(id, signal, unit, value, seq, uptime);
    }
    public static LightSample Light(JsonElement frame, string expected, DateTimeOffset now)
    {
        var s = Parse(frame);
        if (s.DeviceId != expected || s.Signal != "illuminance" || s.Unit != "lx" || s.Value is < 0 or > 200000) throw new InvalidDataException("Unexpected light signal.");
        // Timestamp is explicitly FC receive time, not an invented hardware clock.
        return new() { Format = "fluentcontrol.light-sample", Version = 1, SourceId = s.DeviceId, Timestamp = now, Lux = s.Value, Valid = true };
    }
    public static string SubscriptionLink(string id) => "fluentcontrol://subscribe?device=" + Uri.EscapeDataString(id);
    public static string? LinkIdentity(string text)
    {
        if (text.Length > 512 || !Uri.TryCreate(text, UriKind.Absolute, out var u) || u.Scheme != "fluentcontrol" || u.Host != "subscribe" ||
            u.UserInfo != "" || !u.IsDefaultPort || u.Fragment != "" || u.AbsolutePath is not ("" or "/") || !u.Query.StartsWith("?device=", StringComparison.Ordinal) || u.Query.Contains('&')) return null;
        var id = Uri.UnescapeDataString(u.Query[8..]);
        return id.Length is > 0 and <= 100 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? id : null;
    }
    public static string Mac(string key, string text) => Convert.ToHexString(HMACSHA256.HashData(Convert.FromHexString(key), Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static bool Verify(string key, string text, string signature)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Mac(key, text)), Convert.FromHexString(signature)); }
        catch (FormatException) { return false; }
    }
}

public static class NetworkSignalSensor
{
    // Only authenticated reads are available over LAN. Device management stays USB-only in this release.
    public static async Task<LightSample> ReadAsync(HttpClient client, LightSourceSubscription source, CancellationToken cancellation)
    {
        var key = DeviceKeyStore.Read(source.DeviceId);
        var endpoint = LightFeed.UriFor(source.Url);
        var nonce = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add("X-FC-Nonce", nonce);
        request.Headers.Add("X-FC-Signature", DeviceSignals.Mac(key, "READ\n" + source.DeviceId + "\n" + nonce));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > LightFeed.MaximumBytes) throw new InvalidDataException();
        using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var output = new MemoryStream(); var buffer = new byte[2048]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        { if (output.Length + count > LightFeed.MaximumBytes) throw new InvalidDataException(); output.Write(buffer, 0, count); }
        var body = new UTF8Encoding(false, true).GetString(output.ToArray());
        if (!response.Headers.TryGetValues("X-FC-Signature", out var signatures) || !DeviceSignals.Verify(key, nonce + "\n" + body, signatures.Single())) throw new InvalidDataException("Untrusted device response.");
        using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
        return DeviceSignals.Light(doc.RootElement, source.DeviceId, DateTimeOffset.UtcNow);
    }
    public static async Task<IReadOnlyList<(string Id, string Url)>> DiscoverAsync(CancellationToken cancellation)
    {
        using var udp = new UdpClient(0) { EnableBroadcast = true };
        var bytes = Encoding.ASCII.GetBytes("FC-DISCOVER/1");
        await udp.SendAsync(bytes, new IPEndPoint(IPAddress.Broadcast, 42735), cancellation);
        var found = new Dictionary<string, string>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            while (found.Count < 32)
            {
                var reply = await udp.ReceiveAsync(timeout.Token);
                if (reply.Buffer.Length > 1024) continue;
                try
                {
                    using var d = JsonDocument.Parse(reply.Buffer, new JsonDocumentOptions { MaxDepth = 4 });
                    if (d.RootElement.GetProperty("protocol").GetString() != DeviceSignals.Protocol) continue;
                    var id = LocalLightProtocol.Identity(d.RootElement);
                    // Reply supplies identity only. Host is the actual datagram sender; port/path are fixed.
                    found.TryAdd(id, $"http://{reply.RemoteEndPoint.Address}:8766/v1/sample");
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException) { }
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
        return found.Select(x => (x.Key, x.Value)).ToArray();
    }
}

// A USB-reported, connected IPv4 address is only a candidate endpoint.
// Setup succeeds only after NetworkSignalSensor verifies a signed, valid sample.
public static class WifiSetupNetwork
{
    public static string? TryUrl(JsonElement status)
    {
        if (!status.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String || state.GetString() != "connected" ||
            !status.TryGetProperty("ip", out var ip) || ip.ValueKind != JsonValueKind.String ||
            !IPAddress.TryParse(ip.GetString(), out var address) || address.AddressFamily != AddressFamily.InterNetwork) return null;
        var bytes = address.GetAddressBytes();
        if (bytes[0] == 0 || bytes[0] == 127 || bytes[0] >= 224 || (bytes[0] == 169 && bytes[1] == 254)) return null;
        return $"http://{address}:8766/v1/sample";
    }
}
