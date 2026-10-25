using System.Net;
using System.Text;
using NetGuardian.Core.Network;
using Xunit;

namespace NetGuardian.Tests;

public sealed class PublicIpInfoTests
{
    [Fact]
    public async Task 默认查询使用缓存而手动刷新更新出口地址()
    {
        var handler = new ChangingProviderHandler();
        using var http = new HttpClient(handler);
        var service = new PublicIpInfoService(http);
        Assert.Equal("203.0.113.10", (await service.GetAsync()).Ip);
        handler.Address = "203.0.113.20";
        Assert.Equal("203.0.113.10", (await service.GetAsync()).Ip);
        Assert.Equal("203.0.113.20", (await service.GetAsync(forceRefresh: true)).Ip);
        Assert.Equal("203.0.113.20", (await service.GetAsync()).Ip);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task 已取消的强制刷新不能返回旧缓存()
    {
        using var http = new HttpClient(new ChangingProviderHandler());
        var service = new PublicIpInfoService(http);
        await service.GetAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(cancellation.Token, forceRefresh: true));
    }

    private sealed class ChangingProviderHandler : HttpMessageHandler
    {
        public string Address { get; set; } = "203.0.113.10";
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"ip\":\"" + Address + "\"}", Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task 仅地址回退不能伪造零经纬度()
    {
        using var http = new HttpClient(new ProviderHandler("{\"success\":false}"));
        var info = await new PublicIpInfoService(http).GetAsync();
        Assert.Equal("203.0.113.10", info.Ip);
        Assert.Null(info.Latitude);
        Assert.Null(info.Longitude);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(",\"latitude\":0,\"longitude\":0", 0d)]
    public async Task 缺失坐标和真实零坐标保持区别(string coordinates, double? expected)
    {
        using var http = new HttpClient(new ProviderHandler("{\"success\":true,\"ip\":\"203.0.113.10\"" + coordinates + "}"));
        var info = await new PublicIpInfoService(http).GetAsync();
        Assert.Equal(expected, info.Latitude);
        Assert.Equal(expected, info.Longitude);
    }

    private sealed class ProviderHandler(string primary) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.Host == "ipwho.is" ? primary : "{\"ip\":\"203.0.113.10\"}", Encoding.UTF8, "application/json")
            });
    }
}
