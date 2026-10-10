using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentControl.Services;

internal sealed record ReleaseInstaller(string FileName, Uri DownloadUri, long Bytes, string Sha256);
internal sealed record ReleaseUpdate(Version Version, string Tag, Uri Page, ReleaseInstaller? Installer);
internal enum UpdateFailure { RateLimited, Network, InvalidRelease, Integrity }
internal sealed class UpdateException(UpdateFailure failure, Exception? inner = null) : Exception(failure.ToString(), inner)
{
    internal UpdateFailure Failure { get; } = failure;
}

// Manual operations only. No startup request, credentials, device data or routing changes.
internal sealed class ReleaseUpdates(HttpClient client)
{
    internal const string Repository = "https://github.com/chuanjianghe1024-cmd/FluentControl";
    internal const string LatestApi = "https://api.github.com/repos/chuanjianghe1024-cmd/FluentControl/releases/latest";
    private const long MaxInstallerBytes = 200 * 1024 * 1024;
    internal static Version CurrentVersion => Normalize(typeof(ReleaseUpdates).Assembly.GetName().Version ?? new Version(0, 0, 0));
    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    internal static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);
    internal static bool TryVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        return value is not null && Regex.IsMatch(value, @"^v?\d+\.\d+\.\d+(\.\d+)?$") && Version.TryParse(value.TrimStart('v'), out version!);
    }
    private static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("FluentControl/" + CurrentVersion.ToString(3));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        return request;
    }
    internal async Task<ReleaseUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var request = Request(new Uri(LatestApi));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) throw new UpdateException(UpdateFailure.RateLimited);
            response.EnsureSuccessStatusCode();
            using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (body.Length + count > 512 * 1024) throw new UpdateException(UpdateFailure.InvalidRelease);
                body.Write(buffer, 0, count);
            }
            return Parse(body.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new UpdateException(UpdateFailure.Network); }
        catch (HttpRequestException ex) { throw new UpdateException(UpdateFailure.Network, ex); }
    }
    internal static ReleaseUpdate Parse(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString();
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean() || !TryVersion(tag, out var version))
                throw new UpdateException(UpdateFailure.InvalidRelease);
            var page = Repository + "/releases/tag/" + tag;
            if (root.GetProperty("html_url").GetString() != page) throw new UpdateException(UpdateFailure.InvalidRelease);
            var name = "FluentControl-" + version.ToString(version.Revision < 0 ? 3 : 4) + "-x64.msi";
            var url = Repository + "/releases/download/" + tag + "/" + name;
            ReleaseInstaller? installer = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != name) continue;
                if (installer is not null) throw new UpdateException(UpdateFailure.InvalidRelease);
                var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
                var bytes = asset.GetProperty("size").GetInt64();
                if (asset.GetProperty("state").GetString() == "uploaded" && asset.GetProperty("browser_download_url").GetString() == url &&
                    bytes is > 0 and <= MaxInstallerBytes && digest is not null && Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$"))
                    installer = new(name, new Uri(url), bytes, digest[7..].ToLowerInvariant());
            }
            return new(version, tag!, new Uri(page), installer);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new UpdateException(UpdateFailure.InvalidRelease, ex); }
    }
    internal async Task<string> DownloadAsync(ReleaseUpdate release, string directory, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var installer = release.Installer ?? throw new UpdateException(UpdateFailure.InvalidRelease);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, installer.FileName);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".download");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            using var request = Request(installer.DownloadUri);
            request.Headers.Accept.Clear(); request.Headers.Accept.ParseAdd("application/octet-stream");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != installer.Bytes) throw new UpdateException(UpdateFailure.Integrity);
            using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0; var buffer = new byte[65536]; int count;
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, true))
            {
                while ((count = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > installer.Bytes || total > MaxInstallerBytes) throw new UpdateException(UpdateFailure.Integrity);
                    hash.AppendData(buffer, 0, count);
                    await destination.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    progress?.Report(100d * total / installer.Bytes);
                }
            }
            if (total != installer.Bytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(installer.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException(UpdateFailure.Integrity);
            timeout.Token.ThrowIfCancellationRequested();
            File.Move(temporary, target, true);
            return target;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new UpdateException(UpdateFailure.Network); }
        catch (HttpRequestException ex) { throw new UpdateException(UpdateFailure.Network, ex); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static async Task VerifyAsync(string path, ReleaseInstaller installer, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (stream.Length != installer.Bytes || !Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).Equals(installer.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException(UpdateFailure.Integrity);
    }
}
