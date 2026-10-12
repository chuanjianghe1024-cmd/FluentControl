using System.Text.Json;

namespace FluentControl.Services;

// Persist only hashed connection identities and an interrupted advanced scan.
// Successful basic reads must not clear a previous advanced-scan failure.
public sealed class DdcProbeRecovery
{
    private readonly string directory;
    private readonly Action<string>? log;
    public DdcProbeRecovery(string directory, Action<string>? log = null) { this.directory = directory; this.log = log; }
    private string PathFor(string key) => Path.Combine(directory, key + ".json");
    public bool ShouldSkip(string key, bool force) => !force && File.Exists(PathFor(key));
    public void Begin(string key)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(PathFor(key), JsonSerializer.Serialize(new { operation = "advanced-discovery", at = DateTimeOffset.UtcNow }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log?.Invoke("Cannot save DDC recovery marker: " + ex.Message); }
    }
    public void Complete(string key)
    {
        try { File.Delete(PathFor(key)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log?.Invoke("Cannot clear DDC recovery marker: " + ex.Message); }
    }
}
