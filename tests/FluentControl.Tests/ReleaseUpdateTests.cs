using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentControl.Services;

internal static class ReleaseUpdateTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static byte[] Payload(string tag, byte[] bytes, string? url = null, string? digest = null, bool preview = false)
    {
        var name = "FluentControl-" + tag.TrimStart('v') + "-x64.msi";
        return JsonSerializer.SerializeToUtf8Bytes(new { tag_name = tag, html_url = ReleaseUpdates.Repository + "/releases/tag/" + tag,
            draft = false, prerelease = preview, assets = new[] { new { name, size = bytes.Length, state = "uploaded",
                digest = digest ?? "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)),
                browser_download_url = url ?? ReleaseUpdates.Repository + "/releases/download/" + tag + "/" + name } } });
    }
    private static async Task Fails(Func<Task> action, UpdateFailure failure)
    {
        try { await action(); throw new Exception("Expected update failure: " + failure); }
        catch (UpdateException ex) { Check(ex.Failure == failure, "Wrong update failure type."); }
    }
    internal static async Task RunAsync()
    {
        Check(ReleaseUpdates.IsNewer(new(0, 3, 100), new(0, 3, 99)), "Compare numeric release versions, not strings.");
        Check(!ReleaseUpdates.IsNewer(new(0, 3, 57), new(0, 3, 57, 0)), "Three and four-part versions must agree.");
        Check(!ReleaseUpdates.IsNewer(new(0, 3, 57), new(0, 3, 100)), "Never offer a downgrade from a development build.");
        Check(!ReleaseUpdates.TryVersion("v1.0.0-beta", out _) && !ReleaseUpdates.TryVersion("v1.0", out _) && !ReleaseUpdates.TryVersion("../../x", out _), "Reject prerelease/ambiguous/path tags.");
        var bytes = Encoding.UTF8.GetBytes("fixture installer; never executable");
        var payload = Payload("v0.3.100", bytes);
        var release = ReleaseUpdates.Parse(payload);
        Check(release.Installer is not null && release.Version == new Version(0, 3, 100), "Stable x64 asset selected.");
        Check(ReleaseUpdates.Parse(Payload("v0.3.100", bytes, url: "https://example.com/update.msi")).Installer is null, "Do not download from a different source.");
        Check(ReleaseUpdates.Parse(Payload("v0.3.100", bytes, digest: "sha256:invalid")).Installer is null, "Unverifiable installers cannot be used.");
        await Fails(() => Task.Run(() => ReleaseUpdates.Parse(Payload("v0.3.100", bytes, preview: true))), UpdateFailure.InvalidRelease);
        await Fails(() => Task.Run(() => ReleaseUpdates.Parse(Encoding.UTF8.GetBytes("{}"))), UpdateFailure.InvalidRelease);
        var requests = 0;
        using var handler = new Handler((request, _) =>
        {
            requests++;
            Check(request.Headers.UserAgent.Count > 0 && request.Headers.Authorization is null, "Anonymous requests need an identifying user agent.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.AbsoluteUri == ReleaseUpdates.LatestApi ? payload : bytes) });
        });
        using var client = new HttpClient(handler);
        var updates = new ReleaseUpdates(client);
        Check(requests == 0, "Constructing the update service must not access the network.");
        Check((await updates.CheckAsync(default))?.Tag == release.Tag && requests == 1, "Manual check uses the stable endpoint once.");
        var directory = Path.Combine(Path.GetTempPath(), "FC-update-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = await updates.DownloadAsync(release, directory, null, default);
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Download the selected installer exactly.");
            await ReleaseUpdates.VerifyAsync(path, release.Installer!, default);
            await File.WriteAllBytesAsync(path, bytes.Select(b => (byte)(b ^ 1)).ToArray());
            await Fails(() => ReleaseUpdates.VerifyAsync(path, release.Installer!, default), UpdateFailure.Integrity);
            File.Delete(path);
            using var corruptClient = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes.Select(b => (byte)(b ^ 1)).ToArray()) })));
            await Fails(() => new ReleaseUpdates(corruptClient).DownloadAsync(release, directory, null, default), UpdateFailure.Integrity);
            Check(!Directory.EnumerateFiles(directory).Any(), "Failed hash verification must leave no installer or partial download.");
            using var truncatedClient = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes[..2]) })));
            await Fails(() => new ReleaseUpdates(truncatedClient).DownloadAsync(release, directory, null, default), UpdateFailure.Integrity);
            Check(!Directory.EnumerateFiles(directory).Any(), "Truncated download must not leave a runnable file.");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await updates.DownloadAsync(release, directory, null, cancelled.Token); throw new Exception("Cancellation ignored."); }
            catch (OperationCanceledException) { }
            Check(!Directory.EnumerateFiles(directory).Any(), "Cancelled download must clean partial files.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.NotFound, HttpStatusCode.BadGateway })
        {
            using var statusClient = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status))));
            var service = new ReleaseUpdates(statusClient);
            if (status == HttpStatusCode.NotFound) Check(await service.CheckAsync(default) is null, "An empty release feed must be explicit.");
            else await Fails(async () => { await service.CheckAsync(default); }, status == HttpStatusCode.BadGateway ? UpdateFailure.Network : UpdateFailure.RateLimited);
        }
        Console.WriteLine("PASS: manual stable updates, numeric versions, source/digest validation, verified download, corruption, cancellation and HTTP failures.");
    }
}
