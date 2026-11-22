using System;
using System.Windows.Controls;
using System.Windows.Threading;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;

namespace NetGuardian.App;

public partial class MainWindow
{
    private DateTimeOffset _nextMetricRefresh;

    private void DashboardNetworkChanged(object? sender, EventArgs e)
    {
        var changedAt = DateTimeOffset.Now;
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_resourcesCleaned) return;
            _reportSelection.NetworkChanged(changedAt);
            CurrentSsidText.Text = _reportSelection.KnownSsid ?? "—";
            RenderDashboardMetrics(_reportSelection.Report);
        }), DispatcherPriority.Send);
    }

    private void RefreshMetricAge()
    {
        if (StatusPanel.IsVisible && DateTimeOffset.Now >= _nextMetricRefresh)
            RenderDashboardMetrics(_reportSelection.Report);
    }

    private void RenderDashboardMetrics(ConnectivityReport? report)
    {
        var now = DateTimeOffset.Now;
        _nextMetricRefresh = now.AddMinutes(1);
        Render(HomeGatewayValue, HomeGatewaySamples, "Gateway");
        Render(HomeDnsValue, HomeDnsSamples, "Dns");
        Render(HomeHttpsValue, HomeHttpsSamples, "Https");
        void Render(TextBlock value, TextBlock text, string kind)
        {
            var sample = _reportSelection.Measurement(kind);
            DashboardPresentation.RenderMeasurement(value, text, sample, kind, _localizer,
                now, _settings.Policy.CheckIntervalSeconds, sample?.Report.CheckedAt != report?.CheckedAt);
        }
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
