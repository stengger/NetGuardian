using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetGuardian.Core.Updates;

public sealed class GitHubUpdateService(HttpClient client) : IUpdateService
{
    private const string Repository = "https://github.com/stengger/NetGuardian";
    private const string Latest = "https://api.github.com/repos/stengger/NetGuardian/releases/latest";
    private const long MaximumInstallerSize = 1024L * 1024 * 1024;

    public async Task<UpdateRelease?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(new Uri(Latest), cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            EnsureSuccess(response);
            using var json = JsonDocument.Parse(await ReadLimitedAsync(response.Content, 2 * 1024 * 1024, cancellationToken));
            var root = json.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$") || !Version.TryParse(tag.TrimStart('v'), out var version))
                throw Invalid("发布版本格式无效。");
            if (version <= currentVersion) return null;
            var releaseUrl = ValidateUrl(root.GetProperty("html_url").GetString(), $"{Repository}/releases/tag/{tag}");
            var name = $"NetGuardian-{version}-win-x64-Setup.exe";
            UpdateAsset Asset(string expected)
            {
                var matches = root.GetProperty("assets").EnumerateArray().Where(x => x.GetProperty("name").GetString() == expected).ToArray();
                if (matches.Length != 1) throw Invalid("发布附件缺失或重复。");
                var item = matches[0];
                var size = item.GetProperty("size").GetInt64();
                if (size <= 0 || size > MaximumInstallerSize) throw Invalid("附件大小无效。");
                var digest = item.TryGetProperty("digest", out var value) ? value.GetString() : null;
                if (digest is not null && !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) throw Invalid("附件摘要格式无效。");
                return new UpdateAsset(expected, ValidateUrl(item.GetProperty("browser_download_url").GetString(),
                    $"{Repository}/releases/download/{tag}/{expected}"), size, digest);
            }
            return new UpdateRelease(version, root.GetProperty("body").GetString() ?? "", releaseUrl, Asset(name), Asset(name + ".sha256"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new UpdateException(UpdateError.InvalidRelease, "无法识别更新信息。", ex); }
        catch (HttpRequestException ex) { throw new UpdateException(UpdateError.Network, "无法连接更新服务。", ex); }
    }

    public async Task<DownloadedUpdate> DownloadAsync(UpdateRelease release, string directory,
        IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        // 即使调用者自行构造模型，也不能改变下载来源或输出文件名。
        var name = $"NetGuardian-{release.Version}-win-x64-Setup.exe";
        var tag = release.ReleaseUrl.AbsolutePath.Split('/').Last();
        ValidateUrl(release.ReleaseUrl.AbsoluteUri, $"{Repository}/releases/tag/{tag}");
        if (tag != "v" + release.Version && tag != release.Version.ToString()) throw Invalid("版本与发布地址不一致。");
        if (release.Installer.Name != name || release.Checksum.Name != name + ".sha256") throw Invalid("安装包名称无效。");
        ValidateUrl(release.Installer.Url.AbsoluteUri, $"{Repository}/releases/download/{tag}/{name}");
        ValidateUrl(release.Checksum.Url.AbsoluteUri, $"{Repository}/releases/download/{tag}/{name}.sha256");
        if (release.Installer.Size <= 0 || release.Installer.Size > MaximumInstallerSize) throw Invalid("安装包大小无效。");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using var checksumResponse = await SendAsync(release.Checksum.Url, cancellationToken);
            EnsureSuccess(checksumResponse);
            var checksum = Encoding.UTF8.GetString(await ReadLimitedAsync(checksumResponse.Content, 4096, cancellationToken)).Trim().TrimStart('\uFEFF');
            var match = Regex.Match(checksum, @"\A([a-fA-F0-9]{64})[ \t]+\*?" + Regex.Escape(name) + @"\z");
            if (!match.Success) throw Verification("校验文件与安装包不匹配。");
            var expected = match.Groups[1].Value.ToLowerInvariant();
            if (release.Installer.Digest is not null && !string.Equals(release.Installer.Digest, "sha256:" + expected, StringComparison.OrdinalIgnoreCase))
                throw Verification("发布摘要与校验文件不一致。");
            using var response = await SendAsync(release.Installer.Url, cancellationToken);
            EnsureSuccess(response);
            if (response.Content.Headers.ContentLength is long length && length != release.Installer.Size)
                throw Verification("下载长度与发布信息不一致。");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            var notifications = Stopwatch.StartNew();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    received += count;
                    if (received > release.Installer.Size) throw Verification("下载内容超出预期长度。");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    if (notifications.ElapsedMilliseconds >= 100 || received == release.Installer.Size)
                    {
                        progress?.Report(new UpdateProgress(received, release.Installer.Size));
                        notifications.Restart();
                    }
                }
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (received != release.Installer.Size || actual != expected) throw Verification("安装包完整性校验失败。");
            var destination = Path.Combine(directory, name);
            File.Move(temporary, destination, true);
            return new DownloadedUpdate(release.Version, destination, actual);
        }
        catch (HttpRequestException ex) { throw new UpdateException(UpdateError.Network, "下载安装包失败。", ex); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("NetGuardian/1.1.0");
        request.Headers.Accept.ParseAdd(uri.Host == "api.github.com" ? "application/vnd.github+json" : "application/octet-stream");
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new UpdateException(UpdateError.RateLimited, "更新服务访问受限，请稍后重试。");
        response.EnsureSuccessStatusCode();
    }

    private static Uri ValidateUrl(string? value, string expected)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.Equals(value, expected, StringComparison.Ordinal)
            || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw Invalid("更新附件来源不可信。");
        return uri;
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maximum, CancellationToken token)
    {
        if (content.Headers.ContentLength > maximum) throw Invalid("更新信息过大。");
        await using var input = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > maximum) throw Invalid("更新信息过大。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static UpdateException Invalid(string message) => new(UpdateError.InvalidRelease, message);
    private static UpdateException Verification(string message) => new(UpdateError.VerificationFailed, message);
}
