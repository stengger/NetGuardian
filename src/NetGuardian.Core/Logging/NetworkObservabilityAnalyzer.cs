using NetGuardian.Core.Models;

namespace NetGuardian.Core.Logging;

/// <summary>单个 Wi-Fi SSID 的断网/恢复稳定性排行统计。</summary>
public sealed record SsidStabilityStat(
    string Ssid,
    int OutageCount,
    int RecoveredCount,
    double? AverageRecoveryMinutes);

/// <summary>指定时间窗口（7 天 / 30 天）的 SLA 与 MTTR 汇总指标。</summary>
public sealed record NetworkSlaSummary(
    int WindowDays,
    int IncidentCount,
    int RecoveredCount,
    double AvailabilityPercent,
    double? MttrMinutes,
    double? LongestOutageMinutes,
    double RecoveryRatePercent,
    IReadOnlyList<SsidStabilityStat> WifiStabilityRanking);

/// <summary>趋势图单点（已按时间序排列）。</summary>
public sealed record QualityTrendPoint(
    DateTimeOffset Timestamp,
    double? HttpsAvgMs,
    double? DnsAvgMs,
    double? GatewayAvgMs,
    int? SignalPercent,
    string? Ssid,
    bool Ok);

/// <summary>延迟分位数与趋势图聚合结果。</summary>
public sealed record QualityTrendSummary(
    int SampleCount,
    double? P50HttpsMs,
    double? P95HttpsMs,
    double? AverageSignalPercent,
    IReadOnlyList<QualityTrendPoint> RecentPoints);

/// <summary>根据断网事件历史与质量基线数据计算 SLA、MTTR 与时序趋势。</summary>
public static class NetworkObservabilityAnalyzer
{
    public static NetworkSlaSummary ComputeSla(
        IReadOnlyList<IncidentSummary> incidents,
        int windowDays,
        DateTimeOffset? nowOverride = null)
    {
        var days = Math.Clamp(windowDays, 1, 90);
        var now = nowOverride ?? DateTimeOffset.Now;
        var windowStart = now.AddDays(-days);
        var windowDuration = now - windowStart;

        var inWindow = new List<IncidentSummary>();
        var intervals = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        var mttrDurations = new List<double>();
        var ssidBuckets = new Dictionary<string, List<(bool Recovered, double? DurationMin)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var incident in incidents)
        {
            var start = incident.StartedAt;
            var end = incident.EndedAt ?? now;
            if (end < start)
            {
                end = start;
            }

            if (end < windowStart || start > now)
            {
                continue;
            }

            inWindow.Add(incident);

            var clampedStart = start < windowStart ? windowStart : start;
            var clampedEnd = end > now ? now : end;
            if (clampedEnd > clampedStart)
            {
                intervals.Add((clampedStart, clampedEnd));
            }

            var isRecovered = incident.Outcome is IncidentOutcome.NaturalRecovery or IncidentOutcome.RecoveredAfterAction;
            double? recoveryMinutes = null;
            if (isRecovered && incident.EndedAt is { } ended)
            {
                var mins = Math.Max(0, (ended - incident.StartedAt).TotalMinutes);
                mttrDurations.Add(mins);
                recoveryMinutes = mins;
            }

            var recoveredTarget = incident.Steps
                .LastOrDefault(s => s.Kind is IncidentStepKind.PublicVerified or IncidentStepKind.WifiAssociated)?.Target;
            var ssid = !string.IsNullOrWhiteSpace(incident.OriginalNetwork)
                ? incident.OriginalNetwork.Trim()
                : (!string.IsNullOrWhiteSpace(recoveredTarget) ? recoveredTarget.Trim() : "(Wired / Unknown)");
            if (!ssidBuckets.TryGetValue(ssid, out var list))
            {
                list = [];
                ssidBuckets[ssid] = list;
            }
            list.Add((isRecovered, recoveryMinutes));
        }

        var mergedOutageSeconds = MergeAndSumSeconds(intervals);
        var windowSeconds = Math.Max(1.0, windowDuration.TotalSeconds);
        var availability = Math.Clamp((1.0 - (mergedOutageSeconds / windowSeconds)) * 100.0, 0.0, 100.0);

