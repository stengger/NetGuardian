using System.Net;
using NetGuardian.Core.Network;
using Xunit;

namespace NetGuardian.Tests;

public sealed class NetworkSpeedTestTests
{
    [Fact]
    public async Task 每个阶段先报告开始再报告完成()
    {
        using var client = new HttpClient(new ResponseHandler());
        var events = new List<NetworkSpeedTestProgress>();
        var result = await new NetworkSpeedTestService(client).RunAsync(new InlineProgress(events.Add));
        Assert.Equal(new[] { "Latency", "Download", "Upload" }, events.Where(e => !e.IsPhaseComplete && e.Mbps is null).Select(e => e.Phase));
        Assert.Equal(new[] { "Latency", "Download", "Upload" }, events.Where(e => e.IsPhaseComplete).Select(e => e.Phase));
        Assert.Equal(5_000_000, result.DownloadBytes);
        Assert.Equal(1_000_000, result.UploadBytes);
        Assert.True(result.DownloadMbps > 0);
        Assert.True(result.UploadMbps > 0);
        Assert.Equal(2, result.LatencySampleCount);
    }

    [Fact]
    public async Task 响应测量取消后不会继续下载()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new ResponseHandler(onHead: () => cancellation.Cancel()));
        var events = new List<NetworkSpeedTestProgress>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NetworkSpeedTestService(client).RunAsync(new InlineProgress(events.Add), cancellation.Token));
        Assert.DoesNotContain(events, e => e.Phase == "Download");
    }

    [Fact]
    public async Task 截断下载不能报告测速成功()
    {
        using var client = new HttpClient(new ResponseHandler(downloadLength: 64));
        var events = new List<NetworkSpeedTestProgress>();
        await Assert.ThrowsAsync<IOException>(() => new NetworkSpeedTestService(client).RunAsync(new InlineProgress(events.Add)));
        Assert.DoesNotContain(events, e => e.Phase == "Download" && e.IsPhaseComplete);
        Assert.DoesNotContain(events, e => e.Phase == "Upload");
    }

    [Fact]
    public async Task 响应目标失败仍能测吞吐但不制造零延迟()
    {
        using var client = new HttpClient(new ResponseHandler(headFails: true));
        var result = await new NetworkSpeedTestService(client).RunAsync();
        Assert.Null(result.LatencyMs);
        Assert.Equal(0, result.LatencySampleCount);
        Assert.True(result.DownloadMbps > 0);
    }

    private sealed class InlineProgress(Action<NetworkSpeedTestProgress> report) : IProgress<NetworkSpeedTestProgress>
    {
        public void Report(NetworkSpeedTestProgress value) => report(value);
    }

    // 只替换外部 HTTP 传输，真实运行服务的阶段、计算和取消流程。
    private sealed class ResponseHandler(int downloadLength = 5_000_000, bool headFails = false, Action? onHead = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                onHead?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                if (headFails) throw new HttpRequestException("测试目标不可达");
            }
            if (request.Method == HttpMethod.Post)
                await request.Content!.CopyToAsync(Stream.Null, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(request.Method == HttpMethod.Get ? new byte[downloadLength] : [])
            };
        }
    }
}
