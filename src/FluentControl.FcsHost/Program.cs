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

if (args.Length > 0 && args[0].StartsWith("--script-", StringComparison.Ordinal))
{
    try { await FcsScripts.CliAsync(args); } catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
    return;
}
if (args is ["--self-test"]) { await HostChecks.RunAsync(); return; }
var directory = Path.GetFullPath(Environment.GetEnvironmentVariable("FCS_DATA") ?? "data");
Directory.CreateDirectory(directory);
string ReadToken(string name)
{
    var path = Path.Combine(directory, name);
    if (!File.Exists(path))
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var file = new FileStream(path, options);
        file.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()));
    }
    var value = File.ReadAllText(path).Trim();
    if (value.Length != 64 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid " + name);
    return value;
}
var token = ReadToken("gateway.token"); // Existing FC / HA read-only credential.
var managerToken = ReadToken("manager.token"); // Separate authority for host configuration.
if (token == managerToken) throw new InvalidDataException("Manager and reader tokens must differ.");
var managerOrigin = Environment.GetEnvironmentVariable("FCS_MANAGER_ORIGIN");
if (managerOrigin is not null && (!Uri.TryCreate(managerOrigin, UriKind.Absolute, out var originUri) ||
    originUri.Scheme is not ("https" or "http") || originUri.AbsolutePath != "/" ||
    originUri.UserInfo.Length != 0 || originUri.Query.Length != 0 || originUri.Fragment.Length != 0))
    throw new InvalidDataException("FCS_MANAGER_ORIGIN must be an HTTP(S) origin.");
managerOrigin = managerOrigin?.TrimEnd('/');
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
builder.Services.AddSingleton(sp => new ScriptCollector(directory, sp.GetRequiredService<GatewayHost>()));
builder.Services.AddHostedService<ScriptCollector>(sp => sp.GetRequiredService<ScriptCollector>());
builder.Services.AddRateLimiter(options => options.AddConcurrencyLimiter("rpc", limiter =>
    { limiter.PermitLimit = 8; limiter.QueueLimit = 0; limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst; }));
var app = builder.Build();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    await next();
});
foreach (var route in new[] { "/", "/manager.js" })
{
    var resource = route == "/" ? "manager.html" : "manager.js";
    var type = route == "/" ? "text/html; charset=utf-8" : "text/javascript; charset=utf-8";
    app.MapGet(route, () => Results.Stream(typeof(GatewayHost).Assembly.GetManifestResourceStream("FcsManager." + resource)!, type));
}
app.MapGet("/healthz", () => Results.Json(new { running = true }));
app.MapPost("/rpc", async (HttpContext context, GatewayHost host) =>
{
    var authorization = context.Request.Headers.Authorization.ToString();
    var manager = HostChecks.Authorized(authorization, managerToken);
    if (!manager && !HostChecks.Authorized(authorization, token)) return Results.Unauthorized();
    var expectedOrigin = managerOrigin ?? $"{context.Request.Scheme}://{context.Request.Host}";
    if (context.Request.Headers.TryGetValue("Origin", out var origin) && origin.ToString() != expectedOrigin)
        return Results.StatusCode(403);
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted); deadline.CancelAfter(TimeSpan.FromSeconds(5));
    try
    {
        var root = await GatewayHost.ReadJsonAsync(context.Request.Body, 4096, deadline.Token);
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() > 1))
            throw new InvalidDataException("Invalid request.");
        string Field(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var op = Field("operation"); var id = Field("deviceId");
        if ((op is "manager.capabilities" or "nodes.save" or "nodes.remove" or "scripts.list") && !manager)
            return Results.StatusCode(403);
        if (op == "discover" && manager) await host.ScanAsync(deadline.Token);
        object result = op switch {
            "manager.capabilities" => FcsManagement.Capabilities(usb: false, configureNodes: true),
            "scripts.list" => FcsManagement.Scripts(Path.Combine(directory, "scripts")),
            "nodes.save" => host.SaveNode(id, Field("ip")),
            "nodes.remove" => host.RemoveNode(id),
            "status" => new { protocol = "fcs-gateway/1", running = true, mode = "lan-host" },
            "list" or "discover" => new { protocol = "fcs-gateway/1", devices = host.Snapshot() },
            "node.request" when FcsNode.ValidId(id) => host.Node(id, Field("command"), root.TryGetProperty("arguments", out var a) ? a : default),
            "history.read" when FcsNode.ValidId(id) => new { protocol = "fcs-gateway/1", records = host.History.Read(id) },
            _ => throw new InvalidDataException("Unsupported operation.") };
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result);
    }
    catch (Exception e) when (e is InvalidDataException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
    { return Results.Json(new { protocol = "fcs-gateway/1", error = "unavailable_or_unsupported" }, statusCode: 400); }
}).RequireRateLimiting("rpc");
await app.RunAsync();

internal sealed class Collector(GatewayHost host) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => host.RunAsync(stoppingToken);
}

internal sealed class ScriptCollector(string directory, GatewayHost host) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => new FcsScriptLoop(Path.Combine(directory, "scripts"), (input, cancel) =>
    {
        var envelope = JsonSerializer.SerializeToElement(host.Node(input.GetProperty("deviceId").GetString()!, "data.read",
            JsonSerializer.SerializeToElement(new { sensorId = input.GetProperty("sensorId").GetString(), signalId = input.GetProperty("signalId").GetString(), mode = "fresh" })));
        if (envelope.GetProperty("sampleAgeMilliseconds").GetInt32() > 5000) throw new IOException("Input stale.");
        return Task.FromResult(envelope.GetProperty("result").Clone());
    }).RunAsync(stoppingToken);
}
