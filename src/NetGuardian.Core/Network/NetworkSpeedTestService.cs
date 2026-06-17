using System.Diagnostics;
using System.Net.Http;

namespace NetGuardian.Core.Network;

public sealed class NetworkSpeedTestService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<NetworkSpeedTestResult> RunAsync(
        IProgress<NetworkSpeedTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var latency = await MeasureLatencyAsync(progress, cancellationToken);
        var download = await MeasureDownloadAsync(progress, cancellationToken);
        var upload = await MeasureUploadAsync(progress, cancellationToken);
        return new NetworkSpeedTestResult(download.Mbps, upload.Mbps, latency, download.Bytes, upload.Bytes, DateTimeOffset.Now);
    }

    private static async Task<double?> MeasureLatencyAsync(IProgress<NetworkSpeedTestProgress>? progress, CancellationToken cancellationToken)
    {
        var samples = new List<double>();
        foreach (var url in new[] { "https://www.microsoft.com/", "https://www.baidu.com/" })
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                stopwatch.Stop();
                if ((int)response.StatusCode is >= 200 and < 500)
                {
                    samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                stopwatch.Stop();
            }
        }

        double? latency = samples.Count == 0 ? null : samples.Average();
        progress?.Report(new NetworkSpeedTestProgress("Latency", null, latency));
        return latency;
    }

    private static async Task<(double Mbps, long Bytes)> MeasureDownloadAsync(IProgress<NetworkSpeedTestProgress>? progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://speed.cloudflare.com/__down?bytes=5000000");
        using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[64 * 1024];
        long bytes = 0;
        var stopwatch = Stopwatch.StartNew();
        while (bytes < 5_000_000)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            bytes += read;
            progress?.Report(new NetworkSpeedTestProgress("Download", ToMbps(bytes, stopwatch.Elapsed), null));
        }

        stopwatch.Stop();
        var mbps = ToMbps(bytes, stopwatch.Elapsed);
        progress?.Report(new NetworkSpeedTestProgress("Download", mbps, null));
        return (mbps, bytes);
    }

    private static async Task<(double Mbps, long Bytes)> MeasureUploadAsync(IProgress<NetworkSpeedTestProgress>? progress, CancellationToken cancellationToken)
    {
        var payload = new byte[512 * 1024];
        Random.Shared.NextBytes(payload);
        using var content = new ByteArrayContent(payload);
        var stopwatch = Stopwatch.StartNew();
        using var response = await SharedHttpClient.PostAsync("https://postman-echo.com/post", content, cancellationToken);
        stopwatch.Stop();
        response.EnsureSuccessStatusCode();
        var mbps = ToMbps(payload.Length, stopwatch.Elapsed);
        progress?.Report(new NetworkSpeedTestProgress("Upload", mbps, null));
        return (mbps, payload.Length);
    }

    private static double ToMbps(long bytes, TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= 0 || bytes <= 0)
        {
            return 0;
        }

        return bytes * 8d / elapsed.TotalSeconds / 1_000_000d;
    }
}

public sealed record NetworkSpeedTestResult(
    double DownloadMbps,
    double UploadMbps,
    double? LatencyMs,
    long DownloadBytes,
    long UploadBytes,
    DateTimeOffset CheckedAt);

public sealed record NetworkSpeedTestProgress(string Phase, double? Mbps, double? LatencyMs);
