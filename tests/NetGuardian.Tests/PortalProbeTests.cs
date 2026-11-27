using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.Tests;

public sealed class PortalProbeTests
{
    [Theory]
    [InlineData(200, "text/plain", null, PortalEvidence.None)]
    [InlineData(200, "text/html", null, PortalEvidence.Html)]
    [InlineData(200, "application/json", null, PortalEvidence.None)]
    [InlineData(302, null, "https://portal.example/login?session=secret", PortalEvidence.Redirect)]
    [InlineData(302, null, "/login?session=secret", PortalEvidence.Redirect)]
    [InlineData(302, null, "http://www.msftconnecttest.com/connecttest.txt#same", PortalEvidence.None)]
    [InlineData(302, null, "https://user@portal.example/login", PortalEvidence.None)]
    [InlineData(302, null, "file:///example", PortalEvidence.None)]
    [InlineData(302, null, null, PortalEvidence.None)]
    [InlineData(503, "text/html", null, PortalEvidence.None)]
    [InlineData(511, null, null, PortalEvidence.AuthenticationRequired)]
    public async Task 仅检查响应头且不请求跳转目标(int status, string? contentType, string? location, PortalEvidence expected)
    {
        using var handler = new ProbeHandler(status, contentType, location);
        using var client = new HttpClient(handler);
        Assert.Equal(expected, await PortalProbe.InspectAsync(client, CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task 发现重定向入口时保留内存地址并生成脱敏路径()
    {
        using var handler = new ProbeHandler(302, null, "https://portal.example:8443/eportal/index.jsp?wlanuserip=10.1.2.3&token=secret#frag");
        using var client = new HttpClient(handler);

        var result = await PortalProbe.DiscoverAsync(client, CancellationToken.None);

        Assert.Equal(PortalEvidence.Redirect, result.Evidence);
        Assert.Equal("https://portal.example:8443/eportal/index.jsp?wlanuserip=10.1.2.3&token=secret#frag", result.EntryUri?.AbsoluteUri);
        Assert.Equal("https://portal.example:8443/eportal/index.jsp", result.SanitizedEntry);
    }

    [Theory]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://portal.example/login?s=123\">", "https://portal.example/login")]
    [InlineData("<script>window.location.href='https://portal.example/auth?sid=abc';</script>", "https://portal.example/auth")]
    [InlineData("<script>location.replace(\"https://portal.example/portal?k=1\")</script>", "https://portal.example/portal")]
    public async Task 发现替换HTML中的安全重定向入口(string html, string expectedSanitized)
    {
        using var handler = new ProbeHandler(200, "text/html", null, html);
        using var client = new HttpClient(handler);

        var result = await PortalProbe.DiscoverAsync(client, CancellationToken.None);

        Assert.Equal(PortalEvidence.Html, result.Evidence);
        Assert.NotNull(result.EntryUri);
        Assert.Equal(expectedSanitized, result.SanitizedEntry);
    }

    [Theory]
    [InlineData("<script>location.href='javascript:alert(1)'</script>")]
    [InlineData("<meta http-equiv='refresh' content='0;url=file:///c:/windows/win.ini'>")]
    [InlineData("<script>location.href='https://user:pass@portal.example/login'</script>")]
    public async Task 拒绝HTML中的非HTTP或含凭据跳转地址(string html)
    {
        using var handler = new ProbeHandler(200, "text/html", null, html);
        using var client = new HttpClient(handler);

        var result = await PortalProbe.DiscoverAsync(client, CancellationToken.None);

        Assert.Equal(PortalEvidence.Html, result.Evidence);
        Assert.Null(result.EntryUri);
        Assert.Null(result.SanitizedEntry);
    }

    [Fact]
    public async Task 发现阶段HTML响应正文有64KiB上限保护()
    {
        var largePrefix = new string(' ', 70 * 1024);
        var html = largePrefix + "<script>location.href='https://portal.example/late'</script>";
        using var handler = new ProbeHandler(200, "text/html", null, html);
        using var client = new HttpClient(handler);

        var result = await PortalProbe.DiscoverAsync(client, CancellationToken.None);

        Assert.Equal(PortalEvidence.Html, result.Evidence);
        Assert.Null(result.EntryUri);
    }

    [Fact]
    public void 报告序列化不得包含完整门户查询参数()
    {
        var entry = new Uri("https://portal.example/login?token=super-secret-value&wlanuserip=10.1.1.2");
        var report = new ConnectivityReport
        {
            State = NetworkState.InternetIssue,
            PortalEvidence = PortalEvidence.Redirect,
            PortalEntryUri = entry,
            PortalSanitizedOrigin = PortalDiscoveryResult.SanitizeUri(entry),
            WifiInterfaceIndex = 12
        };

        var json = JsonSerializer.Serialize(report);

        Assert.DoesNotContain("super-secret-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("wlanuserip", json, StringComparison.Ordinal);
        Assert.DoesNotContain("WifiInterfaceIndex", json, StringComparison.Ordinal);
        Assert.Contains("https://portal.example/login", json, StringComparison.Ordinal);
    }

    private sealed class ProbeHandler(int status, string? contentType, string? location, string? body = null) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(PortalProbe.Endpoint, request.RequestUri);
            Assert.Empty(request.Headers);
            HttpContent content = body is null ? new UnreadableContent() : new StringContent(body, Encoding.UTF8);
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = content };
            if (contentType is not null) response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            if (location is not null) response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        }
    }

    private sealed class UnreadableContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("门户线索探测不需要读取响应正文。");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
