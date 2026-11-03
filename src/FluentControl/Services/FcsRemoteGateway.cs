using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
namespace FluentControl.Services;

public sealed class FcsRemoteSettings
{
    public bool Enabled { get; set; }
    public string Address { get; set; } = "";
    public bool AllowPrivateHttp { get; set; }
}

public static class FcsRemoteGateway
{
    private sealed record Connection(Uri Address, string Token);
    private static Connection? connection;
    private static bool enabled;
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(8) };
    public static bool Enabled => Volatile.Read(ref enabled);
    public static Uri ValidateAddress(string address, bool allowPrivateHttp)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" ||
            uri.Scheme is not ("https" or "http")) throw new InvalidDataException("Invalid Gateway address.");
        if (uri.Scheme == "http")
        {
            if (!IPAddress.TryParse(uri.Host, out var ip)) throw new InvalidDataException("HTTP requires a literal local address.");
            var b = ip.GetAddressBytes();
            var privateIp = b.Length == 4 && (b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168);
            if (!IPAddress.IsLoopback(ip) && !(allowPrivateHttp && privateIp)) throw new InvalidDataException("HTTPS required.");
        }
        return uri;
    }
    public static string KeyId(Uri address) => "fcs-gateway:" + address.AbsoluteUri;
    public static void Configure(FcsRemoteSettings? settings)
    {
        Volatile.Write(ref connection, null);
        Volatile.Write(ref enabled, settings?.Enabled == true);
        if (settings?.Enabled != true) return;
        try
        {
            var uri = ValidateAddress(settings.Address, settings.AllowPrivateHttp);
            Volatile.Write(ref connection, new Connection(uri, DeviceKeyStore.Read(KeyId(uri))));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException)
        { /* Keep remote enabled but unavailable; never silently substitute another Gateway. */ }
    }
    public static async Task TestAsync(Uri address, string token, CancellationToken cancel)
    { using var result = await SendAsync(new(address, token), new { operation = "status" }, cancel); }
    public static Task<JsonDocument> QueryAsync(string operation, string? id, string? command, object? arguments, CancellationToken cancel)
    {
        if (operation is not ("list" or "discover" or "status" or "node.request") ||
            operation == "node.request" && command is not ("node.info" or "sensors.list" or "operations.list" or "data.read" or "sampling.get"))
            throw new InvalidDataException("Remote Gateway is read-only.");
        return SendAsync(Volatile.Read(ref connection) ?? throw new IOException("Remote Gateway is unavailable."),
            new { operation, deviceId = id, command, arguments }, cancel);
    }
    private static async Task<JsonDocument> SendAsync(Connection target, object payload, CancellationToken cancel)
    {
        if (target.Token.Length != 64 || !target.Token.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid Gateway token.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel); deadline.CancelAfter(TimeSpan.FromSeconds(8));
        cancel = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(target.Address, "rpc"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var buffer = new MemoryStream(); var chunk = new byte[2048]; int count;
        while ((count = await stream.ReadAsync(chunk, cancel)) > 0)
        { if (buffer.Length + count > 65536) throw new InvalidDataException("Gateway reply too large."); buffer.Write(chunk, 0, count); }
        var doc = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 20 });
        try
        {
            if (doc.RootElement.GetProperty("protocol").GetString() != "fcs-gateway/1" || doc.RootElement.TryGetProperty("error", out _))
                throw new InvalidDataException("Invalid Gateway reply.");
            return doc;
        }
        catch { doc.Dispose(); throw; }
    }
}
