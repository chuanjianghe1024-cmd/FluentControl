using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace FluentControl.Services;

public static class ModelIdentity
{
    // PnP manufacturer/product only. Deliberately excludes serials and connection paths.
    public static string FromDevicePath(string path) => Regex.Match(path.ToUpperInvariant(), @"(?:DISPLAY|MONITOR)[#\\]([A-Z0-9]{7})(?:[#\\]|$)").Groups[1].Value;
    public static bool IsValid(string value) => value is not null && Regex.IsMatch(value, @"\A[A-Z0-9]{7}\z");
    // Friendly labels for common PnP manufacturers; unknown IDs remain explicit.
    public static string Brand(string modelId) => (modelId.Length >= 3 ? modelId[..3] : "") switch
    {
        "HWV" => "Huawei", "DEL" => "Dell", "GSM" => "LG", "HWP" or "HPN" => "HP", "HNM" => "HONOR", "HKC" => "HKC",
        var code => code
    };
}
public class SharedScene
{
    public string Name { get; set; } = "";
    public List<SharedMonitorSlot> Monitors { get; set; } = new();
    public List<string> Applications { get; set; } = new();
}
public sealed class SharedMonitorProfile : SharedScene
{
    public string Schema { get; set; } = "fluentcontrol.monitor-profile";
    public int Version { get; set; } = 1;
}
public sealed class SharedProfileBundle
{
    public string Schema { get; set; } = "fluentcontrol.monitor-bundle";
    public int Version { get; set; } = 2;
    public string Name { get; set; } = "";
    public List<SharedProfileGroup> Groups { get; set; } = new();
}
public sealed class SharedProfileGroup
{
    public string Name { get; set; } = "";
    public List<SharedScene> Profiles { get; set; } = new();
}
public sealed class SharedMonitorSlot
{
    public string Slot { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Brand { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public MonitorHardwareInfo? Hardware { get; set; }
    public Dictionary<string, double> Values { get; set; } = new();
    public BrightnessMapping? Brightness { get; set; }
}
public static class ProfileExchange
{
    public const int MaxBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 16, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
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
        ValidateScene(profile, false);
    }
    public static void ValidateScene(SharedScene profile, bool allowUnknownModel = true)
    {
        if (profile.Applications is null || profile.Applications.Count > 32 || profile.Applications.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80)) throw new InvalidDataException("Invalid application metadata.");
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80 || profile.Monitors is null || profile.Monitors.Count is < 1 or > 16) throw new InvalidDataException("Invalid profile name or monitor count.");
        var slots = new HashSet<string>();
        foreach (var slot in profile.Monitors)
        {
            if (slot is null || string.IsNullOrWhiteSpace(slot.Slot) || slot.Slot.Length > 40 || !slots.Add(slot.Slot) || !(ModelIdentity.IsValid(slot.ModelId) || allowUnknownModel && slot.ModelId == "") || slot.Values is null || slot.Values.Count > 64) throw new InvalidDataException("Invalid monitor slot.");
            if (slot.ModelName is null || slot.DisplayName is null || slot.Brand is null || slot.ModelName.Length > 128 || slot.DisplayName.Length > 80 || slot.Brand.Length > 80) throw new InvalidDataException("Invalid display metadata.");
            slot.Brightness?.Validate();
            slot.Hardware?.Validate();
            foreach (var value in slot.Values)
            {
                if (!IsShareable(value.Key) || !double.IsFinite(value.Value) || value.Value < 0 || value.Value > 65535 ||
                    (VcpCatalog.Find(value.Key)?.Kind == VcpKind.Continuous && value.Value > 100)) throw new InvalidDataException("Unsupported profile control: " + value.Key);
            }
        }
    }
    public static SharedProfileBundle ParseBundle(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("Profile exceeds 1 MB.");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
        if (!document.RootElement.TryGetProperty("schema", out var schema)) throw new InvalidDataException("Missing profile schema.");
        if (schema.GetString() == "fluentcontrol.monitor-profile")
        {
            var old = Parse(text);
            return new() { Name = old.Name, Groups = new() { new() { Name = old.Name, Profiles = new() { new() { Name = old.Name, Applications = old.Applications, Monitors = old.Monitors } } } } };
        }
        var bundle = JsonSerializer.Deserialize<SharedProfileBundle>(text, Json) ?? throw new InvalidDataException("Empty bundle.");
        ValidateBundle(bundle); return bundle;
    }
    public static void ValidateBundle(SharedProfileBundle bundle)
    {
        if (bundle.Schema != "fluentcontrol.monitor-bundle" || bundle.Version != 2 || string.IsNullOrWhiteSpace(bundle.Name) || bundle.Name.Length > 80 || bundle.Groups is null || bundle.Groups.Count is < 1 or > 128) throw new InvalidDataException("Invalid bundle schema or groups.");
        var count = 0; var models = new Dictionary<string, string>();
        foreach (var group in bundle.Groups)
        {
            if (group is null || string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > 80 || group.Profiles is null || group.Profiles.Count == 0) throw new InvalidDataException("Invalid group.");
            foreach (var profile in group.Profiles)
            {
                if (profile is null || ++count > 512) throw new InvalidDataException("Too many profiles.");
                ValidateScene(profile);
                foreach (var slot in profile.Monitors)
                {
                    if (models.TryGetValue(slot.Slot, out var model) && model != slot.ModelId) throw new InvalidDataException("A slot cannot reference different models.");
                    models[slot.Slot] = slot.ModelId;
                }
            }
        }
    }
    public static string SerializeBundle(SharedProfileBundle bundle)
    {
        ValidateBundle(bundle); var text = JsonSerializer.Serialize(bundle, Json);
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("Profile exceeds 1 MB.");
        return text;
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
    Task<SharedProfileBundle> DownloadAsync(Uri uri, CancellationToken cancellationToken = default);
    Task UploadAsync(Uri endpoint, SharedProfileBundle profile, CancellationToken cancellationToken = default);
}
public sealed class HttpMonitorProfileExchange : IMonitorProfileExchange
{
    private static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0) throw new ArgumentException("An HTTPS URL without credentials is required.");
    }
    private static HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    public async Task<SharedProfileBundle> DownloadAsync(Uri uri, CancellationToken cancellationToken = default)
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
        return ProfileExchange.ParseBundle(Encoding.UTF8.GetString(output.ToArray()));
    }
    public async Task UploadAsync(Uri endpoint, SharedProfileBundle profile, CancellationToken cancellationToken = default)
    {
        ValidateUri(endpoint);
        using var client = Client();
        using var body = new StringContent(ProfileExchange.SerializeBundle(profile), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = body };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
