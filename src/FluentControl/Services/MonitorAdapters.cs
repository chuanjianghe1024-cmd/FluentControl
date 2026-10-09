using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FluentControl.Services;

// Data-only packages; no assemblies, scripts, download URLs or expression evaluation.
public sealed class MonitorAdapter
{
    [JsonRequired]
    public string Format { get; set; } = "fluentcontrol.monitor-adapter";
    [JsonRequired]
    public int Version { get; set; } = 1;
    [JsonRequired]
    public string Id { get; set; } = "";
    [JsonRequired]
    public int Revision { get; set; }
    [JsonRequired]
    public string ModelId { get; set; } = "";
    [JsonRequired]
    public string ModelName { get; set; } = "";
    [JsonRequired]
    public List<string> FirmwareVersions { get; set; } = new();
    [JsonRequired]
    public string Notes { get; set; } = "";
    [JsonRequired]
    public string Evidence { get; set; } = "";
    [JsonRequired]
    public List<AdapterChoice> Controls { get; set; } = new();
    [JsonRequired]
    public List<AdapterMenuCommand> NativeMenu { get; set; } = new();
    public bool Matches(string modelId, string firmware) => ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(firmware) && FirmwareVersions.Contains(firmware, StringComparer.Ordinal);

    public void Validate()
    {
        static bool Text(string? s, int max) => s is not null && s.Length <= max && !s.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t');
        if (Format != "fluentcontrol.monitor-adapter" || Version != 1 || Id is null || !Regex.IsMatch(Id, @"\A[a-z0-9][a-z0-9-]{2,79}\z") || Revision is < 1 or > 1000000 ||
            !ModelIdentity.IsValid(ModelId ?? "") || ModelId != ModelId!.ToUpperInvariant() || !Text(ModelName, 120) || ModelName.Length == 0 ||
            FirmwareVersions is null || FirmwareVersions.Count is < 1 or > 32 || FirmwareVersions.Any(f => f is null || !Regex.IsMatch(f, @"\A[0-9]{1,5}\.[0-9]{1,5}\z")) || FirmwareVersions.Distinct().Count() != FirmwareVersions.Count ||
            !Text(Notes, 2000) || !Text(Evidence, 2000) || Evidence.Length < 10 || Controls is null || Controls.Count > 4 || NativeMenu is null || NativeMenu.Count > 7 || Controls.Count + NativeMenu.Count == 0)
            throw new InvalidDataException("Invalid monitor adapter.");
        var keys = new HashSet<string>();
        foreach (var control in Controls)
        {
            if (control is null || control.Key is not ("color-preset" or "display-mode" or "scaling" or "osd-language") || !keys.Add(control.Key) ||
                control.Options is null || control.Options.Count is < 1 or > 64 || control.Options.Select(o => o?.Value).Distinct().Count() != control.Options.Count ||
                control.Options.Any(o => o is null || o.Value > 255 || !Text(o.Label, 80) || o.Label.Length == 0))
                throw new InvalidDataException("Invalid adapter control options.");
        }
        keys.Clear();
        foreach (var command in NativeMenu)
            if (command is null || command.Action is not ("open" or "close" or "up" or "down" or "left" or "right" or "enter") || !keys.Add(command.Action) ||
                (command.Code != 0x03 && command.Code < 0xE0) || command.Value > 65535)
                throw new InvalidDataException("Invalid native menu command.");
    }
}
public sealed class AdapterChoice
{
    [JsonRequired]
    public string Key { get; set; } = "";
    [JsonRequired]
    public List<AdapterOption> Options { get; set; } = new();
}
public sealed class AdapterOption
{
    [JsonRequired]
    public uint Value { get; set; }
    [JsonRequired]
    public string Label { get; set; } = "";
}
public sealed class AdapterMenuCommand
{
    [JsonRequired]
    public string Action { get; set; } = "";
    [JsonRequired]
    public byte Code { get; set; }
    [JsonRequired]
    public uint Value { get; set; }
}
public sealed class MonitorAdapterStore
{
    public const int MaxBytes = 256 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string path;
    private List<MonitorAdapter> installed = new();
    public string? LoadError { get; }
    public MonitorAdapterStore(string? path = null)
    {
        this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "monitor-adapters.json");
        try
        {
            if (!File.Exists(this.path)) return;
            if (new FileInfo(this.path).Length > MaxBytes) throw new InvalidDataException("Adapter storage is too large.");
            installed = ParseList(File.ReadAllText(this.path));
        }
        catch (Exception ex) { installed = new(); LoadError = ex.Message; }
    }
    public bool HasModel(string modelId) => installed.Any(a => a.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
    public MonitorAdapter? Find(string modelId, string firmware) => installed.Where(a => a.Matches(modelId, firmware)).OrderByDescending(a => a.Revision).FirstOrDefault();
    public static MonitorAdapter Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("Adapter is too large.");
        var adapter = JsonSerializer.Deserialize<MonitorAdapter>(json, JsonOptions) ?? throw new InvalidDataException("Invalid adapter.");
        adapter.Validate(); return adapter;
    }
    public static List<MonitorAdapter> ParseList(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("Adapter list is too large.");
        var result = JsonSerializer.Deserialize<List<MonitorAdapter>>(json, JsonOptions) ?? throw new InvalidDataException("Invalid adapter list.");
        if (result.Count > 64) throw new InvalidDataException("Too many adapters.");
        foreach (var adapter in result) { if (adapter is null) throw new InvalidDataException("Invalid adapter."); adapter.Validate(); }
        if (result.Select(a => a.Id).Distinct().Count() != result.Count) throw new InvalidDataException("Duplicate adapter IDs.");
        return result;
    }
    public void Install(MonitorAdapter adapter, string modelId, string firmware)
    {
        adapter.Validate();
        if (!adapter.Matches(modelId, firmware)) throw new InvalidDataException("Adapter model or firmware does not match.");
        var next = installed.Where(a => a.Id != adapter.Id && !(a.ModelId == adapter.ModelId && a.FirmwareVersions.Intersect(adapter.FirmwareVersions).Any())).ToList();
        next.Add(adapter); Save(next);
    }
    public void Remove(string id) => Save(installed.Where(a => a.Id != id).ToList());
    private void Save(List<MonitorAdapter> next)
    {
        var json = JsonSerializer.Serialize(next, JsonOptions); ParseList(json);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, path, true); installed = next; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
