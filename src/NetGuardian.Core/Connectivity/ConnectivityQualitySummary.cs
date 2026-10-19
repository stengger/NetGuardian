using NetGuardian.Core.Models;

namespace NetGuardian.Core.Connectivity;

public sealed record ProbeTimingSummary(int Attempted, int Succeeded, int Measured, double? AverageMs);

public sealed record ConnectivityQualitySummary(int PassedChecks, int TotalChecks,
    ProbeTimingSummary Gateway, ProbeTimingSummary Dns, ProbeTimingSummary Https)
{
    public static ConnectivityQualitySummary From(ConnectivityReport report) => new(
        report.Probes.Count(probe => probe.Ok), report.Probes.Count,
        Summarize(report.Probes.Where(probe => probe.Name.Equals("Gateway ping", StringComparison.OrdinalIgnoreCase))),
        Summarize(report.Probes.Where(probe => probe.Name.StartsWith("DNS ", StringComparison.OrdinalIgnoreCase))),
        Summarize(report.Probes.Where(probe => probe.Name.StartsWith("HTTPS ", StringComparison.OrdinalIgnoreCase))));

    private static ProbeTimingSummary Summarize(IEnumerable<ConnectivityProbe> probes)
    {
        var attempts = probes.ToList();
        var samples = attempts.Where(probe => probe.Ok && probe.LatencyMs is { } value && double.IsFinite(value) && value >= 0)
            .Select(probe => probe.LatencyMs!.Value).ToList();
        return new(attempts.Count, attempts.Count(probe => probe.Ok), samples.Count, samples.Count > 0 ? samples.Average() : null);
    }
}
