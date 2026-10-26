using System.Text.Json;

// Closed operation set. Device capabilities can restrict it, never expand authority.
internal static class FcsNode
{
    public static bool ValidId(string? id) => id is { Length: > 0 and <= 100 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static readonly string[] Operations = { "node.info", "sensors.list", "operations.list",
        "data.read", "sampling.get", "sampling.set", "cache.read", "plugin.list" };
    public static readonly Dictionary<string, string> Routes = new()
    { ["node.info"] = "/v2/node", ["sensors.list"] = "/v2/sensors", ["operations.list"] = "/v2/operations",
      ["data.read"] = "/v2/data", ["sampling.get"] = "/v2/sampling" };
    public static JsonElement Arguments(string op, JsonElement args)
    {
        if (!Operations.Contains(op)) throw new InvalidDataException("Unsupported node operation.");
        if (args.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            args = JsonSerializer.SerializeToElement(new { });
        if (args.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid node arguments.");
        string[] allowed = op switch {
            "data.read" => new[] { "sensorId", "signalId", "mode" },
            "sampling.get" => new[] { "sensorId" },
            "sampling.set" => new[] { "sensorId", "enabled", "intervalMs" },
            "cache.read" => new[] { "sensorId", "signalId", "limit", "afterSequence" },
            _ => Array.Empty<string>() };
        var seen = new HashSet<string>();
        foreach (var p in args.EnumerateObject())
        {
            if (!seen.Add(p.Name) || !allowed.Contains(p.Name)) throw new InvalidDataException("Unknown or repeated argument.");
            bool valid = p.Name switch {
                "sensorId" => p.Value.ValueKind == JsonValueKind.String && ValidId(p.Value.GetString()),
                "signalId" => p.Value.ValueKind == JsonValueKind.String && ValidId(p.Value.GetString()) && p.Value.GetString()!.Length <= 64,
                "mode" => p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is "fresh" or "latest",
                "enabled" => p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "intervalMs" => p.Value.TryGetInt32(out var period) && period is >= 200 and <= 86400000,
                "limit" => p.Value.TryGetInt32(out var limit) && limit is >= 1 and <= 64,
                "afterSequence" => p.Value.TryGetInt64(out var after) && after >= -1,
                _ => false };
            if (!valid) throw new InvalidDataException("Invalid node argument.");
        }
        if (op == "sampling.set" && seen.Count != 3) throw new InvalidDataException("Missing sampling configuration.");
        return args;
    }
    public static JsonElement Validate(JsonElement frame, string id)
    {
        if (frame.GetProperty("protocol").GetString() != "fcs-node/2" ||
            frame.GetProperty("deviceId").GetString() != id ||
            frame.GetProperty("ok").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Unexpected node identity or envelope.");
        return frame;
    }
    public static void Require(JsonElement catalog, string operation, string transport)
    {
        if (!catalog.GetProperty("ok").GetBoolean() || !catalog.GetProperty("operations").EnumerateArray().Any(item =>
            item.GetProperty("id").GetString() == operation && item.GetProperty("state").GetString() == "available" &&
            item.GetProperty("access").GetString() == (operation == "sampling.set" ? "write" : "read") &&
            item.GetProperty("transports").EnumerateArray().Any(t => t.GetString() == transport)))
            throw new InvalidDataException("Node operation unavailable on this transport.");
    }
}
