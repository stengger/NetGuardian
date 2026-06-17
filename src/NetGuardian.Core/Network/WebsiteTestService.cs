using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace NetGuardian.Core.Network;

public sealed class WebsiteTestService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task<WebsiteTestResult> TestAsync(string name, string url, CancellationToken cancellationToken = default)
    {
        var normalizedUrl = NormalizeUrl(url);
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return new WebsiteTestResult(name, normalizedUrl, "", "", false, null, null, null, "Invalid URL");
        }

        var dnsStopwatch = Stopwatch.StartNew();
        string addressText;
        double? dnsLatencyMs;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            dnsStopwatch.Stop();
            dnsLatencyMs = dnsStopwatch.Elapsed.TotalMilliseconds;
            addressText = addresses.FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork)?.ToString()
                ?? addresses.FirstOrDefault()?.ToString()
                ?? "-";
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            dnsStopwatch.Stop();
            return new WebsiteTestResult(name, normalizedUrl, uri.Host, "", false, null, dnsStopwatch.Elapsed.TotalMilliseconds, null, ex.Message);
        }

        var requestStopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            requestStopwatch.Stop();
            var ok = (int)response.StatusCode is >= 200 and < 500;
            return new WebsiteTestResult(
                name,
                normalizedUrl,
                uri.Host,
                addressText,
                ok,
                (int)response.StatusCode,
                dnsLatencyMs,
                requestStopwatch.Elapsed.TotalMilliseconds,
                response.ReasonPhrase ?? "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            requestStopwatch.Stop();
            return new WebsiteTestResult(name, normalizedUrl, uri.Host, addressText, false, null, dnsLatencyMs, requestStopwatch.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    public static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }
}

public sealed record WebsiteTestResult(
    string Name,
    string Url,
    string Host,
    string Address,
    bool Success,
    int? StatusCode,
    double? DnsLatencyMs,
    double? HttpLatencyMs,
    string Detail);
