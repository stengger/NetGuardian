using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetGuardian.Core.Updates;

public sealed class GitHubUpdateService(HttpClient client, Action<FileInfo>? validateCacheFile = null,
    Func<FileInfo, FileStream>? createPartial = null) : IUpdateService
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
        var name = ValidateAssets(release);
        Directory.CreateDirectory(directory);
        // ResponseHeadersRead 不限制正文耗时，单独计时才能区别慢连接超时与用户主动取消。
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(client.Timeout);
        var token = deadline.Token;
        try
        {
            var expected = await ExpectedHashAsync(release, name, token);
            var cached = await VerifyCacheAsync(release, directory, name, expected, token);
            if (cached is not null) return cached;

            var partial = PartialFile(directory, name, expected);
            // 持有独占读写权直到校验和改名结束，另一次下载不能修改或清除正在写入的分片。
            await using var output = InspectFile(partial)
                ? new FileStream(partial.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete, 81920, true)
                : createPartial?.Invoke(partial) ?? new FileStream(partial.FullName, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete, 81920, true);
            try
            {
                if (output.Length > release.Installer.Size) output.SetLength(0);
                progress?.Report(new UpdateProgress(output.Length, release.Installer.Size));
                await DownloadWithRetryAsync(release.Installer, output, progress, token);
                output.Position = 0;
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(output, token)).ToLowerInvariant();
                token.ThrowIfCancellationRequested();
                if (actual != expected) throw Verification("安装包完整性校验失败。");
                var destination = Path.Combine(directory, name);
                File.Move(partial.FullName, destination, true);
                return new DownloadedUpdate(release.Version, destination, actual);
            }
            catch (Exception ex) when (ex is HttpRequestException or UpdateException { Error: UpdateError.RateLimited }
                || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                var saved = new UpdateProgress(output.Length, release.Installer.Size);
                if (saved.Received == 0) partial.Delete();
                var error = ex is UpdateException failure ? failure.Error : UpdateError.Network;
                throw new UpdateException(error, "安装包下载中断。", ex) { SavedProgress = saved };
            }
            catch
            {
                partial.Delete();
                throw;
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        { throw new UpdateException(UpdateError.Network, "下载请求超时。", ex); }
        catch (HttpRequestException ex) { throw new UpdateException(UpdateError.Network, "下载安装包失败。", ex); }
    }

    private async Task DownloadWithRetryAsync(UpdateAsset asset, FileStream output, IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await DownloadAttemptAsync(asset, output, progress, token);
                return;
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts &&
                (ex.StatusCode is null or HttpStatusCode.RequestTimeout || (int)ex.StatusCode >= 500))
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), token);
            }
        }
    }

    private async Task DownloadAttemptAsync(UpdateAsset asset, FileStream output, IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        long offset = output.Length;
        if (offset == asset.Size) return;
        using var response = await SendAsync(asset.Url, token, offset);
        if (offset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            output.SetLength(0);
            progress?.Report(new UpdateProgress(0, asset.Size));
            throw new HttpRequestException("服务端拒绝续传，将重新请求完整安装包。");
        }
        EnsureSuccess(response);
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var range = response.Content.Headers.ContentRange;
            if (range?.Unit != "bytes" || range.From != offset || range.To != asset.Size - 1 || range.Length != asset.Size)
                throw Verification("续传响应范围与安装包不匹配。");
        }
        else if (response.StatusCode == HttpStatusCode.OK)
        {
            // 服务端忽略 Range 时，当前响应本身就是整包。
            if (offset > 0) progress?.Report(new UpdateProgress(0, asset.Size));
            output.SetLength(0);
            offset = 0;
        }
        else throw Verification("服务端未返回预期的安装包响应。");
        if (response.Content.Headers.ContentLength is long length && length != asset.Size - offset)
            throw Verification("下载长度与发布信息不一致。");
        output.Position = offset;
        var notifications = Stopwatch.StartNew();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[81920];
        while (true)
        {
            int count;
            try { count = await input.ReadAsync(buffer, token); }
            catch (IOException ex) { throw new HttpRequestException("读取安装包时连接中断。", ex); }
            if (count == 0) break;
            if (output.Position + count > asset.Size) throw Verification("下载内容超出预期长度。");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            if (notifications.ElapsedMilliseconds >= 100 || output.Position == asset.Size)
            {
                progress?.Report(new UpdateProgress(output.Position, asset.Size));
                notifications.Restart();
            }
        }
        if (output.Position != asset.Size) throw new HttpRequestException("连接在安装包下载完成前关闭。");
    }

    public async Task<DownloadedUpdate?> FindCachedAsync(UpdateRelease release, string directory, CancellationToken token)
    {
        var name = ValidateAssets(release);
        if (!File.Exists(Path.Combine(directory, name))) return null;
        var expected = await ExpectedHashAsync(release, name, token);
        return await VerifyCacheAsync(release, directory, name, expected, token);
    }

    public async Task<UpdateCacheState> InspectCacheAsync(UpdateRelease release, string directory, CancellationToken token)
    {
        var name = ValidateAssets(release);
        if (!Directory.Exists(directory)) return default;
        var complete = await FindCachedAsync(release, directory, token);
        if (complete is not null) return new(complete, null);

        string expected;
        if (release.Installer.Digest is { } digest && Regex.IsMatch(digest, @"\Asha256:[a-fA-F0-9]{64}\z"))
            expected = digest[7..].ToLowerInvariant();
        else
        {
            if (!Directory.EnumerateFiles(directory, name + ".*.part").Any()) return default;
            expected = await ExpectedHashAsync(release, name, token);
        }
        var part = PartialFile(directory, name, expected);
        if (!InspectFile(part) || part.Length <= 0 || part.Length > release.Installer.Size) return default;
        return new(null, new UpdateProgress(part.Length, release.Installer.Size));
    }

    private static FileInfo PartialFile(string directory, string name, string expected) =>
        new(Path.Combine(directory, $"{name}.{expected}.part"));

    private bool InspectFile(FileInfo file)
    {
        if (!file.Exists) return false;
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw Verification("更新缓存不能是重解析点。");
        validateCacheFile?.Invoke(file);
        return true;
    }

    private async Task<DownloadedUpdate?> VerifyCacheAsync(UpdateRelease release, string directory, string name, string expected, CancellationToken token)
    {
        var file = new FileInfo(Path.Combine(directory, name));
        if (!InspectFile(file) || file.Length != release.Installer.Size) return null;
        await using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase) ? new DownloadedUpdate(release.Version, file.FullName, expected) : null;
    }

    private static string ValidateAssets(UpdateRelease release)
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
        return name;
    }

    private async Task<string> ExpectedHashAsync(UpdateRelease release, string name, CancellationToken cancellationToken)
    {
        using var checksumResponse = await SendAsync(release.Checksum.Url, cancellationToken);
        EnsureSuccess(checksumResponse);
        var checksum = Encoding.UTF8.GetString(await ReadLimitedAsync(checksumResponse.Content, 4096, cancellationToken)).Trim().TrimStart('\uFEFF');
        var match = Regex.Match(checksum, @"\A([a-fA-F0-9]{64})[ \t]+\*?" + Regex.Escape(name) + @"\z");
        if (!match.Success) throw Verification("校验文件与安装包不匹配。");
        var expected = match.Groups[1].Value.ToLowerInvariant();
        if (release.Installer.Digest is not null && !string.Equals(release.Installer.Digest, "sha256:" + expected, StringComparison.OrdinalIgnoreCase))
            throw Verification("发布摘要与校验文件不一致。");
        return expected;
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token, long offset = 0)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (offset > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
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
