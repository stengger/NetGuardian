using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetGuardian.Core.Models;

namespace NetGuardian.Core.Portal;

internal sealed record PortalForwardedRequest(
    string Method,
    Uri Url,
    IReadOnlyDictionary<string, string> Headers,
    string? PostData);

internal sealed record PortalForwardedResponse(
    bool Allowed,
    int StatusCode,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    byte[] Body,
    string? BlockReason = null);

/// <summary>
/// 严格执行网页认证请求的白名单过滤、占位符凭据替换和脱敏响应判定。
/// 与浏览器隔离运行，确保未授权的外发请求与凭据篡改均在网络发送前被阻断。
/// </summary>
internal sealed class PortalHttpForwarder
{
    private const int MaxResponseBytes = 256 * 1024;
    private static readonly HashSet<string> SkippedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Connection", "Proxy-Connection", "Transfer-Encoding", "Accept-Encoding"
    };

    private static readonly string[] AccountExpiredMarkers =
    [
        "套餐到期", "已过期", "余额不足", "欠费", "停机", "账户已停用", "账号已停用",
        "account expired", "subscription expired", "overdue", "insufficient balance"
    ];

    private static readonly string[] CredentialRejectedMarkers =
    [
        "密码错误", "用户名或密码", "账号或密码", "认证失败", "登录失败", "无效的密码", "密码不正确",
        "rejected", "invalid password", "wrong password", "invalid username", "authentication failed", "login failed"
    ];

    private readonly PortalAuthConfig _config;
    private readonly HttpClient _httpClient;
    private readonly Func<Uri, Uri>? _uriMapper;
    private readonly IReadOnlyDictionary<string, string> _markerToRealValue;
    private readonly string? _usernameFieldName;
    private readonly string? _passwordFieldName;
    private readonly bool _inspectOnly;

    public PortalHttpForwarder(
        PortalAuthConfig config,
        HttpClient httpClient,
        IReadOnlyDictionary<string, string>? markerToRealValue = null,
        bool inspectOnly = false,
        Func<Uri, Uri>? uriMapper = null)
    {
        _config = config;
        _httpClient = httpClient;
        _markerToRealValue = markerToRealValue ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _usernameFieldName = config.UsernameFieldName;
        _passwordFieldName = config.PasswordFieldName;
        _inspectOnly = inspectOnly;
        _uriMapper = uriMapper;
    }

    public int BlockedRequests { get; private set; }
    public bool SubmitAttempted { get; private set; }
    public bool SubmitCompleted { get; private set; }
    public bool SubmitTargetBlocked { get; private set; }
    public bool PayloadTransformed { get; private set; }
    public bool TlsOrTransportFailed { get; private set; }
    public int? LastSubmitStatusCode { get; private set; }
    public PortalAuthOutcome? RejectionOutcome { get; private set; }
    public string? FailureDetail { get; private set; }

    public async Task<PortalForwardedResponse> ForwardAsync(PortalForwardedRequest request, CancellationToken token)
    {
        var uri = request.Url;
        if (!uri.IsAbsoluteUri || uri.UserInfo.Length > 0
            || !(uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                 || (_config.AllowInsecureHttp && uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))))
        {
            BlockedRequests++;
            return Denied("不允许的协议或非法请求地址。");
        }

        var method = request.Method.ToUpperInvariant();
        var isSubmission = method is "POST" or "PUT" or "PATCH";

        if (_inspectOnly && isSubmission)
        {
            BlockedRequests++;
            return Denied("探测阶段禁止提交表单。");
        }

        if (isSubmission)
        {
            // 每轮对同一认证站点最多放行一次提交请求。
            if (SubmitAttempted)
            {
                BlockedRequests++;
                return Denied("本轮认证已发出过提交请求，阻断额外上传。");
            }

            if (!_config.MatchesSubmitUri(uri))
            {
                BlockedRequests++;
                SubmitTargetBlocked = true;
                FailureDetail ??= $"提交目标与已验证配置不符：{PortalDiscoveryResult.SanitizeUri(uri)}";
                return Denied(FailureDetail);
            }
        }
        else if (method is not ("GET" or "HEAD"))
        {
            BlockedRequests++;
            return Denied($"不支持的请求方法：{method}");
        }
        else
        {
            if (!IsAllowedReadOrigin(uri))
            {
                BlockedRequests++;
                return Denied($"已拦截跨站静态资源或跳转：{PortalDiscoveryResult.SanitizeUri(uri)}");
            }
        }

        string? outgoingBody = request.PostData;
        string? outgoingContentType = GetHeader(request.Headers, "Content-Type");

        if (isSubmission && _markerToRealValue.Count > 0)
        {
            if (!TrySubstituteMarkers(outgoingContentType, request.PostData, out var replacedBody, out var normalizedType))
            {
                PayloadTransformed = true;
                BlockedRequests++;
                FailureDetail ??= "页面在提交前变换或破坏了凭据字段，已终止自动登录。";
                return Denied(FailureDetail);
            }
            outgoingBody = replacedBody;
            outgoingContentType = normalizedType;
        }

        if (isSubmission) SubmitAttempted = true;

        var targetUri = _uriMapper is not null ? _uriMapper(uri) : uri;
        using var message = new HttpRequestMessage(new HttpMethod(method), targetUri);

        foreach (var (name, value) in request.Headers)
        {
            if (SkippedRequestHeaders.Contains(name)) continue;
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            message.Headers.TryAddWithoutValidation(name, value);
        }

        if (isSubmission && outgoingBody is not null)
        {
            var mediaType = ExtractMediaType(outgoingContentType) ?? "application/x-www-form-urlencoded";
            message.Content = new StringContent(outgoingBody, Encoding.UTF8, mediaType);
        }

        try
        {
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
            var body = await ReadBoundedBytesAsync(response.Content, MaxResponseBytes, token);
            var responseHeaders = new List<KeyValuePair<string, string>>();
            foreach (var h in response.Headers)
            {
                if (h.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var v in h.Value) responseHeaders.Add(new KeyValuePair<string, string>(h.Key, v));
                }
            }
            foreach (var h in response.Content.Headers)
            {
                if (h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var v in h.Value) responseHeaders.Add(new KeyValuePair<string, string>(h.Key, v));
                }
            }
            if (!responseHeaders.Any(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
                responseHeaders.Add(new KeyValuePair<string, string>("Content-Type", "text/plain; charset=utf-8"));

            if (isSubmission)
            {
                SubmitCompleted = true;
                LastSubmitStatusCode = (int)response.StatusCode;
                AnalyzeSubmissionResponse(response.StatusCode, body);
            }

            return new PortalForwardedResponse(true, (int)response.StatusCode, responseHeaders, body);
        }
        catch (HttpRequestException ex)
        {
            TlsOrTransportFailed = true;
            FailureDetail ??= ex.InnerException is AuthenticationException
                ? "门户证书校验失败。"
                : $"门户网络请求失败：{ex.Message}";
            return Denied(FailureDetail);
        }
        catch (TaskCanceledException) when (!token.IsCancellationRequested)
        {
            FailureDetail ??= "门户请求超时。";
            return Denied(FailureDetail);
        }
    }

    private bool IsAllowedReadOrigin(Uri uri)
    {
        if (!PortalAuthConfig.TryNormalizeOrigin(_config.AllowedOrigin, _config.AllowInsecureHttp, out var loginOrigin))
            return false;
        var currentOrigin = PortalAuthConfig.FormatOrigin(uri);
        if (string.Equals(currentOrigin, loginOrigin, StringComparison.OrdinalIgnoreCase)) return true;

        if (!string.IsNullOrWhiteSpace(_config.AllowedSubmitOrigin)
            && PortalAuthConfig.TryNormalizeOrigin(_config.AllowedSubmitOrigin, _config.AllowInsecureHttp, out var submitOrigin)
            && string.Equals(currentOrigin, submitOrigin, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private bool TrySubstituteMarkers(string? contentType, string? postData, out string replacedBody, out string normalizedType)
    {
        replacedBody = postData ?? "";
        normalizedType = contentType ?? "application/x-www-form-urlencoded";
        var mediaType = ExtractMediaType(contentType);
        if (string.IsNullOrEmpty(postData)) return false;

        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            normalizedType = "application/json";
            return TrySubstituteJson(postData, out replacedBody);
        }

        if (mediaType is null || string.Equals(mediaType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            normalizedType = "application/x-www-form-urlencoded";
            return TrySubstituteForm(postData, out replacedBody);
        }

        return false;
    }

    private bool TrySubstituteForm(string postData, out string replacedBody)
    {
        replacedBody = "";
        var pairs = ParseFormUrlEncoded(postData);
        var matchedMarkers = new HashSet<string>(StringComparer.Ordinal);
        var updated = new List<KeyValuePair<string, string>>(pairs.Count);

        foreach (var (key, value) in pairs)
        {
            var newValue = value;
            var exactMatched = false;

            foreach (var (marker, realValue) in _markerToRealValue)
            {
                if (string.Equals(value, marker, StringComparison.Ordinal))
                {
                    if (!IsExpectedFieldName(key, marker)) return false;
                    newValue = realValue;
                    matchedMarkers.Add(marker);
                    exactMatched = true;
                    break;
                }

                if (value.Contains(marker, StringComparison.Ordinal))
                    return false;
            }

            if (!exactMatched && value.Contains("ng-user-", StringComparison.Ordinal)) return false;
            if (!exactMatched && value.Contains("ng-password-", StringComparison.Ordinal)) return false;

            updated.Add(new KeyValuePair<string, string>(key, newValue));
        }

        if (matchedMarkers.Count != _markerToRealValue.Count) return false;
        replacedBody = string.Join("&", updated.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return true;
    }

    private bool TrySubstituteJson(string postData, out string replacedBody)
    {
        replacedBody = "";
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(postData);
        }
        catch (JsonException)
        {
            return false;
        }

        if (root is not JsonObject obj) return false;
        var matchedMarkers = new HashSet<string>(StringComparer.Ordinal);
        if (!VisitJsonObject(obj, matchedMarkers)) return false;
        if (matchedMarkers.Count != _markerToRealValue.Count) return false;

        replacedBody = obj.ToJsonString();
        return true;
    }

    private bool VisitJsonObject(JsonObject obj, HashSet<string> matchedMarkers)
    {
        foreach (var kv in obj.ToList())
        {
            if (kv.Value is JsonValue val && val.TryGetValue<string>(out var str))
            {
                foreach (var (marker, realValue) in _markerToRealValue)
                {
                    if (string.Equals(str, marker, StringComparison.Ordinal))
                    {
                        if (!IsExpectedFieldName(kv.Key, marker)) return false;
                        obj[kv.Key] = realValue;
                        matchedMarkers.Add(marker);
                        goto NextKey;
                    }
                    if (str.Contains(marker, StringComparison.Ordinal)) return false;
                }
            }
            else if (kv.Value is JsonObject childObj)
            {
                if (!VisitJsonObject(childObj, matchedMarkers)) return false;
            }
            NextKey:;
        }
        return true;
    }

    private bool IsExpectedFieldName(string actualKey, string marker)
    {
        if (marker.StartsWith("ng-user-", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(_usernameFieldName))
            return string.Equals(actualKey, _usernameFieldName, StringComparison.Ordinal);
        if (marker.StartsWith("ng-password-", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(_passwordFieldName))
            return string.Equals(actualKey, _passwordFieldName, StringComparison.Ordinal);
        return true;
    }

    private void AnalyzeSubmissionResponse(HttpStatusCode statusCode, byte[] body)
    {
        var text = body.Length > 0 ? Encoding.UTF8.GetString(body) : "";
        if (AccountExpiredMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            RejectionOutcome = PortalAuthOutcome.AccountExpired;
            FailureDetail = "门户返回套餐到期或欠费提示，已暂停自动提交。";
            return;
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || CredentialRejectedMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            RejectionOutcome = PortalAuthOutcome.CredentialsRejected;
            FailureDetail = $"门户拒绝登录（HTTP {(int)statusCode}），已暂停自动提交。";
        }
    }

    private static List<KeyValuePair<string, string>> ParseFormUrlEncoded(string query)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = segment.IndexOf('=');
            if (idx < 0)
            {
                result.Add(new KeyValuePair<string, string>(DecodeFormComponent(segment), ""));
            }
            else
            {
                result.Add(new KeyValuePair<string, string>(
                    DecodeFormComponent(segment[..idx]),
                    DecodeFormComponent(segment[(idx + 1)..])));
            }
        }
        return result;
    }

    private static string DecodeFormComponent(string value) =>
        Uri.UnescapeDataString(value.Replace('+', ' '));

    private static string? GetHeader(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.FirstOrDefault(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? ExtractMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        return MediaTypeHeaderValue.TryParse(contentType, out var parsed)
            ? parsed.MediaType
            : contentType.Split(';', 2)[0].Trim();
    }

    private static async Task<byte[]> ReadBoundedBytesAsync(HttpContent content, int maxBytes, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token);
        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        var remaining = maxBytes;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), token);
            if (read == 0) break;
            ms.Write(buffer, 0, read);
            remaining -= read;
        }
        return ms.ToArray();
    }

    private static PortalForwardedResponse Denied(string reason) =>
        new(false, 0, [], [], reason);
}
