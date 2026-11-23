using System.Net;
using System.Net.Http.Headers;
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

    private sealed class ProbeHandler(int status, string? contentType, string? location) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(PortalProbe.Endpoint, request.RequestUri);
            Assert.Empty(request.Headers);
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new UnreadableContent() };
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
