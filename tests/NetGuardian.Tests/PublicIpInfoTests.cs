using System.Net;
using System.Text;
using NetGuardian.Core.Network;
using Xunit;

namespace NetGuardian.Tests;

public sealed class PublicIpInfoTests
{
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
