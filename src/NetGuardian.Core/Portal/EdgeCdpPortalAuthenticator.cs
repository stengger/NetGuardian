using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;

namespace NetGuardian.Core.Portal;

/// <summary>
/// 通过隔离的无头 Edge 私有管道会话执行门户检查与自动表单提交，
/// 所有外发网络流量由宿主的 <see cref="PortalHttpForwarder"/> 经目标 Wi-Fi 出口代为请求与校验。
/// </summary>
public sealed class EdgeCdpPortalAuthenticator : IPortalAuthenticator
{
    private readonly string? _configuredEdgePath;
    private readonly string _tempRoot;
    private readonly Func<IPAddress?, int?, CookieContainer, HttpClient>? _httpClientFactory;
    private readonly Func<Uri, Uri>? _uriMapper;

    public EdgeCdpPortalAuthenticator(string? edgePath = null, string? tempRoot = null)
    {
        _configuredEdgePath = edgePath;
        _tempRoot = tempRoot ?? Path.GetTempPath();
    }

    internal EdgeCdpPortalAuthenticator(
        string? edgePath,
        string? tempRoot,
        Func<IPAddress?, int?, CookieContainer, HttpClient>? httpClientFactory,
        Func<Uri, Uri>? uriMapper = null)
    {
        _configuredEdgePath = edgePath;
        _tempRoot = tempRoot ?? Path.GetTempPath();
        _httpClientFactory = httpClientFactory;
        _uriMapper = uriMapper;
    }

