using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NetGuardian.Core.Network;

public sealed class NetworkSpeedTestService
{
    private const long DownloadBytes = 5_000_000;
    private const long UploadBytes = 1_000_000;

    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly HttpClient _client;

    public NetworkSpeedTestService() : this(SharedHttpClient) { }

    public NetworkSpeedTestService(HttpClient client) => _client = client;

    public async Task<NetworkSpeedTestResult> RunAsync(
        IProgress<NetworkSpeedTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new("Latency", null, null));
        var latency = await MeasureLatencyAsync(progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new("Download", null, null));
        var download = await MeasureDownloadAsync(progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new("Upload", null, null));
        var upload = await MeasureUploadAsync(progress, cancellationToken);
        return new NetworkSpeedTestResult(download.Mbps, upload.Mbps, latency.Ms, download.Bytes, upload.Bytes, DateTimeOffset.Now, latency.Count);
    }

    private async Task<(double? Ms, int Count)> MeasureLatencyAsync(IProgress<NetworkSpeedTestProgress>? progress, CancellationToken cancellationToken)
    {
        var samples = new List<double>();
        foreach (var url in new[] { "https://www.microsoft.com/", "https://www.baidu.com/" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var deadline = CreateDeadline(cancellationToken);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                stopwatch.Stop();
                if ((int)response.StatusCode is >= 200 and < 500)
                {
                    samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stopwatch.Stop();
            }
        }

        double? latency = samples.Count == 0 ? null : samples.Average();
        progress?.Report(new NetworkSpeedTestProgress("Latency", null, latency, true, samples.Count));
        return (latency, samples.Count);
    }

    private async Task<(double Mbps, long Bytes)> MeasureDownloadAsync(IProgress<NetworkSpeedTestProgress>? progress, CancellationToken cancellationToken)
    {
        using var deadline = CreateDeadline(cancellationToken);
        var token = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://speed.cloudflare.com/__down?bytes={DownloadBytes}");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[64 * 1024];
        long bytes = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        while (bytes < DownloadBytes)
        {
            var read = await stream.ReadAsync(buffer, token);
            if (read == 0)
            {
                break;
            }

            bytes += read;
            if (stopwatch.Elapsed - lastReport >= TimeSpan.FromMilliseconds(150))
            {
                progress?.Report(new NetworkSpeedTestProgress("Download", ToMbps(bytes, stopwatch.Elapsed), null));
                lastReport = stopwatch.Elapsed;
            }
        }

        stopwatch.Stop();
        if (bytes != DownloadBytes) throw new IOException("下载响应未完整接收。");
        var mbps = ToMbps(bytes, stopwatch.Elapsed);
        progress?.Report(new NetworkSpeedTestProgress("Download", mbps, null, true));
        return (mbps, bytes);
    }

    private async Task<(double Mbps, long Bytes)> MeasureUploadAsync(IProgress<NetworkSpeedTestProgress>? progress, CancellationToken cancellationToken)
    {
        using var deadline = CreateDeadline(cancellationToken);
        await using var payload = new RepeatingPayloadStream(UploadBytes);
        using var content = new StreamContent(payload, 64 * 1024);
        content.Headers.ContentLength = UploadBytes;
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var stopwatch = Stopwatch.StartNew();
        using var response = await _client.PostAsync($"https://speed.cloudflare.com/__up?bytes={UploadBytes}", content, deadline.Token);
        stopwatch.Stop();
        response.EnsureSuccessStatusCode();
        var mbps = ToMbps(UploadBytes, stopwatch.Elapsed);
        progress?.Report(new NetworkSpeedTestProgress("Upload", mbps, null, true));
        return (mbps, UploadBytes);
    }

    private static CancellationTokenSource CreateDeadline(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // ResponseHeadersRead 之后的流读取也必须有截止时间。
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        return deadline;
    }

    private static double ToMbps(long bytes, TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= 0 || bytes <= 0)
        {
            return 0;
        }

        return bytes * 8d / elapsed.TotalSeconds / 1_000_000d;
    }

    private sealed class RepeatingPayloadStream : Stream
    {
        private static readonly byte[] Seed = CreateSeed();
        private readonly long _length;
        private long _position;

        public RepeatingPayloadStream(long length)
        {
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (_position >= _length)
            {
                return 0;
            }

            var remaining = _length - _position;
            var toRead = (int)Math.Min(buffer.Length, remaining);
            Fill(buffer[..toRead]);
            _position += toRead;
            return toRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<int>(cancellationToken)
                : Task.FromResult(Read(buffer.AsSpan(offset, count)));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return cancellationToken.IsCancellationRequested
                ? ValueTask.FromCanceled<int>(cancellationToken)
                : ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private static byte[] CreateSeed()
        {
            var seed = new byte[8192];
            Random.Shared.NextBytes(seed);
            return seed;
        }

        private static void Fill(Span<byte> destination)
        {
            while (!destination.IsEmpty)
            {
                var count = Math.Min(destination.Length, Seed.Length);
                Seed.AsSpan(0, count).CopyTo(destination);
                destination = destination[count..];
            }
        }
    }
}

public sealed record NetworkSpeedTestResult(
    double DownloadMbps,
    double UploadMbps,
    double? LatencyMs,
    long DownloadBytes,
    long UploadBytes,
    DateTimeOffset CheckedAt,
    int LatencySampleCount = 0);

public sealed record NetworkSpeedTestProgress(string Phase, double? Mbps, double? LatencyMs, bool IsPhaseComplete = false, int LatencySampleCount = 0);
