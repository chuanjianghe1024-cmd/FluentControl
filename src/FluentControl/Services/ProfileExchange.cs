using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace FluentControl.Services;

public static class ModelIdentity
{
    // PnP manufacturer/product only. Deliberately excludes serials and connection paths.
    public static string FromDevicePath(string path) => Regex.Match(path.ToUpperInvariant(), @"(?:DISPLAY|MONITOR)[#\\]([A-Z0-9]{7})(?:[#\\]|$)").Groups[1].Value;
    public static bool IsValid(string value) => value is not null && Regex.IsMatch(value, @"\A[A-Z0-9]{7}\z");
}
public sealed class SharedMonitorProfile
{
    public string Schema { get; set; } = "fluentcontrol.monitor-profile";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public List<SharedMonitorSlot> Monitors { get; set; } = new();
}
public sealed class SharedMonitorSlot
{
    public string Slot { get; set; } = "";
    public string ModelId { get; set; } = "";
    public Dictionary<string, double> Values { get; set; } = new();
    public BrightnessMapping? Brightness { get; set; }
}
public static class ProfileExchange
{
    public const int MaxBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 16 };
    public static bool IsShareable(string key) => key == "temperature" || VcpCatalog.Find(key) is { Kind: VcpKind.Continuous or VcpKind.Choice or VcpKind.Gamma, Code: not 0xD6 and not 0xCA };
    public static SharedMonitorProfile Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("Profile exceeds 1 MB.");
        var profile = JsonSerializer.Deserialize<SharedMonitorProfile>(text, Json) ?? throw new InvalidDataException("Empty profile.");
        Validate(profile); return profile;
    }
    public static string Serialize(SharedMonitorProfile profile) { Validate(profile); return JsonSerializer.Serialize(profile, Json); }
    public static void Validate(SharedMonitorProfile profile)
    {
        if (profile.Schema != "fluentcontrol.monitor-profile" || profile.Version != 1) throw new InvalidDataException("Unsupported profile schema/version.");
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80 || profile.Monitors is null || profile.Monitors.Count is < 1 or > 16) throw new InvalidDataException("Invalid profile name or monitor count.");
        var slots = new HashSet<string>();
        foreach (var slot in profile.Monitors)
        {
            if (slot is null || string.IsNullOrWhiteSpace(slot.Slot) || slot.Slot.Length > 40 || !slots.Add(slot.Slot) || !ModelIdentity.IsValid(slot.ModelId) || slot.Values is null || slot.Values.Count > 64) throw new InvalidDataException("Invalid monitor slot.");
            slot.Brightness?.Validate();
            foreach (var value in slot.Values)
            {
                if (!IsShareable(value.Key) || !double.IsFinite(value.Value) || value.Value < 0 || value.Value > 65535 ||
                    (VcpCatalog.Find(value.Key)?.Kind == VcpKind.Continuous && value.Value > 100)) throw new InvalidDataException("Unsupported profile control: " + value.Key);
            }
        }
    }
    public static bool CanApply(ControlChannel channel, double value) => channel.CanSave && !channel.IsAction && double.IsFinite(value) && value >= channel.Minimum && value <= channel.Maximum && (channel.Options is null || channel.Options.Any(x => x.Value == value));
}

// Future model adapters are code shipped/reviewed with the app, never executable downloads in a profile.
public interface IMonitorModelAdapter
{
    string Id { get; }
    bool Matches(string modelId, string firmware);
    IReadOnlyList<VcpDefinition> AdditionalFeatures { get; }
}
public interface IMonitorProfileExchange
{
    Task<SharedMonitorProfile> DownloadAsync(Uri uri, CancellationToken cancellationToken = default);
    Task UploadAsync(Uri endpoint, SharedMonitorProfile profile, CancellationToken cancellationToken = default);
}
public sealed class HttpMonitorProfileExchange : IMonitorProfileExchange
{
    private static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0) throw new ArgumentException("An HTTPS URL without credentials is required.");
    }
    private static HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    public async Task<SharedMonitorProfile> DownloadAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ValidateUri(uri);
        using var client = Client();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > ProfileExchange.MaxBytes) throw new InvalidDataException("Profile exceeds 1 MB.");
        using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream(); var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (output.Length + read > ProfileExchange.MaxBytes) throw new InvalidDataException("Profile exceeds 1 MB.");
            output.Write(buffer, 0, read);
        }
        return ProfileExchange.Parse(Encoding.UTF8.GetString(output.ToArray()));
    }
    public async Task UploadAsync(Uri endpoint, SharedMonitorProfile profile, CancellationToken cancellationToken = default)
    {
        ValidateUri(endpoint);
        using var client = Client();
        using var body = new StringContent(ProfileExchange.Serialize(profile), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = body };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