    public static string? FindEdgeExecutable(string? preferredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
            return preferredPath;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<PortalAuthResult> AuthenticateAsync(
        PortalAuthRequest request,
        PortalCredential? credential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var config = request.Config;
        if (!config.IsConfigurationValid())
            return new PortalAuthResult(PortalAuthOutcome.Failed, "网页认证配置不完整。");

        if (config.Mode == PortalAuthMode.CredentialsForm
            && (credential is null || string.IsNullOrEmpty(credential.Username) || string.IsNullOrEmpty(credential.Password)))
        {
            return new PortalAuthResult(PortalAuthOutcome.CredentialMissing, "未找到可用的网页认证凭据。");
        }

        if (request.DiscoveredEntryUri is not { } entryUri)
            return new PortalAuthResult(PortalAuthOutcome.EntryNotFound, "未能从当前网络发现门户登录入口。");

        var sanitizedEntry = PortalDiscoveryResult.SanitizeUri(entryUri);
        if (!config.MatchesEntryUri(entryUri))
            return new PortalAuthResult(PortalAuthOutcome.EntryOriginMismatch, "发现的门户来源与已验证配置不符，已中止自动填充。", sanitizedEntry);

        var edgePath = FindEdgeExecutable(_configuredEdgePath);
        if (edgePath is null || !OperatingSystem.IsWindows())
            return new PortalAuthResult(PortalAuthOutcome.BrowserUnavailable, "未找到可用的 Microsoft Edge 浏览器。", sanitizedEntry);

        if (_httpClientFactory is null && (request.WifiAddress is null || request.WifiInterfaceIndex is not > 0))
            return new PortalAuthResult(PortalAuthOutcome.Failed, "无法确定用于门户认证的无线接口。", sanitizedEntry);

        var markers = new Dictionary<string, string>(StringComparer.Ordinal);
        string? usernameValue = null;
        string? passwordValue = null;

        if (config.Mode == PortalAuthMode.CredentialsForm && credential is not null)
        {
            if (config.FillStrategy == PortalFillStrategy.PlaceholderSubstitution)
            {
                var userMarker = $"ng-user-{Guid.NewGuid():N}";
                var secretMarker = $"ng-password-{Guid.NewGuid():N}";
                markers[userMarker] = credential.Username;
                markers[secretMarker] = credential.Password;
                usernameValue = userMarker;
                passwordValue = secretMarker;
            }
            else
            {
                usernameValue = credential.Username;
                passwordValue = credential.Password;
            }
        }

        var cookies = new CookieContainer();
        using var httpClient = CreateScopedHttpClient(request.WifiAddress, request.WifiInterfaceIndex, cookies);
        var forwarder = new PortalHttpForwarder(config, httpClient, markers, inspectOnly: false, uriMapper: _uriMapper);

        var profileDir = Path.Combine(_tempRoot, "NetGuardian-Portal-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var edge = EdgePipeProcess.Start(edgePath, profileDir);
            var targetResult = await edge.CallAsync("Target.createTarget", new { url = "about:blank" }, cancellationToken: cancellationToken);
            var targetId = targetResult["targetId"]?.GetValue<string>() ?? throw new InvalidOperationException("无法创建浏览器页面。");
            var attachResult = await edge.CallAsync("Target.attachToTarget", new { targetId, flatten = true }, cancellationToken: cancellationToken);
            var sessionId = attachResult["sessionId"]?.GetValue<string>() ?? throw new InvalidOperationException("无法附加到浏览器会话。");

            edge.NotificationReceived += (method, parameters) =>
            {
                if (method != "Fetch.requestPaused" || !OperatingSystem.IsWindows()) return Task.CompletedTask;
                return HandlePausedRequestAsync(edge, sessionId, forwarder, parameters, cancellationToken);
            };

            await edge.CallAsync("Fetch.enable", new { patterns = new[] { new { urlPattern = "*" } } }, sessionId, cancellationToken);

            var navResult = await edge.CallAsync("Page.navigate", new { url = entryUri.AbsoluteUri }, sessionId, cancellationToken);
            if (navResult["errorText"]?.GetValue<string>() is { Length: > 0 } navError)
            {
                var detail = forwarder.FailureDetail ?? $"加载门户页面失败：{navError}";
                return new PortalAuthResult(PortalAuthOutcome.Failed, detail, sanitizedEntry);
            }

            var readyScript = BuildReadyCheckScript(config);
            var pageReady = await PollBooleanAsync(edge, sessionId, readyScript, TimeSpan.FromSeconds(8), cancellationToken);
            if (!pageReady)
            {
                if (forwarder.TlsOrTransportFailed)
                    return new PortalAuthResult(PortalAuthOutcome.Failed, forwarder.FailureDetail ?? "门户页面加载失败。", sanitizedEntry);
                return new PortalAuthResult(PortalAuthOutcome.ControlNotFound, "门户页面未在规定时间内渲染出登录控件。", sanitizedEntry);
            }

            var inspectCounts = await EvaluateJsonAsync(edge, sessionId, BuildControlAuditScript(config, entryUri), cancellationToken);
            var auditStatus = inspectCounts?["status"]?.GetValue<string>();
            switch (auditStatus)
            {
                case "origin_mismatch":
                    return new PortalAuthResult(PortalAuthOutcome.EntryOriginMismatch, "页面实际地址与配置来源不符。", sanitizedEntry);
                case "control_not_found":
                    return new PortalAuthResult(PortalAuthOutcome.ControlNotFound, "未能定位唯一的登录控件。", sanitizedEntry);
                case "control_ambiguous":
                    return new PortalAuthResult(PortalAuthOutcome.ControlAmbiguous, "页面出现多个匹配的输入或提交控件，已停止自动填表。", sanitizedEntry);
                case "select_option_missing":
                    return new PortalAuthResult(PortalAuthOutcome.ControlNotFound, "预设下拉选项在页面中不存在。", sanitizedEntry);
            }

            var fillAndSubmitResult = await EvaluateJsonAsync(
                edge,
                sessionId,
                BuildFillAndSubmitScript(config, usernameValue, passwordValue),
                cancellationToken);

            if (fillAndSubmitResult?["submitted"]?.GetValue<bool>() != true)
                return new PortalAuthResult(PortalAuthOutcome.Failed, "页面未接受表单提交指令。", sanitizedEntry);

            // 等待提交请求被转发器放行或被拦截
            var deadline = DateTimeOffset.UtcNow.AddSeconds(6);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (forwarder.PayloadTransformed)
                    return new PortalAuthResult(PortalAuthOutcome.PayloadTransformed, forwarder.FailureDetail ?? "凭据在页面中被变换，已停止提交。", sanitizedEntry);
                if (forwarder.SubmitTargetBlocked)
                    return new PortalAuthResult(PortalAuthOutcome.SubmitTargetMismatch, forwarder.FailureDetail ?? "表单提交地址与白名单不符。", sanitizedEntry);
                if (forwarder.SubmitCompleted)
                    break;
                if (forwarder.TlsOrTransportFailed && forwarder.SubmitAttempted)
                    return new PortalAuthResult(PortalAuthOutcome.Failed, forwarder.FailureDetail ?? "提交认证请求失败。", sanitizedEntry);
                await Task.Delay(60, cancellationToken);
            }

            if (!forwarder.SubmitAttempted)
                return new PortalAuthResult(PortalAuthOutcome.Failed, "点击提交后未观察到任何表单请求。", sanitizedEntry);

            if (!forwarder.SubmitCompleted)
                return new PortalAuthResult(PortalAuthOutcome.TimedOut, forwarder.FailureDetail ?? "等待门户提交响应超时。", sanitizedEntry);

            // 给前端短暂时间处理异步登录响应和可能伴随的额外外发检测。
            await Task.Delay(120, cancellationToken);

            if (forwarder.RejectionOutcome is { } rejection)
                return new PortalAuthResult(rejection, forwarder.FailureDetail ?? "门户拒绝登录。", sanitizedEntry, ShouldPause: true);

            // 也检查页面渲染文本是否包含明确的拒绝或过期提示。
            var bodyHint = await EvaluateStringAsync(edge, sessionId, "document.body ? document.body.innerText.slice(0, 1500) : ''", cancellationToken);
            if (!string.IsNullOrWhiteSpace(bodyHint))
            {
                if (ContainsAny(bodyHint, "套餐到期", "已过期", "余额不足", "欠费", "停机", "account expired", "overdue"))
                    return new PortalAuthResult(PortalAuthOutcome.AccountExpired, "门户页面提示套餐到期或欠费，已暂停自动认证。", sanitizedEntry, ShouldPause: true);
                if (ContainsAny(bodyHint, "密码错误", "用户名或密码", "认证失败", "invalid password", "wrong password", "rejected"))
                    return new PortalAuthResult(PortalAuthOutcome.CredentialsRejected, "门户页面提示账号或密码被拒绝，已暂停自动认证。", sanitizedEntry, ShouldPause: true);
            }

            return new PortalAuthResult(PortalAuthOutcome.Submitted, "已提交网页认证请求，等待公网复测。", sanitizedEntry);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new PortalAuthResult(PortalAuthOutcome.Cancelled, "网页认证已取消。", sanitizedEntry);
        }
        catch (TimeoutException ex)
        {
            return new PortalAuthResult(PortalAuthOutcome.TimedOut, ex.Message, sanitizedEntry);
        }
        catch (Exception ex)
        {
            return new PortalAuthResult(PortalAuthOutcome.Failed, $"网页认证异常：{ex.Message}", sanitizedEntry);
        }
    }

