using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Web;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Portal;
using Xunit;

namespace NetGuardian.Tests;

public sealed class EdgeCdpPortalAuthenticatorTests : IAsyncDisposable
{
    private const string FakeOrigin = "https://portal-prototype.invalid";
    private const string TestUser = "fixture-student";
    private const string TestPassword = "test-value&+😃";

    private readonly LoopbackPortalServer _server = new();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "NetGuardian-EdgeProof-" + Guid.NewGuid().ToString("N"));
    private string _scenario = "";
    private bool _pass;
    private int _session;
    private int _forbiddenHits;
    private readonly List<string?> _submissions = [];

    public EdgeCdpPortalAuthenticatorTests()
    {
        Directory.CreateDirectory(_tempRoot);
        _server.Handler = HandleRequestAsync;
    }

    private EdgeCdpPortalAuthenticator CreateAuthenticator() => new(
        EdgeCdpPortalAuthenticator.FindEdgeExecutable(),
        _tempRoot,
        (_, _, cookies) => new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            CookieContainer = cookies
        }),
        uri =>
        {
            var builder = new UriBuilder(uri)
            {
                Scheme = "http",
                Host = "127.0.0.1",
                Port = _server.Port
            };
            return builder.Uri;
        });

    private static PortalAuthConfig CreateConfig(
        PortalAuthMode mode = PortalAuthMode.CredentialsForm,
        PortalFillStrategy strategy = PortalFillStrategy.PlaceholderSubstitution,
        bool includeSelect = true) => new()
        {
            Enabled = true,
            Ready = true,
            CredentialId = Guid.NewGuid().ToString("N"),
            Mode = mode,
            FillStrategy = strategy,
            AllowedOrigin = FakeOrigin,
            AllowedLoginPath = "/portal",
            AllowedSubmitPath = "/sign-in",
            UsernameSelector = mode == PortalAuthMode.CredentialsForm ? "input[autocomplete=username]" : null,
            PasswordSelector = mode == PortalAuthMode.CredentialsForm ? "input[type=password]" : null,
            SubmitSelector = "button",
            FixedSelectValues = includeSelect ? [new PortalFieldSelection { Selector = "select", Value = "campus" }] : []
        };

    [Fact]
    public async Task 真实Edge无头私有管道覆盖全套门户认证场景()
    {
        if (EdgeCdpPortalAuthenticator.FindEdgeExecutable() is null) return;

        var authenticator = CreateAuthenticator();
        var cred = new PortalCredential(TestUser, TestPassword);

        // 1. 首次表单认证成功 + 2. JSON 表单 + 3. 过期后新会话 + 4. 受限直填（前端加密密码场景）
        foreach (var (name, strategy) in new[]
        {
            ("首次认证", PortalFillStrategy.PlaceholderSubstitution),
            ("JSON 表单", PortalFillStrategy.PlaceholderSubstitution),
            ("过期后新会话", PortalFillStrategy.PlaceholderSubstitution),
            ("凭据在页面被变换", PortalFillStrategy.ScopedBrowserFill)
        })
        {
            _scenario = name;
            _pass = false;
            var discovery = await DiscoverAsync();
            Assert.Equal(PortalEvidence.Redirect, discovery.Evidence);
            Assert.NotNull(discovery.EntryUri);

            var config = CreateConfig(strategy: strategy);
            var result = await authenticator.AuthenticateAsync(
                new PortalAuthRequest("CampusNet", config, discovery.EntryUri, IPAddress.Loopback, 1),
                cred,
                CancellationToken.None);

            Assert.Equal(PortalAuthOutcome.Submitted, result.Outcome);
            Assert.False(result.ShouldPause);
            Assert.True(_pass, name);
        }

        // 5. 单按钮放行（无凭据）
        _scenario = "单按钮放行";
        _pass = false;
        var consentDiscovery = await DiscoverAsync();
        var consentResult = await authenticator.AuthenticateAsync(
            new PortalAuthRequest("GuestNet", CreateConfig(PortalAuthMode.OneClickConsent, includeSelect: false), consentDiscovery.EntryUri, IPAddress.Loopback, 1),
            null,
            CancellationToken.None);
        Assert.Equal(PortalAuthOutcome.Submitted, consentResult.Outcome);
        Assert.True(_pass);

        // 6. 登录拒绝 -> ShouldPause
        _scenario = "登录拒绝";
        _pass = false;
        var rejectedDiscovery = await DiscoverAsync();
        var rejectedResult = await authenticator.AuthenticateAsync(
            new PortalAuthRequest("CampusNet", CreateConfig(), rejectedDiscovery.EntryUri, IPAddress.Loopback, 1),
            cred,
            CancellationToken.None);
        Assert.Equal(PortalAuthOutcome.CredentialsRejected, rejectedResult.Outcome);
        Assert.True(rejectedResult.ShouldPause);

        // 7. 套餐到期 -> ShouldPause
        _scenario = "套餐到期";
        _pass = false;
        var expiredDiscovery = await DiscoverAsync();
        var expiredResult = await authenticator.AuthenticateAsync(
            new PortalAuthRequest("CampusNet", CreateConfig(), expiredDiscovery.EntryUri, IPAddress.Loopback, 1),
            cred,
            CancellationToken.None);
        Assert.Equal(PortalAuthOutcome.AccountExpired, expiredResult.Outcome);
        Assert.True(expiredResult.ShouldPause);

        // 8. 提交目标不匹配 -> 不得命中 /unapproved
        _scenario = "目标不匹配";
        _pass = false;
        var mismatchDiscovery = await DiscoverAsync();
        var mismatchResult = await authenticator.AuthenticateAsync(
            new PortalAuthRequest("CampusNet", CreateConfig(), mismatchDiscovery.EntryUri, IPAddress.Loopback, 1),
            cred,
            CancellationToken.None);
        Assert.Equal(PortalAuthOutcome.SubmitTargetMismatch, mismatchResult.Outcome);

        // 9. 控件歧义 -> 停止填表
        _scenario = "控件歧义";
        _pass = false;
        var ambiguousDiscovery = await DiscoverAsync();
        var ambiguousResult = await authenticator.AuthenticateAsync(
            new PortalAuthRequest("CampusNet", CreateConfig(), ambiguousDiscovery.EntryUri, IPAddress.Loopback, 1),
            cred,
            CancellationToken.None);
        Assert.Equal(PortalAuthOutcome.ControlAmbiguous, ambiguousResult.Outcome);

        // 10. 入口未知 -> 不启动填表
        _scenario = "入口未知";
        _pass = false;
        var unknownDiscovery = await DiscoverAsync();
        var unknownResult = await authenticator.AuthenticateAsync(
            new PortalAuthRequest("CampusNet", CreateConfig(), unknownDiscovery.EntryUri, IPAddress.Loopback, 1),
            cred,
            CancellationToken.None);
        Assert.Equal(PortalAuthOutcome.EntryOriginMismatch, unknownResult.Outcome);

        // 11. 页面尝试额外上传 -> 主请求完成，但 /unapproved 被转发器阻断
        _scenario = "页面尝试额外上传";
        _pass = false;
        var extraDiscovery = await DiscoverAsync();
        var extraResult = await authenticator.AuthenticateAsync(
            new PortalAuthRequest("CampusNet", CreateConfig(), extraDiscovery.EntryUri, IPAddress.Loopback, 1),
            cred,
            CancellationToken.None);
        Assert.Equal(PortalAuthOutcome.Submitted, extraResult.Outcome);
        Assert.False(_pass);

        // 12. 占位替换模式下脚本篡改凭据 -> PayloadTransformed
        _scenario = "凭据在页面被变换";
        _pass = false;
        var transformDiscovery = await DiscoverAsync();
        var transformResult = await authenticator.AuthenticateAsync(
            new PortalAuthRequest("CampusNet", CreateConfig(strategy: PortalFillStrategy.PlaceholderSubstitution), transformDiscovery.EntryUri, IPAddress.Loopback, 1),
            cred,
            CancellationToken.None);
        Assert.Equal(PortalAuthOutcome.PayloadTransformed, transformResult.Outcome);

        // 13. 登录无响应超时
        _scenario = "登录无响应";
        _pass = false;
        var timeoutDiscovery = await DiscoverAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var timeoutResult = await authenticator.AuthenticateAsync(
            new PortalAuthRequest("CampusNet", CreateConfig(), timeoutDiscovery.EntryUri, IPAddress.Loopback, 1),
            cred,
            cts.Token);
        Assert.True(timeoutResult.Outcome is PortalAuthOutcome.Cancelled or PortalAuthOutcome.TimedOut);

        Assert.Equal(0, _forbiddenHits);
        Assert.Equal(_submissions.Distinct().Count(), _submissions.Count);

        // 14. 配置向导控件探测（只读）
        _scenario = "首次认证";
        var inspectDiscovery = await DiscoverAsync();
        var inspection = await authenticator.InspectEntryAsync(inspectDiscovery.EntryUri!, IPAddress.Loopback, 1, allowInsecureHttp: false, CancellationToken.None);
        Assert.True(inspection.Success);
        Assert.NotEmpty(inspection.UsernameCandidates);
        Assert.NotEmpty(inspection.PasswordCandidates);
        Assert.NotEmpty(inspection.SelectCandidates);
        Assert.NotEmpty(inspection.SubmitCandidates);

        // 确认所有临时 Edge 用户数据目录已被清理。
        Assert.Empty(Directory.GetDirectories(_tempRoot));
    }

    private async Task<PortalDiscoveryResult> DiscoverAsync()
    {
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
        var result = await PortalProbe.DiscoverAsync(client, CancellationToken.None, new Uri($"http://127.0.0.1:{_server.Port}/probe"));
        if (result.EntryUri is not null && result.EntryUri.Host == "127.0.0.1")
        {
            var mapped = new UriBuilder(result.EntryUri) { Scheme = "https", Host = "portal-prototype.invalid", Port = 443 }.Uri;
            return new PortalDiscoveryResult(result.Evidence, mapped, PortalDiscoveryResult.SanitizeUri(mapped));
        }
        return result;
    }

    private async Task<(int Status, string ContentType, string Body, string? Location)> HandleRequestAsync(
        string method, string pathAndQuery, string? contentType, string body)
    {
        var uri = new Uri("http://127.0.0.1" + pathAndQuery);
        if (uri.AbsolutePath == "/probe")
        {
            if (_pass) return (200, "text/plain; charset=utf-8", "expected", null);
            _session++;
            if (_scenario == "入口未知")
                return (302, "text/plain", "", "https://unknown-prototype.invalid/portal");
            return (302, "text/plain", "", $"{FakeOrigin}/portal?s={_session}");
        }

        if (uri.AbsolutePath == "/unapproved")
        {
            _forbiddenHits++;
            return (404, "text/plain", "forbidden", null);
        }

        if (uri.AbsolutePath == "/portal")
        {
            var action = _scenario == "目标不匹配" ? "/unapproved" : "/sign-in";
            if (_scenario == "单按钮放行")
            {
                return (200, "text/html; charset=utf-8", $$"""
                    <!doctype html><meta charset="utf-8"><main>
                    <form data-submit="{{action}}"><button type="submit">同意并连接</button></form>
                    </main>
                    <script>
                    const session = new URLSearchParams(location.search).get('s');
                    document.querySelector('form').addEventListener('submit', async e => {
                        e.preventDefault();
                        const res = await fetch(e.currentTarget.dataset.submit, { method: 'POST', body: new URLSearchParams({ session, consent: '1' }) });
                        document.querySelector('main').textContent = await res.text();
                    });
                    </script>
                    """, null);
            }

            var count = _scenario == "控件歧义" ? 2 : 1;
            var userFields = string.Concat(Enumerable.Repeat("<label>学号<input autocomplete=\"username\"></label>", count));
            var jsonFlag = _scenario == "JSON 表单" ? "true" : "false";
            var transformFlag = _scenario == "凭据在页面被变换" ? "true" : "false";
            var extraFlag = _scenario == "页面尝试额外上传" ? "true" : "false";
            var htmlBody = $$"""
                <!doctype html><meta charset="utf-8"><main></main>
                <script>
                const session = new URLSearchParams(location.search).get('s');
                document.querySelector('main').innerHTML = '<form data-submit="{{action}}">{{userFields}}<label>密码<input autocomplete="current-password" type="password"></label><label>运营商<select><option value="campus">校园网</option><option value="other">其他</option></select></label><button type="submit">连接 Login</button></form>';
                document.querySelector('form').addEventListener('submit', async e => {
                    e.preventDefault();
                    const form = e.currentTarget;
                    const payload = new URLSearchParams({
                        session,
                        username: form.querySelector('[autocomplete=username]').value,
                        password: form.querySelector('[type=password]').value,
                        provider: form.querySelector('select').value
                    });
                    if ({{transformFlag}}) payload.set('password', 'transformed-' + payload.get('password'));
                    const response = await fetch(form.dataset.submit, {{jsonFlag}}
                        ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(Object.fromEntries(payload)) }
                        : { method: 'POST', body: payload });
                    document.querySelector('main').textContent = await response.text();
                    if ({{extraFlag}}) { try { await fetch('/unapproved', { method: 'POST', body: payload }); } catch {} }
                });
                </script>
                """;
            return (200, "text/html; charset=utf-8", htmlBody, null);
        }

        if (uri.AbsolutePath == "/sign-in" && method == "POST")
        {
            Dictionary<string, string> fields;
            if (contentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                fields = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? [];
            }
            else
            {
                var parsed = HttpUtility.ParseQueryString(body);
                fields = parsed.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => parsed[k!] ?? "");
            }

            fields.TryGetValue("session", out var sess);
            _submissions.Add(sess);

            if (_scenario == "登录无响应")
            {
                await Task.Delay(6000);
                return (503, "text/plain; charset=utf-8", "unavailable", null);
            }

            if (_scenario == "套餐到期")
                return (200, "text/plain; charset=utf-8", "套餐到期，请先续费", null);

            if (_scenario == "登录拒绝")
                return (403, "text/plain; charset=utf-8", "rejected", null);

            if (_scenario == "单按钮放行")
            {
                if (sess == _session.ToString(CultureInfo.InvariantCulture)) _pass = true;
                return (200, "text/plain; charset=utf-8", "accepted", null);
            }

            var expectedPass = _scenario == "凭据在页面被变换" ? "transformed-" + TestPassword : TestPassword;
            var ok = fields.GetValueOrDefault("username") == TestUser
                && fields.GetValueOrDefault("password") == expectedPass
                && fields.GetValueOrDefault("provider") == "campus"
                && sess == _session.ToString(CultureInfo.InvariantCulture);

            if (!ok) return (403, "text/plain; charset=utf-8", "rejected", null);
            if (_scenario != "页面尝试额外上传") _pass = true;
            return (200, "text/plain; charset=utf-8", "accepted", null);
        }

        return (404, "text/plain", "not found", null);
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        }
    }

    private sealed class LoopbackPortalServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentBag<Task> _tasks = [];
        private readonly Task _acceptLoop;

        public LoopbackPortalServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = AcceptAsync();
        }

        public int Port { get; }
        public Func<string, string, string?, string, Task<(int Status, string ContentType, string Body, string? Location)>> Handler { get; set; }
            = (_, _, _, _) => Task.FromResult((404, "text/plain", "", (string?)null));

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _tasks.Add(ServeAsync(client));
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    var headerBytes = new List<byte>(1024);
                    var single = new byte[1];
                    while (true)
                    {
                        var read = await stream.ReadAsync(single, _stop.Token);
                        if (read == 0) return;
                        headerBytes.Add(single[0]);
                        var count = headerBytes.Count;
                        if (count >= 4 && headerBytes[count - 4] == '\r' && headerBytes[count - 3] == '\n'
                            && headerBytes[count - 2] == '\r' && headerBytes[count - 1] == '\n')
                            break;
                    }

                    var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
                    var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length == 0) return;
                    var parts = lines[0].Split(' ');
                    if (parts.Length < 2) return;
                    var method = parts[0];
                    var path = parts[1];

                    var contentLength = 0;
                    string? contentType = null;
                    foreach (var line in lines.Skip(1))
                    {
                        var colon = line.IndexOf(':');
                        if (colon <= 0) continue;
                        var name = line[..colon].Trim();
                        var val = line[(colon + 1)..].Trim();
                        if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(val, out contentLength);
                        else if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            contentType = val;
                    }

                    var bodyBytes = new byte[contentLength];
                    var offset = 0;
                    while (offset < contentLength)
                    {
                        var read = await stream.ReadAsync(bodyBytes.AsMemory(offset, contentLength - offset), _stop.Token);
                        if (read == 0) break;
                        offset += read;
                    }

                    var bodyText = Encoding.UTF8.GetString(bodyBytes, 0, offset);
                    var (status, respType, respBody, location) = await Handler(method, path, contentType, bodyText);
                    var payload = Encoding.UTF8.GetBytes(respBody);
                    var sb = new StringBuilder();
                    sb.Append($"HTTP/1.1 {status} OK\r\n");
                    sb.Append($"Content-Type: {respType}\r\n");
                    sb.Append($"Content-Length: {payload.Length}\r\n");
                    if (!string.IsNullOrEmpty(location)) sb.Append($"Location: {location}\r\n");
                    sb.Append("Connection: close\r\n\r\n");

                    var respHeader = Encoding.ASCII.GetBytes(sb.ToString());
                    await stream.WriteAsync(respHeader, _stop.Token);
                    await stream.WriteAsync(payload, _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                }
                catch { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _acceptLoop; } catch { }
            try { await Task.WhenAll(_tasks); } catch { }
            _stop.Dispose();
        }
    }
}
