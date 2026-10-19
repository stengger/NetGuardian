using System;
using System.Collections.Generic;
using System.Linq;

namespace NetGuardian.App.Presentation;

internal sealed record NetworkByteCounter(string Id, long ReceivedBytes, long SentBytes);
internal sealed record NetworkTrafficRate(double DownloadBytesPerSecond, double UploadBytesPerSecond, int SampledInterfaces, int CurrentInterfaces);

internal sealed class NetworkTrafficSampler
{
    private Dictionary<string, NetworkByteCounter> _previous = new(StringComparer.Ordinal);

    public NetworkTrafficRate? Sample(IReadOnlyList<NetworkByteCounter> current, TimeSpan elapsed)
    {
        var received = 0d;
        var sent = 0d;
        var sampled = 0;
        if (elapsed.TotalSeconds > 0 && double.IsFinite(elapsed.TotalSeconds))
        {
            foreach (var counter in current)
            {
                if (!_previous.TryGetValue(counter.Id, out var previous)
                    || counter.ReceivedBytes < previous.ReceivedBytes || counter.SentBytes < previous.SentBytes)
                    continue;
                received += counter.ReceivedBytes - previous.ReceivedBytes;
                sent += counter.SentBytes - previous.SentBytes;
                sampled++;
            }
        }
        _previous = current.ToDictionary(counter => counter.Id, StringComparer.Ordinal);
        return sampled == 0 ? null : new(received / elapsed.TotalSeconds, sent / elapsed.TotalSeconds, sampled, current.Count);
    }
}
