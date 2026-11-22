using NetGuardian.Core.Models;

namespace NetGuardian.Core.Monitoring;

public sealed partial class MonitoringEngine
{
    internal event Action<IncidentStepKind, string?, string?, ConnectivityReport?, bool>? IncidentObserved;
    // 失败时按快检间隔调度；单轮探测/恢复可用掉整个 CheckTimeout。
    internal int ObservationBudgetSeconds => (int)Math.Ceiling(CheckTimeout.TotalSeconds) +
        Math.Clamp(_policyProvider().CheckIntervalSeconds, 1, 10) + 2;

    private void Observe(IncidentStepKind kind, string? target = null, string? detail = null,
        ConnectivityReport? report = null, bool afterAction = false) =>
        IncidentObserved?.Invoke(kind, target, detail, report, afterAction);

    private void ObserveReport(ConnectivityReport report, bool afterAction = false, string? detail = null)
    {
        if (IncidentObserved is null) return;
        var kind = IncidentStepKind.Inconclusive;
        if (report.Ok && report.Probes.Any(p => p.Ok && p.Name.StartsWith("HTTPS ", StringComparison.Ordinal)))
            kind = IncidentStepKind.PublicVerified;
        else if (!report.IsLightweight && report.State is NetworkState.AdapterFault or NetworkState.WifiDisabled
            or NetworkState.WifiDisconnected or NetworkState.Disconnected or NetworkState.DnsIssue or NetworkState.InternetIssue)
            kind = IncidentStepKind.Failure;
        Observe(kind, report.CurrentSsid, detail ?? $"{report.State} · {report.Summary}", report, afterAction);
    }
}
