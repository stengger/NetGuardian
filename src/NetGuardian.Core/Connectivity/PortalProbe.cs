using System.Net;
using NetGuardian.Core.Models;

namespace NetGuardian.Core.Connectivity;

internal static class PortalProbe
{
    internal static readonly Uri Endpoint = new("http://www.msftconnecttest.com/connecttest.txt");

    internal static async Task<PortalEvidence> InspectAsync(HttpClient client, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        token.ThrowIfCancellationRequested();
        if ((int)response.StatusCode == 511) return PortalEvidence.AuthenticationRequired;
        if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
        {
            return response.Headers.Location is { } location
                && Uri.TryCreate(Endpoint, location, out var destination)
                && destination.Scheme is "http" or "https"
                && destination.UserInfo.Length == 0
                && Uri.Compare(Endpoint, destination, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped, StringComparison.Ordinal) != 0
                    ? PortalEvidence.Redirect : PortalEvidence.None;
        }
        // 响应头只作为线索。探测不读取页面正文，也不跟随跳转。
        if (response.StatusCode == HttpStatusCode.OK
            && string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
            return PortalEvidence.Html;
        return PortalEvidence.None;
    }
}
