using FluentControl.Services;
using System.Net;
using System.Net.Http;
using System.Text;

internal static class AdaptivePresetTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or ArgumentOutOfRangeException) { return; }
        throw new Exception("Invalid adaptive input was accepted.");
    }
    public static async Task RunAsync()
    {
        var lux = AdaptivePresets.Default("ambient");
        Check(AdaptivePresets.Evaluate(lux, -3) == 10 && AdaptivePresets.Evaluate(lux, 20000) == 100, "Lux clamps to endpoints.");
        double midpoint = Math.Sqrt(11 * 101) - 1;
        Check(Math.Abs(AdaptivePresets.Evaluate(lux, midpoint) - 30) < .0001, "Lux interpolation must use log space.");
        var clock = AdaptivePresets.Default("clock");
        Check(AdaptivePresets.Evaluate(clock, 0) == AdaptivePresets.Evaluate(clock, 24), "Midnight must wrap smoothly.");
        Check(AdaptivePresets.Evaluate(clock, -1) == AdaptivePresets.Evaluate(clock, 23), "Negative time wraps correctly.");
        var copy = AdaptivePresets.Copy(lux); copy.Points[0].Brightness = 90;
        Check(lux.Points[0].Brightness == 10, "Editing a local copy must not mutate its subscription.");
        clock.Points[^1].Brightness++;
        Reject(() => AdaptivePresets.Validate(clock));
        Reject(() => AdaptivePresets.Evaluate(lux, double.NaN));
        Reject(() => AdaptivePresets.Parse(AdaptivePresets.Export(lux).Replace("\"version\": 1", "\"version\": 2")));
        Reject(() => AdaptivePresets.Parse(AdaptivePresets.Export(lux).Replace("\"name\":", "\"command\": \"run.exe\", \"name\":")));
        Reject(() => AdaptivePresets.Parse("null"));
        Reject(() => AdaptivePresets.Parse(new string(' ', AdaptivePresets.MaximumBytes + 1)));
        Reject(() => AdaptivePresets.SubscriptionUri("http://example.com/preset.json"));
        Reject(() => AdaptivePresets.SubscriptionUri("https://user:password@example.com/a"));
        Reject(() => AdaptivePresets.SubscriptionUri("file:///preset.json"));
        copy.Points[1].X = copy.Points[0].X;
        Reject(() => AdaptivePresets.Validate(copy));
        var settings = new AdaptiveSettings { PreviewEnabled = true, Curve = copy, Source = "script" };
        AdaptivePresets.Normalize(settings);
        Check(!settings.PreviewEnabled && settings.Source == "simulation", "Corrupt settings must reset to inactive preview.");
        using var handler = new Handler(AdaptivePresets.Export(lux)); using var client = new HttpClient(handler);
        var fetched = await AdaptivePresets.DownloadAsync(client, "https://example.com/preset.json", CancellationToken.None);
        Check(fetched.Points.Count == lux.Points.Count && handler.Requests == 1, "Subscription fetch parses one data document.");
        using var oversized = new HttpClient(new Handler(new string('x', AdaptivePresets.MaximumBytes + 1)));
        try { await AdaptivePresets.DownloadAsync(oversized, "https://example.com/big", CancellationToken.None); throw new Exception("Oversized response accepted"); }
        catch (InvalidDataException) { }
        using var redirect = new HttpClient(new Handler("", HttpStatusCode.Redirect));
        try { await AdaptivePresets.DownloadAsync(redirect, "https://example.com/redirect", CancellationToken.None); throw new Exception("Redirect accepted"); }
        catch (HttpRequestException) { }
        Console.WriteLine("Adaptive preset tests passed (curves, isolation, validation, bounded downloads).");
    }
    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
