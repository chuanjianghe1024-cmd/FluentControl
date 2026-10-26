using Microsoft.AspNetCore.Hosting;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args is ["--self-test"]) { await HostChecks.RunAsync(); return; }
var directory = Path.GetFullPath(Environment.GetEnvironmentVariable("FCS_DATA") ?? "data");
Directory.CreateDirectory(directory);
var tokenPath = Path.Combine(directory, "gateway.token");
if (!File.Exists(tokenPath))
{
    using var tokenFile = new FileStream(tokenPath, new FileStreamOptions { Mode = FileMode.CreateNew,
        Access = FileAccess.Write, UnixCreateMode = OperatingSystem.IsWindows() ? null : UnixFileMode.UserRead | UnixFileMode.UserWrite });
    tokenFile.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()));
}
var token = File.ReadAllText(tokenPath).Trim();
if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid gateway.token.");
var configPath = Path.Combine(directory, "nodes.json");
if (!File.Exists(configPath)) File.WriteAllText(configPath, JsonSerializer.Serialize(new HostConfig(), new JsonSerializerOptions { WriteIndented = true }));
if (new FileInfo(configPath).Length > 16384) throw new InvalidDataException("Configuration too large.");
var config = JsonSerializer.Deserialize<HostConfig>(File.ReadAllText(configPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidDataException("Missing configuration.");
config.Validate();
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); builder.Logging.AddSimpleConsole();
builder.WebHost.ConfigureKestrel(options => { options.Limits.MaxRequestBodySize = 4096; options.Limits.MaxConcurrentConnections = 64; });
builder.Services.AddSingleton(new GatewayHost(config, directory));
builder.Services.AddHostedService<Collector>();
builder.Services.AddRateLimiter(options => options.AddConcurrencyLimiter("rpc", limiter =>
    { limiter.PermitLimit = 8; limiter.QueueLimit = 0; limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst; }));
var app = builder.Build();
app.UseRateLimiter();
app.MapGet("/healthz", () => Results.Json(new { running = true }));
app.MapPost("/rpc", async (HttpContext context, GatewayHost host) =>
{
    var authorization = context.Request.Headers.Authorization.ToString();
    if (!HostChecks.Authorized(authorization, token)) return Results.Unauthorized();
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted); deadline.CancelAfter(TimeSpan.FromSeconds(5));
    try
    {
        var root = await GatewayHost.ReadJsonAsync(context.Request.Body, 4096, deadline.Token);
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() > 1))
            throw new InvalidDataException("Invalid request.");
        string Field(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var op = Field("operation"); var id = Field("deviceId");
        object result = op switch {
            "status" => new { protocol = "fcs-gateway/1", running = true, mode = "lan-host" },
            "list" or "discover" => new { protocol = "fcs-gateway/1", devices = host.Snapshot() },
            "node.request" when FcsNode.ValidId(id) => host.Node(id, Field("command"), root.TryGetProperty("arguments", out var a) ? a : default),
            "history.read" when FcsNode.ValidId(id) => new { protocol = "fcs-gateway/1", records = host.History.Read(id) },
            _ => throw new InvalidDataException("Unsupported operation.") };
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result);
    }
    catch (Exception e) when (e is InvalidDataException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException)
    { return Results.Json(new { protocol = "fcs-gateway/1", error = "unavailable_or_unsupported" }, statusCode: 400); }
}).RequireRateLimiting("rpc");
await app.RunAsync();

internal sealed class Collector(GatewayHost host) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => host.RunAsync(stoppingToken);
}