    public async Task<PortalInspectionResult> InspectEntryAsync(
        Uri entryUri,
        IPAddress? wifiAddress,
        int? interfaceIndex,
        bool allowInsecureHttp,
        CancellationToken cancellationToken)
    {
        var origin = PortalAuthConfig.FormatOrigin(entryUri);
        var loginPath = string.IsNullOrEmpty(entryUri.AbsolutePath) ? "/" : entryUri.AbsolutePath;
        var isHttps = entryUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

        if (!isHttps && !allowInsecureHttp)
        {
            return new PortalInspectionResult(false, "门户地址不是 HTTPS，请确认信任后重试。", origin, loginPath, null, null, false, [], [], [], []);
        }

        var edgePath = FindEdgeExecutable(_configuredEdgePath);
        if (edgePath is null || !OperatingSystem.IsWindows())
            return new PortalInspectionResult(false, "未找到可用的 Microsoft Edge 浏览器。", origin, loginPath, null, null, isHttps, [], [], [], []);

        if (_httpClientFactory is null && (wifiAddress is null || interfaceIndex is not > 0))
            return new PortalInspectionResult(false, "无法确定目标无线接口。", origin, loginPath, null, null, isHttps, [], [], [], []);

        var inspectConfig = new PortalAuthConfig
        {
            AllowInsecureHttp = allowInsecureHttp,
            AllowedOrigin = origin,
            AllowedLoginPath = loginPath,
            SubmitSelector = "button"
        };

        var cookies = new CookieContainer();
        using var httpClient = CreateScopedHttpClient(wifiAddress, interfaceIndex, cookies);
        var forwarder = new PortalHttpForwarder(inspectConfig, httpClient, inspectOnly: true, uriMapper: _uriMapper);
        var profileDir = Path.Combine(_tempRoot, "NetGuardian-PortalInspect-" + Guid.NewGuid().ToString("N"));

        try
        {
            await using var edge = EdgePipeProcess.Start(edgePath, profileDir);
            var targetResult = await edge.CallAsync("Target.createTarget", new { url = "about:blank" }, cancellationToken: cancellationToken);
            var targetId = targetResult["targetId"]?.GetValue<string>() ?? throw new InvalidOperationException("无法创建浏览器页面。");
            var attachResult = await edge.CallAsync("Target.attachToTarget", new { targetId, flatten = true }, cancellationToken: cancellationToken);
            var sessionId = attachResult["sessionId"]?.GetValue<string>() ?? throw new InvalidOperationException("无法附加到浏览器会话。");

            edge.NotificationReceived += (method, parameters) =>
            {
                if (method != "Fetch.requestPaused" || !OperatingSystem.IsWindows()) return Task.CompletedTask;
                return HandlePausedRequestAsync(edge, sessionId, forwarder, parameters, cancellationToken);
            };

            await edge.CallAsync("Fetch.enable", new { patterns = new[] { new { urlPattern = "*" } } }, sessionId, cancellationToken);
            var navResult = await edge.CallAsync("Page.navigate", new { url = entryUri.AbsoluteUri }, sessionId, cancellationToken);
            if (navResult["errorText"]?.GetValue<string>() is { Length: > 0 } navError)
                return new PortalInspectionResult(false, forwarder.FailureDetail ?? navError, origin, loginPath, null, null, isHttps, [], [], [], []);

            await PollBooleanAsync(edge, sessionId, "document.readyState === 'complete' && !!document.querySelector('form, input, button')", TimeSpan.FromSeconds(6), cancellationToken);
            var domInfo = await EvaluateJsonAsync(edge, sessionId, BuildInspectionScript(), cancellationToken);
            if (domInfo is null)
                return new PortalInspectionResult(false, "未能读取门户页面控件。", origin, loginPath, null, null, isHttps, [], [], [], []);

            var users = ParseControls(domInfo["usernames"] as JsonArray);
            var passwords = ParseControls(domInfo["passwords"] as JsonArray);
            var selects = ParseControls(domInfo["selects"] as JsonArray);
            var submits = ParseControls(domInfo["submits"] as JsonArray);

            var actionRaw = domInfo["formAction"]?.GetValue<string>();
            string? submitOrigin = origin;
            string? submitPath = loginPath;
            if (!string.IsNullOrWhiteSpace(actionRaw) && Uri.TryCreate(entryUri, actionRaw, out var actionUri))
            {
                submitOrigin = PortalAuthConfig.FormatOrigin(actionUri);
                submitPath = string.IsNullOrEmpty(actionUri.AbsolutePath) ? "/" : actionUri.AbsolutePath;
            }

            return new PortalInspectionResult(true, "已识别门户页面控件，请核对后试登。", origin, loginPath, submitOrigin, submitPath, isHttps, users, passwords, selects, submits);
        }
        catch (OperationCanceledException)
        {
            return new PortalInspectionResult(false, "门户检测已取消。", origin, loginPath, null, null, isHttps, [], [], [], []);
        }
        catch (Exception ex)
        {
            return new PortalInspectionResult(false, $"门户检测失败：{ex.Message}", origin, loginPath, null, null, isHttps, [], [], [], []);
        }
    }

