using System.Net;

namespace NetGuardian.Core.Updates;

// 备用线路禁止跳出项目域名。主源 CDN 的额外 HTTPS 源必须在客户端显式授权。
internal sealed class UpdateRouteHandler(UpdateEndpoint endpoint, bool primary, Func<bool> permitted, HttpMessageHandler inner)
    : DelegatingHandler(inner)
{
    internal static bool Secure(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri!;
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            if (!permitted()) throw new HttpRequestException("管理员路由策略禁止备用源直连。");
            if (!Secure(uri) || !(SameOrigin(uri, endpoint.Manifest) || SameOrigin(uri, endpoint.PackageRoot) ||
                primary && endpoint.RedirectOrigins.Any(origin => SameOrigin(uri, origin))))
                throw SignedManifest.Security("更新线路跳转到了未经授权的地址。");
            using var next = new HttpRequestMessage(HttpMethod.Get, uri);
            foreach (var header in request.Headers) next.Headers.TryAddWithoutValidation(header.Key, header.Value);
            var response = await base.SendAsync(next, token).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return response;
            using (response)
            {
                if (response.Headers.Location is not { } location || !Uri.TryCreate(uri, location, out var redirected))
                    throw SignedManifest.Security("更新线路的跳转地址无效。");
                uri = redirected;
            }
        }
        throw new HttpRequestException("更新线路的跳转次数过多。");
    }

    internal static bool SameOrigin(Uri first, Uri second) => first.Scheme == second.Scheme &&
        first.IdnHost.Equals(second.IdnHost, StringComparison.OrdinalIgnoreCase) && first.Port == second.Port;
}
