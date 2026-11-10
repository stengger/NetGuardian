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

    private static HttpClient Client(string version = "1.2.0", bool draft = false, bool prerelease = false,
        string? installerUrl = null, bool checksum = true, string? failure = null, CancellationTokenSource? cancelDownload = null)
    {
        var name = $"NetGuardian-{version}-win-x64-Setup.exe";
        var download = $"https://github.com/stengger/NetGuardian/releases/download/v{version}/{name}";
        var assets = new List<object>
        {
            new { name, size = Package.Length, browser_download_url = installerUrl ?? download, digest = "sha256:" + Hash }
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
                    Content = new StringContent(Hash + "  " + (failure == "校验文件不符" ? "其他文件.exe" : name) + "\n")
                };
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
