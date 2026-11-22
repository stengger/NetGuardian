using System;
using System.Collections.Generic;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Presentation;

internal enum ConnectivityReportOrigin { Manual, Foreground, Background }

internal sealed record DashboardMeasurement(ConnectivityReport Report, ConnectivityReportOrigin Origin,
    ProbeTimingSummary Timing, double? LastValidMs, DateTimeOffset? LastValidAt)
{
    public bool Expired(DateTimeOffset now, int intervalSeconds) => now - Report.CheckedAt >=
        TimeSpan.FromSeconds(Math.Clamp(3d * intervalSeconds, 300, 1800));
}

internal sealed class ConnectivityReportSelection
{
    private readonly Dictionary<string, DashboardMeasurement> _measurements = new();
    private DateTimeOffset _networkChangedAt;
    public ConnectivityReport? Report { get; private set; }
    public ConnectivityReportOrigin Origin { get; private set; }
    public string? KnownSsid { get; private set; }
    public DashboardMeasurement? Measurement(string kind) => _measurements.GetValueOrDefault(kind);

    public void NetworkChanged(DateTimeOffset changedAt)
    {
        if (changedAt <= _networkChangedAt) return;
        _networkChangedAt = changedAt;
        _measurements.Clear();
        KnownSsid = null;
    }

    public bool TryAccept(ConnectivityReport report, ConnectivityReportOrigin origin)
    {
        if ((report.SampleStartedAt ?? report.CheckedAt) < _networkChangedAt || Report is not null && (report.CheckedAt < Report.CheckedAt ||
            report.CheckedAt == Report.CheckedAt && origin == ConnectivityReportOrigin.Background && Origin != origin)) return false;
        if (string.IsNullOrEmpty(report.NetworkContext) || report.NetworkContext != Report?.NetworkContext ||
            KnownSsid is not null && report.CurrentSsid is not null && KnownSsid != report.CurrentSsid)
        {
            _measurements.Clear();
            KnownSsid = null;
        }
        KnownSsid = report.CurrentSsid ?? KnownSsid;
        Report = report;
        Origin = origin;
        var summary = ConnectivityQualitySummary.From(report);
        Record("Gateway", summary.Gateway);
        Record("Dns", summary.Dns);
        Record("Https", summary.Https);
        return true;

        void Record(string kind, ProbeTimingSummary timing)
        {
            if (timing.Attempted == 0) return;
            var previous = Measurement(kind);
            _measurements[kind] = new(report, origin, timing, timing.AverageMs ?? previous?.LastValidMs,
                timing.AverageMs.HasValue ? report.CheckedAt : previous?.LastValidAt);
        }
    }
}
