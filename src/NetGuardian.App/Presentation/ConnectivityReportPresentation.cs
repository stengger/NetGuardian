using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Presentation;

internal static class ConnectivityReportPresentation
{
    public static string Context(ConnectivityReport report, ConnectivityReportOrigin origin, Localizer localizer)
    {
        var source = localizer.T(origin switch
        {
            ConnectivityReportOrigin.Manual => "ReportSourceManual",
            ConnectivityReportOrigin.Background => "ReportSourceBackground",
            _ => "ReportSourceForeground"
        });
        var kind = localizer.T(report.IsLightweight ? "LightweightCheck" : "FullDiagnosticCheck");
        var context = string.Format(localizer.T("ConnectivityReportContext"), $"{source} · {kind}",
            report.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        return report.PortalEvidence == PortalEvidence.None ? context : $"{context} · {localizer.T("SuspectedWifiPortal")}";
    }
}
