namespace FluentControl.Services;

public static class MonitorLinking
{
    // Unknown models stay independent; a generic friendly name is not a model ID.
    public static string GroupKey(string modelId, string deviceId) => ModelIdentity.IsValid(modelId) ? "model/" + modelId : "device/" + Uri.EscapeDataString(deviceId);
    public static ControlOption[]? Options(IEnumerable<ControlChannel> channels)
    {
        var choices = channels.Where(c => c.Options is not null).SelectMany(c => c.Options!).DistinctBy(x => x.Value).ToArray();
        return choices.Length == 0 ? null : choices;
    }
}