public static class MonitorAdapterClient
{
    public static async Task<List<MonitorAdapter>> FindAsync(string modelId, string firmware, CancellationToken token)
    {
        if (!ModelIdentity.IsValid(modelId) || !Regex.IsMatch(firmware, @"\A[0-9]{1,5}\.[0-9]{1,5}\z"))
            throw new InvalidDataException(Strings.T("需要有效型号和固件版本才能匹配适配包。", "A valid model and firmware version are required to match adapters."));
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        using var response = await client.GetAsync("https://fctrl.app/api/v1/adapters?modelId=" + Uri.EscapeDataString(modelId) + "&firmware=" + Uri.EscapeDataString(firmware), HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException(Strings.T("适配服务暂不可用，请稍后再试。", "Adapter service unavailable. Please try again later."));
        if (response.Content.Headers.ContentLength > MonitorAdapterStore.MaxBytes) throw new InvalidDataException("Adapter list is too large.");
        using var stream = await response.Content.ReadAsStreamAsync(token); using var bytes = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (bytes.Length + count > MonitorAdapterStore.MaxBytes) throw new InvalidDataException("Adapter list is too large.");
            bytes.Write(buffer, 0, count);
        }
        var adapters = MonitorAdapterStore.ParseList(new UTF8Encoding(false, true).GetString(bytes.ToArray()));
        if (adapters.Any(a => !a.Matches(modelId, firmware))) throw new InvalidDataException("Server returned an incompatible adapter.");
        return adapters;
    }
}
