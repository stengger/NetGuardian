using System.Net;
using System.Text;
using NetGuardian.Core.Models;
using NetGuardian.Core.Portal;
using Xunit;

namespace NetGuardian.Tests;

public sealed class PortalForwarderTests
{
    private static PortalAuthConfig CreateConfig(bool allowHttp = false) => new()
    {
        Enabled = true,
        Ready = true,
        CredentialId = Guid.NewGuid().ToString("N"),
        AllowInsecureHttp = allowHttp,
        AllowedOrigin = "https://portal.example.edu",
        AllowedLoginPath = "/portal",
        AllowedSubmitPath = "/sign-in",
        UsernameSelector = "#user",
        PasswordSelector = "#pass",
        SubmitSelector = "#btn"
    };

    [Fact]
    public async Task 放行同源页面并拦截跨站和非法协议请求()
    {
        var handler = new RecordingHandler((req, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>ok</html>", Encoding.UTF8, "text/html")
        });
        using var client = new HttpClient(handler);
        var forwarder = new PortalHttpForwarder(CreateConfig(), client);

        var allowed = await forwarder.ForwardAsync(
            new PortalForwardedRequest("GET", new Uri("https://portal.example.edu/portal?s=1"), new Dictionary<string, string>(), null),
            CancellationToken.None);
        Assert.True(allowed.Allowed);
        Assert.Equal(200, allowed.StatusCode);

        var crossOrigin = await forwarder.ForwardAsync(
            new PortalForwardedRequest("GET", new Uri("https://evil.example/tracker.js"), new Dictionary<string, string>(), null),
            CancellationToken.None);
        Assert.False(crossOrigin.Allowed);

        var insecureHttp = await forwarder.ForwardAsync(
            new PortalForwardedRequest("GET", new Uri("http://portal.example.edu/portal"), new Dictionary<string, string>(), null),
            CancellationToken.None);
        Assert.False(insecureHttp.Allowed);

        var unapprovedPost = await forwarder.ForwardAsync(
            new PortalForwardedRequest("POST", new Uri("https://portal.example.edu/unapproved"), new Dictionary<string, string>(), "a=1"),
            CancellationToken.None);
        Assert.False(unapprovedPost.Allowed);
        Assert.True(forwarder.SubmitTargetBlocked);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task 表单占位符精确替换为含特殊字符和Unicode的真实凭据()
    {
        string? capturedBody = null;
        var handler = new RecordingHandler((req, body) =>
        {
            capturedBody = body;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("accepted", Encoding.UTF8, "text/plain") };
        });
        using var client = new HttpClient(handler);
        var markers = new Dictionary<string, string>
        {
            ["ng-user-111"] = "student@campus",
            ["ng-password-222"] = "p@ss&=+%中文😃"
        };
        var forwarder = new PortalHttpForwarder(CreateConfig(), client, markers);

        var response = await forwarder.ForwardAsync(
            new PortalForwardedRequest(
                "POST",
                new Uri("https://portal.example.edu/sign-in"),
                new Dictionary<string, string> { ["Content-Type"] = "application/x-www-form-urlencoded" },
                "session=99&username=ng-user-111&password=ng-password-222&provider=campus"),
            CancellationToken.None);

        Assert.True(response.Allowed);
        Assert.True(forwarder.SubmitCompleted);
        Assert.False(forwarder.PayloadTransformed);
        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("ng-password-222", capturedBody, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("p@ss&=+%中文😃"), capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json占位符被页面脚本变换时立即拒绝转发()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        var markers = new Dictionary<string, string>
        {
            ["ng-user-111"] = "student",
            ["ng-password-222"] = "secret"
        };
        var forwarder = new PortalHttpForwarder(CreateConfig(), client, markers);

        var blocked = await forwarder.ForwardAsync(
            new PortalForwardedRequest(
                "POST",
                new Uri("https://portal.example.edu/sign-in"),
                new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                "{\"username\":\"ng-user-111\",\"password\":\"transformed-ng-password-222\"}"),
            CancellationToken.None);

        Assert.False(blocked.Allowed);
        Assert.True(forwarder.PayloadTransformed);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task 拒绝二次提交并识别密码错误与套餐到期特征()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"msg\":\"套餐到期，请续费\"}", Encoding.UTF8, "application/json")
        });
        using var client = new HttpClient(handler);
        var forwarder = new PortalHttpForwarder(CreateConfig(), client);

        var first = await forwarder.ForwardAsync(
            new PortalForwardedRequest("POST", new Uri("https://portal.example.edu/sign-in"), new Dictionary<string, string>(), "a=1"),
            CancellationToken.None);
        Assert.True(first.Allowed);
        Assert.Equal(PortalAuthOutcome.AccountExpired, forwarder.RejectionOutcome);

        var second = await forwarder.ForwardAsync(
            new PortalForwardedRequest("POST", new Uri("https://portal.example.edu/sign-in"), new Dictionary<string, string>(), "a=1"),
            CancellationToken.None);
        Assert.False(second.Allowed);
        Assert.Single(handler.Requests);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Uri?> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, body);
        }
    }
}