    private HttpClient CreateScopedHttpClient(IPAddress? address, int? interfaceIndex, CookieContainer cookies)
    {
        if (_httpClientFactory is not null)
            return _httpClientFactory(address, interfaceIndex, cookies);
        return WifiSocketConnector.CreateHttpClient(address!, interfaceIndex!.Value, allowAutoRedirect: false, cookies: cookies);
    }

    [SupportedOSPlatform("windows")]
    private static async Task HandlePausedRequestAsync(
        EdgePipeProcess edge,
        string sessionId,
        PortalHttpForwarder forwarder,
        JsonObject parameters,
        CancellationToken token)
    {
        var requestId = parameters["requestId"]?.GetValue<string>();
        var requestObj = parameters["request"] as JsonObject;
        if (string.IsNullOrEmpty(requestId) || requestObj is null) return;

        var method = requestObj["method"]?.GetValue<string>() ?? "GET";
        var urlText = requestObj["url"]?.GetValue<string>() ?? "";
        var postData = requestObj["postData"]?.GetValue<string>();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (requestObj["headers"] is JsonObject hdrs)
        {
            foreach (var kv in hdrs)
            {
                if (kv.Value is JsonValue v && v.TryGetValue<string>(out var s))
                    headers[kv.Key] = s;
            }
        }

        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var uri))
        {
            try { await edge.CallAsync("Fetch.failRequest", new { requestId, errorReason = "BlockedByClient" }, sessionId, token); } catch { }
            return;
        }

        var forwarded = await forwarder.ForwardAsync(new PortalForwardedRequest(method, uri, headers, postData), token);
        if (!forwarded.Allowed)
        {
            try { await edge.CallAsync("Fetch.failRequest", new { requestId, errorReason = "BlockedByClient" }, sessionId, token); } catch { }
            return;
        }

        var responseHeaders = forwarded.Headers
            .Select(h => new { name = h.Key, value = h.Value })
            .ToArray();
        try
        {
            await edge.CallAsync("Fetch.fulfillRequest", new
            {
                requestId,
                responseCode = forwarded.StatusCode,
                responseHeaders,
                body = Convert.ToBase64String(forwarded.Body)
            }, sessionId, token);
        }
        catch { }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<bool> PollBooleanAsync(
        EdgePipeProcess edge,
        string sessionId,
        string expression,
        TimeSpan timeout,
        CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            var result = await edge.CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, sessionId, token);
            if (result["result"]?["value"]?.GetValue<bool>() == true) return true;
            await Task.Delay(60, token);
        }
        return false;
    }

    [SupportedOSPlatform("windows")]
    private static async Task<JsonObject?> EvaluateJsonAsync(
        EdgePipeProcess edge,
        string sessionId,
        string expression,
        CancellationToken token)
    {
        var result = await edge.CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, sessionId, token);
        if (result["exceptionDetails"] is not null) return null;
        return result["result"]?["value"] as JsonObject;
    }

    [SupportedOSPlatform("windows")]
    private static async Task<string?> EvaluateStringAsync(
        EdgePipeProcess edge,
        string sessionId,
        string expression,
        CancellationToken token)
    {
        var result = await edge.CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, sessionId, token);
        return result["result"]?["value"]?.GetValue<string>();
    }

    private static string BuildReadyCheckScript(PortalAuthConfig config)
    {
        var submitSel = JsonSerializer.Serialize(config.SubmitSelector);
        if (config.Mode == PortalAuthMode.OneClickConsent)
            return $"!!document.querySelector({submitSel})";
        var userSel = JsonSerializer.Serialize(config.UsernameSelector ?? "");
        var passSel = JsonSerializer.Serialize(config.PasswordSelector ?? "");
        return $"!!document.querySelector({userSel}) && !!document.querySelector({passSel}) && !!document.querySelector({submitSel})";
    }

    private static string BuildControlAuditScript(PortalAuthConfig config, Uri expectedEntryUri)
    {
        var expectedOrigin = JsonSerializer.Serialize(PortalAuthConfig.FormatOrigin(expectedEntryUri));
        var userSel = JsonSerializer.Serialize(config.UsernameSelector ?? "");
        var passSel = JsonSerializer.Serialize(config.PasswordSelector ?? "");
        var submitSel = JsonSerializer.Serialize(config.SubmitSelector);
        var selectsJson = JsonSerializer.Serialize(config.FixedSelectValues);
        var isCredentials = config.Mode == PortalAuthMode.CredentialsForm ? "true" : "false";

        return $$"""
        (() => {
            if (location.origin.toLowerCase() !== {{expectedOrigin}}.toLowerCase()) return { status: 'origin_mismatch' };
            const sub = document.querySelectorAll({{submitSel}});
            if (sub.length === 0) return { status: 'control_not_found' };
            if (sub.length > 1) return { status: 'control_ambiguous' };
            if ({{isCredentials}}) {
                const u = document.querySelectorAll({{userSel}});
                const p = document.querySelectorAll({{passSel}});
                if (u.length === 0 || p.length === 0) return { status: 'control_not_found' };
                if (u.length > 1 || p.length > 1) return { status: 'control_ambiguous' };
            }
            const selects = {{selectsJson}};
            for (const item of selects) {
                const els = document.querySelectorAll(item.Selector);
                if (els.length !== 1) return { status: els.length === 0 ? 'control_not_found' : 'control_ambiguous' };
                const sel = els[0];
                const hasOpt = Array.from(sel.options || []).some(o => o.value === item.Value);
                if (!hasOpt) return { status: 'select_option_missing' };
            }
            return { status: 'ok' };
        })()
        """;
    }

    private static string BuildFillAndSubmitScript(PortalAuthConfig config, string? username, string? password)
    {
        var userSel = JsonSerializer.Serialize(config.UsernameSelector ?? "");
        var passSel = JsonSerializer.Serialize(config.PasswordSelector ?? "");
        var submitSel = JsonSerializer.Serialize(config.SubmitSelector);
        var userVal = JsonSerializer.Serialize(username ?? "");
        var passVal = JsonSerializer.Serialize(password ?? "");
        var selectsJson = JsonSerializer.Serialize(config.FixedSelectValues);
        var isCredentials = config.Mode == PortalAuthMode.CredentialsForm ? "true" : "false";

        return $$"""
        (() => {
            function setNativeValue(el, value) {
                const proto = el.tagName === 'SELECT' ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
                const desc = Object.getOwnPropertyDescriptor(proto, 'value');
                if (desc && desc.set) desc.set.call(el, value);
                else el.value = value;
                el.dispatchEvent(new Event('input', { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
            }
            if ({{isCredentials}}) {
                setNativeValue(document.querySelector({{userSel}}), {{userVal}});
                setNativeValue(document.querySelector({{passSel}}), {{passVal}});
            }
            for (const item of {{selectsJson}}) {
                setNativeValue(document.querySelector(item.Selector), item.Value);
            }
            const submit = document.querySelector({{submitSel}});
            if (!submit) return { submitted: false };
            submit.click();
            return { submitted: true };
        })()
        """;
    }

    private static string BuildInspectionScript() => """
        (() => {
            function buildSelector(el, fallback) {
                if (el.id) return '#' + CSS.escape(el.id);
                if (el.name) return el.tagName.toLowerCase() + '[name="' + el.name.replace(/"/g, '\\"') + '"]';
                if (el.getAttribute('autocomplete')) return el.tagName.toLowerCase() + '[autocomplete="' + el.getAttribute('autocomplete') + '"]';
                return fallback;
            }
            function labelOf(el) {
                if (el.labels && el.labels.length > 0) return el.labels[0].innerText.trim();
                return (el.getAttribute('placeholder') || el.getAttribute('aria-label') || el.innerText || el.value || el.name || el.id || el.tagName).trim();
            }
            const allInputs = Array.from(document.querySelectorAll('input'));
            const usernames = allInputs
                .filter(i => ['text', 'email', 'tel', ''].includes((i.type || 'text').toLowerCase()))
                .filter(i => i.type?.toLowerCase() !== 'hidden')
                .map((i, idx) => ({ selector: buildSelector(i, 'input:not([type=password]):not([type=hidden]):nth-of-type(' + (idx + 1) + ')'), label: labelOf(i), kind: 'username' }));
            const passwords = allInputs
                .filter(i => (i.type || '').toLowerCase() === 'password')
                .map((i, idx) => ({ selector: buildSelector(i, 'input[type=password]:nth-of-type(' + (idx + 1) + ')'), label: labelOf(i), kind: 'password' }));
            const selects = Array.from(document.querySelectorAll('select')).map((s, idx) => ({
                selector: buildSelector(s, 'select:nth-of-type(' + (idx + 1) + ')'),
                label: labelOf(s),
                kind: 'select',
                options: Array.from(s.options).map(o => ({ value: o.value, text: (o.textContent || '').trim() }))
            }));
            const submits = Array.from(document.querySelectorAll('button, input[type=submit], input[type=button]'))
                .map((b, idx) => ({ selector: buildSelector(b, b.tagName.toLowerCase() + ':nth-of-type(' + (idx + 1) + ')'), label: labelOf(b), kind: 'submit' }));
            const form = document.querySelector('form');
            const formAction = form ? (form.getAttribute('action') || form.dataset.submit || '') : '';
            return { usernames, passwords, selects, submits, formAction };
        })()
        """;

    private static IReadOnlyList<PortalDiscoveredControl> ParseControls(JsonArray? arr)
    {
        if (arr is null) return [];
        var list = new List<PortalDiscoveredControl>();
        foreach (var item in arr.OfType<JsonObject>())
        {
            var selector = item["selector"]?.GetValue<string>() ?? "";
            var label = item["label"]?.GetValue<string>() ?? selector;
            var kind = item["kind"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(selector)) continue;
            List<PortalSelectOption>? options = null;
            if (item["options"] is JsonArray optArray)
            {
                options = [];
                foreach (var opt in optArray.OfType<JsonObject>())
                {
                    options.Add(new PortalSelectOption(
                        opt["value"]?.GetValue<string>() ?? "",
                        opt["text"]?.GetValue<string>() ?? ""));
                }
            }
            list.Add(new PortalDiscoveredControl(selector, label, kind, options));
        }
        return list;
    }

    private static bool ContainsAny(string text, params string[] markers) =>
        markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
}
