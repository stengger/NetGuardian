using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using NetGuardian.Core.Models;

namespace NetGuardian.Core.Connectivity;

internal static partial class PortalProbe
{
    internal static readonly Uri Endpoint = new("http://www.msftconnecttest.com/connecttest.txt");
    private const int MaxHtmlBytes = 64 * 1024;

    internal static async Task<PortalEvidence> InspectAsync(HttpClient client, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        token.ThrowIfCancellationRequested();
        if ((int)response.StatusCode == 511) return PortalEvidence.AuthenticationRequired;
        if (IsRedirect(response.StatusCode))
            return ResolveValidDestination(Endpoint, response.Headers.Location) is not null
                ? PortalEvidence.Redirect : PortalEvidence.None;
        // 响应头只作为线索。探测不读取页面正文，也不跟随跳转。
        if (response.StatusCode == HttpStatusCode.OK
            && string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
            return PortalEvidence.Html;
        return PortalEvidence.None;
    }

    internal static async Task<PortalDiscoveryResult> DiscoverAsync(
        HttpClient client,
        CancellationToken token,
        Uri? probeEndpoint = null)
    {
        var target = probeEndpoint ?? Endpoint;
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        token.ThrowIfCancellationRequested();

        if ((int)response.StatusCode == 511)
        {
            var destination = ResolveValidDestination(target, response.Headers.Location);
            return new PortalDiscoveryResult(PortalEvidence.AuthenticationRequired, destination, PortalDiscoveryResult.SanitizeUri(destination));
        }

        if (IsRedirect(response.StatusCode))
        {
            var destination = ResolveValidDestination(target, response.Headers.Location);
            return destination is not null
                ? new PortalDiscoveryResult(PortalEvidence.Redirect, destination, PortalDiscoveryResult.SanitizeUri(destination))
                : PortalDiscoveryResult.Empty;
        }

        if (response.StatusCode == HttpStatusCode.OK
            && string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            var html = await ReadBoundedTextAsync(response.Content, MaxHtmlBytes, token);
            var destination = ExtractHtmlRedirect(target, html);
            return new PortalDiscoveryResult(PortalEvidence.Html, destination, PortalDiscoveryResult.SanitizeUri(destination));
        }

        return PortalDiscoveryResult.Empty;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static Uri? ResolveValidDestination(Uri baseUri, Uri? location)
    {
        if (location is null) return null;
        if (!Uri.TryCreate(baseUri, location, out var destination)) return null;
        if (destination.Scheme is not ("http" or "https") || destination.UserInfo.Length > 0) return null;
        if (Uri.Compare(baseUri, destination, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped, StringComparison.Ordinal) == 0)
            return null;
        return destination;
    }

    private static async Task<string> ReadBoundedTextAsync(HttpContent content, int maxBytes, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token);
        var buffer = new byte[4096];
        using var ms = new MemoryStream(Math.Min(maxBytes, 8192));
        var remaining = maxBytes;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), token);
            if (read == 0) break;
            ms.Write(buffer, 0, read);
            remaining -= read;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static Uri? ExtractHtmlRedirect(Uri baseUri, string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var metaMatch = MetaRefreshRegex().Match(html);
        if (metaMatch.Success)
        {
            var raw = WebUtility.HtmlDecode(metaMatch.Groups["url"].Value.Trim().Trim('\'', '"'));
            if (TryResolveRedirectString(baseUri, raw, out var metaUri)) return metaUri;
        }

        var jsMatch = JsRedirectRegex().Match(html);
        if (jsMatch.Success)
        {
            var raw = WebUtility.HtmlDecode(jsMatch.Groups["url"].Value.Trim());
            if (TryResolveRedirectString(baseUri, raw, out var jsUri)) return jsUri;
        }

        return null;
    }

    private static bool TryResolveRedirectString(Uri baseUri, string raw, out Uri? resolved)
    {
        resolved = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!Uri.TryCreate(baseUri, raw, out var candidate)) return false;
        resolved = ResolveValidDestination(baseUri, candidate);
        return resolved is not null;
    }

    [GeneratedRegex(@"<meta[^>]+http-equiv\s*=\s*[""']?refresh[""']?[^>]+content\s*=\s*[""'][^""']*?\burl\s*=\s*(?<url>[^""'>\s]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MetaRefreshRegex();

    [GeneratedRegex(@"(?:window\.|self\.|top\.)?location(?:\.href)?\s*=\s*[""'](?<url>[^""']+)[""']|location\.(?:replace|assign)\(\s*[""'](?<url>[^""']+)[""']\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex JsRedirectRegex();
}