        double? longestOutageMin = inWindow.Count == 0
            ? null
            : inWindow.Max(i => Math.Max(0, ((i.EndedAt ?? now) - i.StartedAt).TotalMinutes));

        var recoveredCount = inWindow.Count(i => i.Outcome is IncidentOutcome.NaturalRecovery or IncidentOutcome.RecoveredAfterAction);
        var recoveryRate = inWindow.Count == 0
            ? 100.0
            : Math.Round(recoveredCount * 100.0 / inWindow.Count, 1);

        double? mttr = mttrDurations.Count > 0 ? Math.Round(mttrDurations.Average(), 1) : null;

        var ranking = ssidBuckets
            .Select(kvp =>
            {
                var recTimes = kvp.Value.Where(x => x.DurationMin.HasValue).Select(x => x.DurationMin!.Value).ToList();
                return new SsidStabilityStat(
                    kvp.Key,
                    kvp.Value.Count,
                    kvp.Value.Count(x => x.Recovered),
                    recTimes.Count > 0 ? Math.Round(recTimes.Average(), 1) : null);
            })
            .OrderByDescending(x => x.OutageCount)
            .ThenBy(x => x.Ssid, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new NetworkSlaSummary(
            WindowDays: days,
            IncidentCount: inWindow.Count,
            RecoveredCount: recoveredCount,
            AvailabilityPercent: Math.Round(availability, 2),
            MttrMinutes: mttr,
            LongestOutageMinutes: longestOutageMin is { } lo ? Math.Round(lo, 1) : null,
            RecoveryRatePercent: recoveryRate,
            WifiStabilityRanking: ranking);
    }

    public static QualityTrendSummary SummarizeQualityTrend(
        IReadOnlyList<QualityBaselineSample> samples,
        int maxPoints = 60)
    {
        if (samples.Count == 0)
        {
            return new QualityTrendSummary(0, null, null, null, []);
        }

        var httpsLatencies = samples
            .Where(s => s.HttpsAvgMs is { } ms && double.IsFinite(ms) && ms >= 0)
            .Select(s => s.HttpsAvgMs!.Value)
            .OrderBy(v => v)
            .ToList();

        var signals = samples
            .Where(s => s.SignalPercent is { } sig and >= 0 and <= 100)
            .Select(s => (double)s.SignalPercent!.Value)
            .ToList();

        var p50 = Percentile(httpsLatencies, 0.50);
        var p95 = Percentile(httpsLatencies, 0.95);
        double? avgSignal = signals.Count > 0 ? Math.Round(signals.Average(), 0) : null;

        var cap = Math.Clamp(maxPoints, 10, 200);
        var recent = samples.Count <= cap
            ? samples.ToList()
            : samples.Skip(samples.Count - cap).ToList();

        var points = recent
            .Select(s => new QualityTrendPoint(s.Timestamp, s.HttpsAvgMs, s.DnsAvgMs, s.GatewayAvgMs, s.SignalPercent, s.Ssid, s.Ok))
            .ToList();

        return new QualityTrendSummary(
            samples.Count,
            p50 is { } p50Val ? Math.Round(p50Val, 1) : null,
            p95 is { } p95Val ? Math.Round(p95Val, 1) : null,
            avgSignal,
            points);
    }

    private static double MergeAndSumSeconds(List<(DateTimeOffset Start, DateTimeOffset End)> intervals)
    {
        if (intervals.Count == 0)
        {
            return 0;
        }

        intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
        double total = 0;
        var currentStart = intervals[0].Start;
        var currentEnd = intervals[0].End;

        for (var i = 1; i < intervals.Count; i++)
        {
            var next = intervals[i];
            if (next.Start <= currentEnd)
            {
                if (next.End > currentEnd)
                {
                    currentEnd = next.End;
                }
            }
            else
            {
                total += (currentEnd - currentStart).TotalSeconds;
                currentStart = next.Start;
                currentEnd = next.End;
            }
        }

        total += (currentEnd - currentStart).TotalSeconds;
        return total;
    }

    private static double? Percentile(List<double> sorted, double quantile)
    {
        if (sorted.Count == 0)
        {
            return null;
        }
        if (sorted.Count == 1)
        {
            return sorted[0];
        }

        var index = quantile * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
        {
            return sorted[lower];
        }
        var fraction = index - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }
}
