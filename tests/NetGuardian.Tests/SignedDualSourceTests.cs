using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using NetGuardian.Core.Updates;
using Xunit;

namespace NetGuardian.Tests;

public sealed class SignedDualSourceTests : IAsyncLifetime
{
    private readonly UpdateLoopbackServer main = new(), mirror = new();
    private readonly ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string root = Directory.CreateTempSubdirectory("NetGuardian-Signed-").FullName;
    private byte[] body = "本地测试安装包，不执行"u8.ToArray();
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    private string Downloads => Path.Combine(root, "downloads");
    private const string Name = "NetGuardian-2.0.1-win-x64-Setup.exe";
    private static readonly Version Current = new(2, 0, 0);
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await main.DisposeAsync(); await mirror.DisposeAsync(); signer.Dispose(); Directory.Delete(root, true);
    }

    private SignedUpdateService Service(bool brokenProxy = false, bool allowDirect = true)
    {
        var trust = new UpdateTrust(signer.ExportSubjectPublicKeyInfo(),
            new(new(main.Root, "stable.json"), main.Root), new(new(mirror.Root, "stable.json"), mirror.Root));
        return new(trust, new(() => root), () => allowDirect, () => now, useProxy => new HttpClientHandler
        {
            AllowAutoRedirect = false, UseProxy = useProxy && brokenProxy,
            Proxy = brokenProxy ? new WebProxy("http://127.0.0.1:1") { BypassProxyOnLocal = false } : null,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null &&
                (cert.Equals(main.Certificate) || cert.Equals(mirror.Certificate))
        });
    }

    private byte[] Manifest(long serial = 1, string? bad = null, DateTimeOffset? until = null)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 1, channel = bad == "渠道" ? "beta" : "stable", sequence = serial,
            expiresUtc = until ?? now.AddDays(1), version = bad == "版本" ? "02.0.1" : "2.0.1",
            arch = bad == "架构" ? "arm64" : "win-x64", file = bad == "文件名" ? "../x.exe" : Name,
            length = bad == "长度" ? 0 : body.Length, sha256 = bad == "摘要" ? "??" : Convert.ToHexString(SHA256.HashData(body)),
            notes = bad == "换包" ? "被替换的说明" : "本地发行说明"
        });
        var signature = signer.SignData(payload, HashAlgorithmName.SHA256);
        if (bad == "签名") signature[0] ^= 1;
        return JsonSerializer.SerializeToUtf8Bytes(new { payload, signature });
    }

    private sealed class LegacyRecorder : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("api.github.com", request.RequestUri?.Host);
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Theory]
    [InlineData("开发")][InlineData("签名")][InlineData("过期")][InlineData("离线")]
    public async Task 构建只选一次协议且签名客户端永不降级(string scenario)
    {
        var legacy = new LegacyRecorder();
        int legacyClients = 0;
        var trust = scenario == "开发" ? null : new UpdateTrust(signer.ExportSubjectPublicKeyInfo(),
            new(new(main.Root, "stable.json"), main.Root), new(new(mirror.Root, "stable.json"), mirror.Root));
        var envelope = Manifest(bad: scenario, until: scenario == "过期" ? now : now.AddDays(1));
        main.Respond = mirror.Respond = (_, _) => scenario == "离线" ? new(503, []) : new(200, envelope);
        using var build = UpdateClient.Create(trust, () => root, null, null, () => false, () => now,
            _ => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false,
                ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && (cert.Equals(main.Certificate) || cert.Equals(mirror.Certificate)) },
            () => { legacyClients++; return new HttpClient(legacy); });
        if (scenario == "开发")
        {
            Assert.Null(build.Signed);
            Assert.Null(await build.Service.CheckAsync(Current, default));
            Assert.Equal(1, legacyClients);
            Assert.Equal(1, legacy.Calls);
            Assert.Empty(main.Requests); Assert.Empty(mirror.Requests);
        }
        else
        {
            Assert.NotNull(build.Signed);
            var error = await Assert.ThrowsAsync<UpdateException>(() => build.Service.CheckAsync(Current, default));
            Assert.Equal(scenario switch { "过期" => UpdateError.Expired, "离线" => UpdateError.Network, _ => UpdateError.VerificationFailed }, error.Error);
            Assert.Equal(0, legacyClients);
            Assert.Equal(0, legacy.Calls);
        }
    }

    [Theory]
    [InlineData("签名")][InlineData("渠道")][InlineData("架构")][InlineData("版本")][InlineData("文件名")][InlineData("长度")][InlineData("摘要")]
    public async Task 无效签名或字段在访问其他线路前终止(string bad)
    {
        main.Respond = (_, _) => new(200, Manifest(bad: bad));
        using var service = Service();
        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.CheckAsync(Current, default));
        Assert.Equal(UpdateError.VerificationFailed, ex.Error);
        Assert.Empty(mirror.Requests);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task 系统代理失效只按授权尝试备用直连(bool allowDirect)
    {
        var envelope = Manifest();
        mirror.Respond = (path, _) => new(200, path == "/stable.json" ? envelope : body);
        using var service = Service(true, allowDirect);
        if (allowDirect)
        {
            var release = await service.CheckAsync(Current, default);
            var download = await service.DownloadAsync(release!, Downloads, null, default);
            Assert.Equal(body, await File.ReadAllBytesAsync(download.InstallerPath));
            Assert.Equal(UpdateRoute.MirrorDirect, service.Status.Route);
            Assert.Single(mirror.Requests, x => x.Path.EndsWith(".exe"));
        }
        else
        {
            await Assert.ThrowsAsync<UpdateException>(() => service.CheckAsync(Current, default));
            Assert.Empty(mirror.Requests);
            Assert.Equal(2, service.Status.Failures.Count);
        }
        Assert.Empty(main.Requests);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task 主源断流备用源先验签再按范围或整包恢复(bool rangeSupported)
    {
        var envelope = Manifest();
        main.Respond = (path, range) => path == "/stable.json" ? new(200, envelope) :
            range is null ? new(200, body[..5], body.Length) : new(503, []);
        mirror.Respond = (path, range) => path == "/stable.json" ? new(200, envelope) :
            rangeSupported && range == "bytes=5-" ? new(206, body[5..], ExtraHeader: $"Content-Range: bytes 5-{body.Length - 1}/{body.Length}\r\n") : new(200, body);
        using var service = Service();
        var release = (await service.CheckAsync(Current, default))!;
        var result = await service.DownloadAsync(release, Downloads, null, default);
        Assert.Equal(body, await File.ReadAllBytesAsync(result.InstallerPath));
        Assert.Equal(new[] { ("/stable.json", (string?)null), ($"/v2.0.1/{Name}", (string?)"bytes=5-") }, mirror.Requests.ToArray());
        Assert.Equal(UpdateRoute.MirrorSystem, service.Status.Route);
        Assert.NotNull((await service.InspectCacheAsync(release, Downloads, default)).Downloaded);
    }

    [Theory]
    [InlineData(1, null)][InlineData(2, "换包")]
    public async Task 重开客户端拒绝回退和同序号替换(int serial, string? bad)
    {
        main.Respond = (_, _) => new(200, Manifest(2));
        using (var service = Service()) Assert.NotNull(await service.CheckAsync(Current, default));
        main.Respond = (_, _) => new(200, Manifest(serial, bad));
        using var next = Service();
        var ex = await Assert.ThrowsAsync<UpdateException>(() => next.CheckAsync(Current, default));
        Assert.Equal(UpdateError.VerificationFailed, ex.Error);
    }

    [Theory]
    [InlineData("不同清单")][InlineData("未同步")][InlineData("安装包摘要")][InlineData("越权跳转")]
    public async Task 备用源须提供相同签名版本及整包才能消费断点(string scenario)
    {
        var envelope = Manifest(2);
        main.Respond = (path, _) => path == "/stable.json" ? new(200, envelope) : new(200, body[..5], body.Length);
        var other = scenario switch { "不同清单" => Manifest(2, "换包"), "未同步" => Manifest(1), _ => envelope };
        mirror.Respond = (path, _) => path == "/stable.json" ? new(200, other) : scenario == "越权跳转"
            ? new(302, [], ExtraHeader: $"Location: {main.Root}unapproved\r\n") : new(200, new byte[body.Length]);
        using var service = Service(allowDirect: false);
        var release = (await service.CheckAsync(Current, default))!;
        var failure = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(release, Downloads, null, default));
        Assert.False(File.Exists(Path.Combine(Downloads, Name)));
        Assert.DoesNotContain(main.Requests, r => r.Path == "/unapproved");
        if (scenario == "未同步")
        {
            Assert.Equal(UpdateError.Network, failure.Error);
            Assert.Equal(5, failure.SavedProgress!.Received);
            Assert.Contains(service.Status.Failures, f => f.Reason.Contains("未同步"));
        }
        else Assert.Equal(UpdateError.VerificationFailed, failure.Error);
        Assert.Equal(scenario is "越权跳转" or "安装包摘要", mirror.Requests.Any(r => r.Path.EndsWith(".exe")));
    }

    [Fact]
    public async Task 同版本较高序号也不能把同名包换成另一摘要()
    {
        var first = Manifest(3);
        main.Respond = (_, _) => new(200, first);
        using var service = Service();
        Assert.NotNull(await service.CheckAsync(Current, default));
        body[0] ^= 1;
        main.Respond = (_, _) => new(200, Manifest(4));
        var failure = await Assert.ThrowsAsync<UpdateException>(() => service.CheckAsync(Current, default));
        Assert.Equal(UpdateError.VerificationFailed, failure.Error);
        Assert.Empty(mirror.Requests);
    }

    [Fact]
    public async Task 双进程模拟中的旧选择不能越过新记录进入安装()
    {
        var first = Manifest(1);
        main.Respond = (_, _) => new(200, first);
        using var firstClient = Service();
        var selected = (await firstClient.CheckAsync(Current, default))!;
        var newer = Manifest(2);
        using var second = Service();
        main.Respond = (_, _) => new(200, newer);
        Assert.NotNull(await second.CheckAsync(Current, default));
        Assert.Throws<UpdateException>(() => firstClient.ValidateForInstall(selected));
    }

    [Fact]
    public async Task 大包跨源保留三个完整分段并只补首段缺口()
    {
        body = new byte[8 * 1024 * 1024 + 4];
        new Random(20).NextBytes(body);
        var envelope = Manifest();
        UpdateLoopbackServer.Reply Serve(string path, string? range, bool interrupt)
        {
            if (path == "/stable.json") return new(200, envelope);
            if (range is null) return new(200, body);
            var boundaries = range[6..].Split('-');
            int start = int.Parse(boundaries[0]);
            int end = boundaries[1].Length == 0 ? body.Length - 1 : int.Parse(boundaries[1]);
            int count = end - start + 1;
            return new(206, body.AsSpan(start, interrupt && start == 0 ? 5000 : count).ToArray(), count,
                $"Content-Range: bytes {start}-{end}/{body.Length}\r\n");
        }
        main.Respond = (p, r) => Serve(p, r, true);
        mirror.Respond = (p, r) => Serve(p, r, false);
        using var service = Service();
        var release = (await service.CheckAsync(Current, default))!;
        var complete = await service.DownloadAsync(release, Downloads, null, default);
        Assert.Equal(4, main.Requests.Count(r => r.Range is not null));
        Assert.Equal($"bytes=5000-{body.Length / 4 - 1}", Assert.Single(mirror.Requests, r => r.Range is not null).Range);
        Assert.Equal(body, await File.ReadAllBytesAsync(complete.InstallerPath));
        using var reopened = Service();
        Assert.Equal(UpdateRoute.MirrorSystem, reopened.LastSuccessfulRoute()!.Route);
        Assert.Single(Directory.GetFiles(Downloads));
    }

    [Fact]
    public async Task 已缓存的包在清单过期后不能进入安装()
    {
        var envelope = Manifest(until: now.AddMinutes(1));
        main.Respond = (path, _) => new(200, path == "/stable.json" ? envelope : body);
        using var service = Service();
        var release = (await service.CheckAsync(Current, default))!;
        await service.DownloadAsync(release, Downloads, null, default);
        now = now.AddMinutes(2);
        Assert.Throws<UpdateException>(() => service.ValidateForInstall(release));
        await Assert.ThrowsAsync<UpdateException>(() => service.CheckAsync(Current, default));
        Assert.NotNull(service.Status.LastChecked);
    }
}
