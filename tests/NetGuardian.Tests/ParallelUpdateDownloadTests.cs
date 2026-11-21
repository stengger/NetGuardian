using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using NetGuardian.Core.Updates;
using Xunit;

namespace NetGuardian.Tests;

public sealed class ParallelUpdateDownloadTests : IDisposable
{
    private static readonly byte[] Package = CreatePackage();
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Package)).ToLowerInvariant();
    private const string Name = "NetGuardian-2.0.0-win-x64-Setup.exe";
    private const string Repository = "https://github.com/stengger/NetGuardian/releases";
    private static readonly UpdateRelease Release = new(new Version(2, 0, 0), "", new(Repository + "/tag/v2.0.0"),
        new(Name, new(Repository + "/download/v2.0.0/" + Name), Package.Length, "sha256:" + Hash),
        new(Name + ".sha256", new(Repository + "/download/v2.0.0/" + Name + ".sha256"), 100, null));
    private readonly string directory = Directory.CreateTempSubdirectory("NetGuardian-Ranges-").FullName;

    [Fact]
    public async Task 四个区段能同时读取且覆盖整包一次()
    {
        var ranges = new ConcurrentQueue<(long Start, long End)>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0;
        var progress = new ProgressLog();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = Client(range =>
        {
            if (range?.To is not long end) return Full();
            long start = range.From!.Value;
            ranges.Enqueue((start, end));
            return Slice(start, end, beforeRead: async token =>
            {
                if (Interlocked.Increment(ref active) == 4) gate.TrySetResult();
                await gate.Task.WaitAsync(token);
            });
        });
        var downloaded = await new GitHubUpdateService(client).DownloadAsync(Release, directory, progress, deadline.Token);
        Assert.Equal(4, active);
        long next = 0;
        foreach (var range in ranges.OrderBy(x => x.Start)) { Assert.Equal(next, range.Start); next = range.End + 1; }
        Assert.Equal(Package.Length, next);
        Assert.Equal(Package, await File.ReadAllBytesAsync(downloaded.InstallerPath));
        Assert.Equal(progress.Values.Order(), progress.Values);
        Assert.Equal(Package.Length, progress.Values.Last());
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.RequestedRangeNotSatisfiable)]
    public async Task 不支持分段时整包只传输一次(HttpStatusCode response)
    {
        var ranges = new List<RangeItemHeaderValue?>();
        using var client = Client(range =>
        {
            ranges.Add(range);
            return range?.To is not null && response != HttpStatusCode.OK ? new(response) : Full();
        });
        var downloaded = await new GitHubUpdateService(client).DownloadAsync(Release, directory, null, default);
        Assert.NotNull(ranges.First()?.To);
        Assert.Equal(response == HttpStatusCode.OK ? 1 : 2, ranges.Count);
        Assert.Equal(Package, await File.ReadAllBytesAsync(downloaded.InstallerPath));
    }

    [Fact]
    public async Task 断线重开只请求缺失的字节并能找回乱序完成的区段()
    {
        const int received = 1537;
        long firstEnd = -1;
        using var interrupted = Client(range =>
        {
            if (range?.To is not long end) throw new HttpRequestException("连接中断");
            long start = range.From!.Value;
            if (start == 0) { firstEnd = end; return Slice(start, end, payloadLength: received); }
            if (start == received) throw new HttpRequestException("连接中断");
            return Slice(start, end);
        });
        var service = new GitHubUpdateService(interrupted);
        var error = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(Release, directory, null, default));
        Assert.Equal(UpdateError.Network, error.Error);
        Assert.True(firstEnd > received);
        var saved = new UpdateProgress(Package.Length - (firstEnd + 1 - received), Package.Length);
        Assert.Equal(saved, error.SavedProgress);
        var requests = new ConcurrentQueue<(long?, long?)>();
        using var recovered = Client(range =>
        {
            requests.Enqueue((range?.From, range?.To));
            return range?.To is long end ? Slice(range.From!.Value, end) : Full();
        });
        var next = new GitHubUpdateService(recovered);
        foreach (var release in new[] { Release, Release with { Installer = Release.Installer with { Digest = null } } })
            Assert.Equal(saved, (await next.InspectCacheAsync(release, directory, default)).Partial);
        var result = await next.DownloadAsync(Release, directory, null, default);
        Assert.Equal(((long?)received, (long?)firstEnd), Assert.Single(requests));
        Assert.Equal(Package, await File.ReadAllBytesAsync(result.InstallerPath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 错位或损坏的分段不能交给安装器(bool corrupt)
    {
        using var client = Client(range =>
        {
            if (range?.To is not long end) return Full();
            long start = range.From!.Value;
            var slice = Slice(start, end, corrupt: corrupt && start > 0);
            if (!corrupt && start > 0) slice.Content.Headers.ContentRange = new(start - 1, end - 1, Package.Length);
            return slice;
        });
        var error = await Assert.ThrowsAsync<UpdateException>(() => new GitHubUpdateService(client).DownloadAsync(Release, directory, null, default));
        Assert.Equal(UpdateError.VerificationFailed, error.Error);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task 可从旧版本的连续分片中部开始并行下载()
    {
        int prefix = Package.Length * 3 / 8;
        await File.WriteAllBytesAsync(Path.Combine(directory, $"{Name}.{Hash}.part"), Package[..prefix]);
        var ranges = new ConcurrentQueue<(long Start, long? End)>();
        using var client = Client(range =>
        {
            if (range?.From is not long start) return Full();
            ranges.Enqueue((start, range.To));
            return Slice(start, range.To ?? Package.Length - 1);
        });
        var result = await new GitHubUpdateService(client).DownloadAsync(Release, directory, null, default);
        Assert.True(ranges.Count >= 2);
        long next = prefix;
        foreach (var range in ranges.OrderBy(x => x.Start)) { Assert.Equal(next, range.Start); next = range.End!.Value + 1; }
        Assert.Equal(Package.Length, next);
        Assert.Equal(Package, await File.ReadAllBytesAsync(result.InstallerPath));
    }

    [Fact]
    public async Task 缓存尾段过长时仍能检查更新并只重下坏段()
    {
        int boundary = Package.Length / 4;
        int middle = Package.Length / 2;
        int last = Package.Length * 3 / 4;
        var primary = Path.Combine(directory, $"{Name}.{Hash}.part");
        await File.WriteAllBytesAsync(primary, Package[..boundary]);
        await File.WriteAllBytesAsync(primary + ".2", new byte[last - middle + 128]);
        await File.WriteAllBytesAsync(primary + ".3", Package[last..]);
        var requests = new ConcurrentQueue<long>();
        using var client = Client(range =>
        {
            long start = range!.From!.Value;
            requests.Enqueue(start);
            return Slice(start, range.To!.Value);
        });
        var service = new GitHubUpdateService(client);
        Assert.Equal(new UpdateProgress(boundary + Package.Length - last, Package.Length), (await service.InspectCacheAsync(Release, directory, default)).Partial);
        var result = await service.DownloadAsync(Release, directory, null, default);
        Assert.Equal(Hash, result.Sha256);
        Assert.Equal(new long[] { boundary, middle }, requests.Order());
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task 后续分段被忽略时可退回单连接并验证整包()
    {
        var requests = new ConcurrentQueue<RangeItemHeaderValue?>();
        using var client = Client(range =>
        {
            requests.Enqueue(range);
            if (range?.From is long start && (start == 0 || range.To is null)) return Slice(start, range.To ?? Package.Length - 1);
            return Full();
        });
        var result = await new GitHubUpdateService(client).DownloadAsync(Release, directory, null, default);
        Assert.Contains(requests, r => r?.From > 0 && r.To.HasValue);
        Assert.Contains(requests, r => r?.To is null);
        Assert.Equal(Hash, result.Sha256);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task 分段被限流时保留限流原因与可恢复字节()
    {
        using var client = Client(range => range?.From == 0 && range.To.HasValue
            ? Slice(0, range.To.Value) : new(HttpStatusCode.TooManyRequests));
        var service = new GitHubUpdateService(client);
        var error = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(Release, directory, null, default));
        Assert.Equal(UpdateError.RateLimited, error.Error);
        Assert.False(File.Exists(Path.Combine(directory, Name)));
        long saved = (await service.InspectCacheAsync(Release, directory, default)).Partial?.Received ?? 0;
        Assert.Equal(saved, error.SavedProgress!.Received);
    }

    [Fact]
    public async Task 主动取消会等并行写入结束并清理临时文件()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int started = 0;
        using var client = Client(range =>
        {
            if (range?.From is not long start || range.To is not long end) return Full();
            return Slice(start, end, beforeRead: async token =>
            {
                if (Interlocked.Increment(ref started) == 4) await cancel.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitHubUpdateService(client).DownloadAsync(Release, directory, null, cancel.Token));
        Assert.Equal(4, started);
        Assert.Empty(Directory.GetFiles(directory));
    }

    private static HttpClient Client(Func<RangeItemHeaderValue?, HttpResponseMessage> respond) => new(new Handler(respond));

    private sealed class Handler(Func<RangeItemHeaderValue?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? new(HttpStatusCode.OK) { Content = new StringContent(Hash + "  " + Name) }
                : respond(request.Headers.Range?.Ranges.Single()));
    }

    private static byte[] CreatePackage()
    {
        var data = new byte[8 * 1024 * 1024 + 11];
        new Random(12345).NextBytes(data);
        return data;
    }

    private static HttpResponseMessage Full() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) };

    private static HttpResponseMessage Slice(long start, long end, int? payloadLength = null,
        Func<CancellationToken, Task>? beforeRead = null, bool corrupt = false)
    {
        int length = (int)(end - start + 1);
        var stream = new GatedStream(corrupt ? new byte[length] : Package, corrupt ? 0 : (int)start, payloadLength ?? length, beforeRead);
        var content = new StreamContent(stream);
        content.Headers.ContentLength = length;
        content.Headers.ContentRange = new(start, end, Package.Length);
        return new(HttpStatusCode.PartialContent) { Content = content };
    }

    private sealed class GatedStream(byte[] data, int offset, int length, Func<CancellationToken, Task>? beforeRead)
        : MemoryStream(data, offset, length, false)
    {
        private bool started;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (!started && beforeRead is not null) { started = true; await beforeRead(token); }
            return await base.ReadAsync(buffer, token);
        }
    }

    private sealed class ProgressLog : IProgress<UpdateProgress>
    {
        public ConcurrentQueue<long> Values { get; } = new();
        public void Report(UpdateProgress value) => Values.Enqueue(value.Received);
    }

    public void Dispose() => Directory.Delete(directory, true);
}
