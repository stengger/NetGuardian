using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;

namespace NetGuardian.App;

public partial class MainWindow
{
    private void RenderDashboardMetrics(ConnectivityReport? report)
    {
        var summary = report is null ? null : ConnectivityQualitySummary.From(report);
        DashboardPresentation.RenderTiming(HomeGatewayValue, HomeGatewaySamples, summary?.Gateway, "Gateway", _localizer);
        DashboardPresentation.RenderTiming(HomeDnsValue, HomeDnsSamples, summary?.Dns, "Dns", _localizer);
        DashboardPresentation.RenderTiming(HomeHttpsValue, HomeHttpsSamples, summary?.Https, "Https", _localizer);
        HomeGatewayLabel.Text = _localizer.T("QualityGatewayTiming");
        HomeDnsLabel.Text = _localizer.T("QualityDnsTiming");
        HomeHttpsLabel.Text = _localizer.T("QualityHttpsTiming");
        HealthStateIcon.Text = report?.State switch
        {
            NetworkState.Healthy => "✓", NetworkState.Recovering => "↻",
            NetworkState.GatewayIssue or NetworkState.DnsIssue or NetworkState.InternetIssue => "!",
            null => "—", _ => "×"
        };
        AppThemePolicy.BindBrush(HealthStateIcon, TextBlock.ForegroundProperty, report?.State switch
        {
            NetworkState.Healthy => "StatusHealthyBrush", NetworkState.Recovering or NetworkState.GatewayIssue
                or NetworkState.DnsIssue or NetworkState.InternetIssue => "StatusWarningBrush",
            null => "TextFillColorSecondaryBrush", _ => "StatusDangerBrush"
        });
    }
}
