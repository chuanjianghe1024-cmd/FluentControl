using System.Text.Json.Serialization;
namespace FluentControl.Services;

// Pure data on inherited anonymous pipes; no network listener or executable payloads.
public sealed record DdcRequest(string Operation, int Monitor = 0, byte Code = 0, uint Value = 0,
    string Connection = "", string Identity = "");
public sealed record DdcResponse([property: JsonRequired] int Error = 0, [property: JsonRequired] uint Type = 0,
    [property: JsonRequired] uint Current = 0, [property: JsonRequired] uint Maximum = 0,
    [property: JsonRequired] string Text = "", [property: JsonRequired] string[]? Descriptions = null);

public static class DdcReadPolicy
{
    // Windows graphics transport errors, not unsupported VCP codes or disconnected displays.
    public static bool IsTransient(int error) => unchecked((uint)error) is
        0xC0262582 or 0xC0262583 or 0xC0262586 or 0xC0262589 or 0xC026258A or 0xC026258B;

    public static DdcResponse Read(Func<DdcResponse> read, byte code, Action<int>? delay = null)
    {
        // Event reads can consume a report. Pairing and diagnostics bypass this policy entirely.
        var limit = code is 0x02 or 0x03 or 0x52 ? 1 : 3;
        for (var attempt = 1; ; attempt++)
        {
            var reply = read();
            if (attempt >= limit || !IsTransient(reply.Error)) return reply;
            (delay ?? Thread.Sleep)(50);
        }
    }
}
