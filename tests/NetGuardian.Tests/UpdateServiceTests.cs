using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetGuardian.Core.Updates;
using Xunit;

namespace NetGuardian.Tests;

public sealed class UpdateServiceTests
{
    private static readonly byte[] Package = Encoding.UTF8.GetBytes("完整安装包内容");
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Package)).ToLowerInvariant();

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.1")]
    public async Task 相同或旧版本不提示更新(string version)
    {
        using var client = Client(version);
        Assert.Null(await new GitHubUpdateService(client).CheckAsync(new Version(1, 0, 1), default));
    }

    [Fact]
    public async Task 正式新版本返回安装包与发布说明()
    {
        using var client = Client();
        var release = await new GitHubUpdateService(client).CheckAsync(new Version(1, 0, 1), default);
        Assert.Equal(new Version(1, 2, 0), release!.Version);
        Assert.Equal("修复界面问题", release.Notes);
        Assert.Equal("NetGuardian-1.2.0-win-x64-Setup.exe", release.Installer.Name);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task 草稿和预发布不会提示安装(bool draft, bool prerelease)
    {
        using var client = Client(draft: draft, prerelease: prerelease);
        Assert.Null(await new GitHubUpdateService(client).CheckAsync(new Version(1, 0, 1), default));
    }

    [Theory]
    [InlineData("https://example.com/installer.exe", true)]
    [InlineData("http://github.com/stengger/NetGuardian/releases/download/v1.2.0/NetGuardian-1.2.0-win-x64-Setup.exe", true)]
    [InlineData(null, false)]
    public async Task 不可信附件来源或缺少校验附件时拒绝更新(string? installerUrl, bool checksum)
    {
        using var client = Client(installerUrl: installerUrl, checksum: checksum);
        await Assert.ThrowsAsync<UpdateException>(() => new GitHubUpdateService(client).CheckAsync(new Version(1, 0, 1), default));
    }

    [Fact]
    public async Task 下载并核对摘要后才生成完整安装包()
    {
        using var cache = new TemporaryDirectory();
        using var client = Client();
        var service = new GitHubUpdateService(client);
        var release = (await service.CheckAsync(new Version(1, 0, 1), default))!;
        var result = await service.DownloadAsync(release, cache.Path, null, default);
        Assert.Equal(Package, await File.ReadAllBytesAsync(result.InstallerPath));
        Assert.Equal(Hash, result.Sha256);
        Assert.Single(Directory.GetFiles(cache.Path));
    }

    [Theory]
    [InlineData("摘要不符")]
    [InlineData("长度不符")]
    [InlineData("校验文件不符")]
    public async Task 下载校验失败不留下完整安装包(string failure)
    {
        using var cache = new TemporaryDirectory();
        using var client = Client(failure: failure);
        var service = new GitHubUpdateService(client);
        var release = (await service.CheckAsync(new Version(1, 0, 1), default))!;
        await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(release, cache.Path, null, default));
        Assert.Empty(Directory.GetFiles(cache.Path));
    }

    [Fact]
    public async Task 取消下载清理临时文件且不会安装()
    {
        using var cache = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        using var client = Client(cancelDownload: cancellation);
        var service = new GitHubUpdateService(client);
        var release = (await service.CheckAsync(new Version(1, 0, 1), default))!;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(release, cache.Path, null, cancellation.Token));
        Assert.Empty(Directory.GetFiles(cache.Path));
    }

    [Fact]
    public async Task 限流错误保留明确的错误类型()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var error = await Assert.ThrowsAsync<UpdateException>(() => new GitHubUpdateService(client).CheckAsync(new Version(1, 0, 1), default));
        Assert.Equal(UpdateError.RateLimited, error.Error);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task 安装包缓存按摘要复用或替换(bool valid, int expectedRequests)
    {
        using var cache = new TemporaryDirectory();
        int requests = 0;
        using var client = Client(onInstaller: () => requests++);
        var service = new GitHubUpdateService(client);
        var release = (await service.CheckAsync(new Version(1, 0, 1), default))!;
        File.WriteAllBytes(System.IO.Path.Combine(cache.Path, release.Installer.Name), valid ? Package : new byte[Package.Length]);
        var result = await service.DownloadAsync(release, cache.Path, null, default);
        Assert.Equal(expectedRequests, requests);
        Assert.Equal(Package, File.ReadAllBytes(result.InstallerPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 连接中断后续传或接收完整响应(bool supportsRange)
    {
        using var cache = new TemporaryDirectory();
        var offsets = new List<long?>();
        using var client = Client(installerResponse: request =>
        {
            offsets.Add(request.Headers.Range?.Ranges.Single().From);
            if (offsets.Count == 1)
            {
                var partial = new ByteArrayContent(Package[..3]);
                partial.Headers.ContentLength = Package.Length;
                return new(HttpStatusCode.OK) { Content = partial };
            }
            if (!supportsRange) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) };
            var rest = new ByteArrayContent(Package[3..]);
            rest.Headers.ContentRange = new(3, Package.Length - 1, Package.Length);
            return new(HttpStatusCode.PartialContent) { Content = rest };
        });
        var service = new GitHubUpdateService(client);
        var release = (await service.CheckAsync(new Version(1, 0, 1), default))!;
        var result = await service.DownloadAsync(release, cache.Path, null, default);
        Assert.Equal(new long?[] { null, 3 }, offsets);
        Assert.Equal(Package, File.ReadAllBytes(result.InstallerPath));
        Assert.Single(Directory.GetFiles(cache.Path));
    }

    [Fact]
    public async Task 三次断流后关闭下载器再打开时仅请求剩余字节()
    {
        using var cache = new TemporaryDirectory();
        var offsets = new List<long?>();
        using var failing = Client(installerResponse: request =>
        {
            offsets.Add(request.Headers.Range?.Ranges.Single().From);
            if (offsets.Count > 1) throw new HttpRequestException("连接中断");
            var first = new ByteArrayContent(Package[..3]);
            first.Headers.ContentLength = Package.Length;
            return new(HttpStatusCode.OK) { Content = first };
        });
        var firstService = new GitHubUpdateService(failing);
        var release = (await firstService.CheckAsync(new Version(1, 0, 1), default))!;
        var error = await Assert.ThrowsAsync<UpdateException>(() => firstService.DownloadAsync(release, cache.Path, null, default));
        Assert.Equal(UpdateError.Network, error.Error);
        Assert.Equal(new long?[] { null, 3, 3 }, offsets);
        Assert.Null(await firstService.FindCachedAsync(release, cache.Path, default));

        using var recovered = Client(installerResponse: request =>
        {
            offsets.Add(request.Headers.Range?.Ranges.Single().From);
            if (request.Headers.Range is null) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) };
            var rest = new ByteArrayContent(Package[3..]);
            rest.Headers.ContentRange = new(3, Package.Length - 1, Package.Length);
            return new(HttpStatusCode.PartialContent) { Content = rest };
        });
        var nextService = new GitHubUpdateService(recovered);
        Assert.Equal(new UpdateProgress(3, Package.Length), error.SavedProgress);
        foreach (var metadata in new[] { release, release with { Installer = release.Installer with { Digest = null } } })
        {
            var state = await nextService.InspectCacheAsync(metadata, cache.Path, default);
            Assert.Null(state.Downloaded);
            Assert.Equal(error.SavedProgress, state.Partial);
        }
        var progress = new ProgressLog();
        var result = await nextService.DownloadAsync(release, cache.Path, progress, default);
        Assert.Equal(new long?[] { null, 3, 3, 3 }, offsets);
        Assert.Equal(new UpdateProgress(3, Package.Length), progress.First());
        Assert.Equal(Package, await File.ReadAllBytesAsync(result.InstallerPath));
        Assert.Equal(Hash, result.Sha256);
        Assert.Single(Directory.GetFiles(cache.Path));
    }

    [Fact]
    public async Task 下载超时保留已有分片而主动取消清理分片()
    {
        using var cache = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var requests = 0;
        using var client = Client(installerResponse: request =>
        {
            requests++;
            if (request.Headers.Range is not null) throw new HttpRequestException("连接仍中断");
            var first = new ByteArrayContent(Package[..3]);
            first.Headers.ContentLength = Package.Length;
            return new(HttpStatusCode.OK) { Content = first };
        });
        client.Timeout = TimeSpan.FromMilliseconds(500);
        var service = new GitHubUpdateService(client);
        var release = (await service.CheckAsync(new Version(1, 0, 1), default))!;
        var timeout = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(release, cache.Path, null, default));
        Assert.Equal(1, requests);
        Assert.Equal(UpdateError.Network, timeout.Error);
        Assert.Equal(new UpdateProgress(3, Package.Length), timeout.SavedProgress);

        using var cancelledClient = Client(installerResponse: request =>
        {
            Assert.Equal(3L, request.Headers.Range?.Ranges.Single().From);
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitHubUpdateService(cancelledClient)
            .DownloadAsync(release, cache.Path, null, cancellation.Token));
        Assert.Empty(Directory.GetFiles(cache.Path));
    }

    [Fact]
    public async Task 同版本替换附件按新摘要从头下载()
    {
        using var cache = new TemporaryDirectory();
        var release = await SeedInterruptedDownloadAsync(cache.Path);
        var replacement = new byte[Package.Length];
        var digest = Convert.ToHexString(SHA256.HashData(replacement)).ToLowerInvariant();
        using var client = Client(assetHash: digest, installerResponse: request =>
        {
            Assert.Null(request.Headers.Range);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(replacement) };
        });
        var service = new GitHubUpdateService(client);
        var next = release with { Installer = release.Installer with { Digest = "sha256:" + digest } };
        Assert.Null((await service.InspectCacheAsync(next, cache.Path, default)).Partial);
        var result = await service.DownloadAsync(next, cache.Path, null, default);
        Assert.Equal(digest, result.Sha256);
        Assert.Equal(replacement, await File.ReadAllBytesAsync(result.InstallerPath));
    }

    [Fact]
    public async Task 缓存分片损坏时整包校验拒绝并清理()
    {
        using var cache = new TemporaryDirectory();
        var release = await SeedInterruptedDownloadAsync(cache.Path);
        await File.WriteAllBytesAsync(Assert.Single(Directory.GetFiles(cache.Path)), new byte[3]);
        using var client = Client(installerResponse: request =>
        {
            Assert.Equal(3L, request.Headers.Range?.Ranges.Single().From);
            var rest = new ByteArrayContent(Package[3..]);
            rest.Headers.ContentRange = new(3, Package.Length - 1, Package.Length);
            return new(HttpStatusCode.PartialContent) { Content = rest };
        });
        var error = await Assert.ThrowsAsync<UpdateException>(() => new GitHubUpdateService(client).DownloadAsync(release, cache.Path, null, default));
        Assert.Equal(UpdateError.VerificationFailed, error.Error);
        Assert.Empty(Directory.GetFiles(cache.Path));
    }

    private static async Task<UpdateRelease> SeedInterruptedDownloadAsync(string directory)
    {
        using var client = Client(installerResponse: request =>
        {
            if (request.Headers.Range is not null) return new(HttpStatusCode.TooManyRequests);
            var first = new ByteArrayContent(Package[..3]);
            first.Headers.ContentLength = Package.Length;
            return new(HttpStatusCode.OK) { Content = first };
        });
        var service = new GitHubUpdateService(client);
        var release = (await service.CheckAsync(new Version(1, 0, 1), default))!;
        var error = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(release, directory, null, default));
        Assert.Equal(UpdateError.RateLimited, error.Error);
        Assert.Equal(new UpdateProgress(3, Package.Length), error.SavedProgress);
        return release;
    }

    private sealed class ProgressLog : List<UpdateProgress>, IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress value) => Add(value);
    }

    [Fact]
    public async Task 续传范围错位拒绝安装并清理临时文件()
    {
        using var cache = new TemporaryDirectory();
        using var client = Client(installerResponse: request =>
        {
            if (request.Headers.Range is null)
            {
                var first = new ByteArrayContent(Package[..3]);
                first.Headers.ContentLength = Package.Length;
                return new(HttpStatusCode.OK) { Content = first };
            }
            var wrong = new ByteArrayContent(Package[2..]);
            wrong.Headers.ContentRange = new(2, Package.Length - 1, Package.Length);
            return new(HttpStatusCode.PartialContent) { Content = wrong };
        });
        var service = new GitHubUpdateService(client);
        var release = (await service.CheckAsync(new Version(1, 0, 1), default))!;
        var error = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(release, cache.Path, null, default));
        Assert.Equal(UpdateError.VerificationFailed, error.Error);
        Assert.Empty(Directory.GetFiles(cache.Path));
    }

    [Fact]
    public async Task 持续传输失败仅尝试三次并清理临时文件()
    {
        using var cache = new TemporaryDirectory();
        int requests = 0;
        using var client = Client(installerResponse: _ => { requests++; throw new HttpRequestException("连接中断"); });
        var service = new GitHubUpdateService(client);
        var release = (await service.CheckAsync(new Version(1, 0, 1), default))!;
        var error = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(release, cache.Path, null, default));
        Assert.Equal(UpdateError.Network, error.Error);
        Assert.Equal(3, requests);
        Assert.Empty(Directory.GetFiles(cache.Path));
    }

    private static HttpClient Client(string version = "1.2.0", bool draft = false, bool prerelease = false,
        string? installerUrl = null, bool checksum = true, string? failure = null, CancellationTokenSource? cancelDownload = null, Action? onInstaller = null,
        Func<HttpRequestMessage, HttpResponseMessage>? installerResponse = null, string? assetHash = null)
    {
        assetHash ??= Hash;
        var name = $"NetGuardian-{version}-win-x64-Setup.exe";
        var download = $"https://github.com/stengger/NetGuardian/releases/download/v{version}/{name}";
        var assets = new List<object>
        {
            new { name, size = Package.Length, browser_download_url = installerUrl ?? download, digest = "sha256:" + assetHash }
        };
        if (checksum) assets.Add(new { name = name + ".sha256", size = 110, browser_download_url = download + ".sha256", digest = (string?)null });
        var json = JsonSerializer.Serialize(new
        {
            tag_name = "v" + version, draft, prerelease, body = "修复界面问题",
            html_url = "https://github.com/stengger/NetGuardian/releases/tag/v" + version, assets
        });
        return new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host == "api.github.com")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            if (request.RequestUri.AbsolutePath.EndsWith(".sha256"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(assetHash + "  " + (failure == "校验文件不符" ? "其他文件.exe" : name) + "\n")
                };
            if (installerResponse is not null) return installerResponse(request);
            onInstaller?.Invoke();
            cancelDownload?.Cancel();
            var content = new ByteArrayContent(failure == "摘要不符" ? new byte[Package.Length] : Package);
            if (failure == "长度不符") content.Headers.ContentLength = Package.Length + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NetGuardian-UpdateTests-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
