using NetGuardian.Core.Models;

namespace NetGuardian.App.Presentation;

internal enum ConnectivityReportOrigin { Manual, Foreground, Background }

internal sealed class ConnectivityReportSelection
{
    public ConnectivityReport? Report { get; private set; }
    public ConnectivityReportOrigin Origin { get; private set; }

    public bool TryAccept(ConnectivityReport report, ConnectivityReportOrigin origin)
    {
        if (Report is not null && (report.CheckedAt < Report.CheckedAt ||
            report.CheckedAt == Report.CheckedAt && origin == ConnectivityReportOrigin.Background && Origin != origin)) return false;
        Report = report;
        Origin = origin;
        return true;
    }
}
