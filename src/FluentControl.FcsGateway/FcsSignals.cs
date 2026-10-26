using System.Text.Json;

internal sealed record FcsSignalDescriptor(string Id, string Unit, double? Minimum = null,
    double? Maximum = null, int? MinIntervalMs = null);
internal sealed record FcsSensorDescriptor(string Id, string Kind, FcsSignalDescriptor[] Signals);
internal sealed record FcsDeviceCapabilities(string DeviceId, string Model, FcsSensorDescriptor[] Sensors,
    string[] Endpoints, string Source);

internal static class FcsSignalCatalog
{
    private static string Id(JsonElement value, string key) =>
        value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String &&
        GatewayRegistry.ValidId(property.GetString()) ? property.GetString()! :
        throw new InvalidDataException("Invalid sensor or signal identifier.");

    private static string Text(JsonElement value, string key, int max = 64)
    {
        if (!value.TryGetProperty(key, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Missing capability property.");
        var text = property.GetString() ?? "";
        if (text.Length is < 1 or > 64 || text.Length > max || text.Any(char.IsControl))
            throw new InvalidDataException("Invalid capability property.");
        return text;
    }

    private static double? OptionalDouble(JsonElement value, string key)
    {
        if (!value.TryGetProperty(key, out var item)) return null;
        if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var number) || !double.IsFinite(number))
            throw new InvalidDataException("Invalid capability number.");
        return number;
    }

    private static FcsSignalDescriptor Signal(JsonElement signal)
    {
        if (signal.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid signal descriptor.");
        var id = Id(signal, "id");
        var unit = Text(signal, "unit", 24);
        var minimum = OptionalDouble(signal, "minimum");
        var maximum = OptionalDouble(signal, "maximum");
        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
            throw new InvalidDataException("Invalid signal range.");
        int? interval = null;
        if (signal.TryGetProperty("minIntervalMs", out var item))
        {
            if (!item.TryGetInt32(out int delay) || delay is < 0 or > 86400000)
                throw new InvalidDataException("Invalid signal interval.");
            interval = delay;
        }
        return new(id, unit, minimum, maximum, interval);
    }

    public static FcsDeviceCapabilities ParseLan(JsonElement frame, string expected)
    {
        if (frame.ValueKind != JsonValueKind.Object || Text(frame, "protocol") != "fc-device/1" ||
            Id(frame, "sourceId") != expected ||
            !frame.TryGetProperty("device", out var device) || Id(device, "id") != expected ||
            !frame.TryGetProperty("sensors", out var sensors) || sensors.ValueKind != JsonValueKind.Array ||
            sensors.GetArrayLength() is < 1 or > 16)
            throw new InvalidDataException("Invalid device capabilities.");
        var found = new List<FcsSensorDescriptor>();
        foreach (var sensor in sensors.EnumerateArray())
        {
            var id = Id(sensor, "id");
            var kind = Text(sensor, "kind");
            if (!sensor.TryGetProperty("signals", out var signals) || signals.ValueKind != JsonValueKind.Array ||
                signals.GetArrayLength() is < 1 or > 16)
                throw new InvalidDataException("Invalid device signals.");
            var parsed = signals.EnumerateArray().Select(Signal).ToArray();
            if (parsed.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != parsed.Length ||
                found.Any(s => s.Id == id))
                throw new InvalidDataException("Duplicate sensor or signal.");
            found.Add(new(id, kind, parsed));
        }
        return new(expected, Text(device, "model"), found.ToArray(), new[] { "lan" }, "lan-capabilities");
    }

    // Existing USB PING only announces flat signals, not sensors. Project them
    // explicitly as a legacy adapter until a firmware sensor inventory exists.
    public static FcsDeviceCapabilities ParseUsb(JsonElement frame, string expected)
    {
        if (frame.ValueKind != JsonValueKind.Object || Text(frame, "protocol") != "fc-light-usb/1" ||
            Id(frame, "sourceId") != expected || !frame.TryGetProperty("signals", out var signals) ||
            signals.ValueKind != JsonValueKind.Array || signals.GetArrayLength() is < 1 or > 16)
            throw new InvalidDataException("USB firmware does not declare signals.");
        var parsed = signals.EnumerateArray().Select(Signal).ToArray();
        if (parsed.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != parsed.Length)
            throw new InvalidDataException("Duplicate USB signals.");
        return new(expected, "usb-legacy", new[] {
            new FcsSensorDescriptor("ambient-01", "usb-legacy", parsed)
        }, new[] { "usb" }, "usb-hello-adapter");
    }

    public static bool Supports(FcsDeviceCapabilities catalog, string sensorId, string signalId) =>
        catalog.Sensors.Any(s => s.Id == sensorId && s.Signals.Any(x => x.Id == signalId));
}
