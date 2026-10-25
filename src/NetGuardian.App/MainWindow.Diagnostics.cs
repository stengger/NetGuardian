using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Network;

namespace NetGuardian.App;

public partial class MainWindow
{
    private readonly DiagnosticSession<WebsiteTestResult> _websiteSession = new();
    private readonly DiagnosticSession<PublicIpInfo> _ipSession = new();
    private readonly DiagnosticSession<ConnectivityReport> _diagnosticSession = new();
    private readonly List<(WebsiteTestResult Result, DateTimeOffset At)> _websiteHistory = new();

    private async Task RunWebsiteTestAsync(string name, string url)
    {
        if (_resourcesCleaned || _websiteSession.IsBusy) return;
        if (string.IsNullOrWhiteSpace(url)) { WebsiteStatusText.Text = _localizer.T("EnterWebsiteUrl"); return; }
        await _websiteSession.RunAsync(token => _websiteTester.TestAsync(name, url, token), RefreshWebsiteUi);
        if (_resourcesCleaned) return;
        if (_websiteSession.Result is { } result)
        {
            _websiteHistory.Insert(0, (result, DateTimeOffset.Now));
            while (_websiteHistory.Count > 20) _websiteHistory.RemoveAt(_websiteHistory.Count - 1);
            RefreshWebsiteUi();
        }
        DiagnosticLog(WebsiteStatusText.Text, _websiteSession.Phase == DiagnosticPhase.Failed ? UiLogLevel.Error
            : _websiteSession.Result is { Success: false } ? UiLogLevel.Warning : UiLogLevel.Information);
        UpdateEmptyStates();
    }

    private async Task RefreshPublicIpInfoAsync()
    {
        if (_resourcesCleaned || _ipSession.IsBusy) return;
        await _ipSession.RunAsync(token => _publicIpInfo.GetAsync(token, forceRefresh: true), RefreshIpToolUi);
        if (!_resourcesCleaned) DiagnosticLog(IpInfoStatusText.Text, _ipSession.Phase == DiagnosticPhase.Failed ? UiLogLevel.Error : UiLogLevel.Information);
    }

    private async Task RunDiagnosticReportAsync()
    {
        if (_resourcesCleaned || _diagnosticSession.IsBusy) return;
        await _diagnosticSession.RunAsync(async token =>
        {
            var currentSsid = await _wifi.GetCurrentSsidAsync(token);
            var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(token);
            return await _connectivityTester.RunFullCheckAsync(_settings.Policy, currentSsid, diagnostics, token);
        }, RefreshDiagnosticReportUi);
        if (!_resourcesCleaned) DiagnosticLog(ReportStatusText.Text, _diagnosticSession.Phase == DiagnosticPhase.Failed ? UiLogLevel.Error
            : _diagnosticSession.Result is { Ok: false } ? UiLogLevel.Warning : UiLogLevel.Information);
    }

    private string DiagnosticStatus<T>(DiagnosticSession<T> session) where T : class => session.Phase switch
    {
        DiagnosticPhase.Running => _localizer.T("Checking"),
        DiagnosticPhase.Cancelling => _localizer.T("DiagnosticCancelling"),
        DiagnosticPhase.Cancelled => _localizer.T("DiagnosticCancelled"),
        DiagnosticPhase.Failed => $"{_localizer.T("DiagnosticFailed")} {session.Error}",
        _ => _localizer.T("NotChecked")
    };

    private void RefreshWebsiteUi()
    {
        if (_resourcesCleaned) return;
        foreach (var button in new[] { WebsiteRunButton, WebsiteAppleButton, WebsiteGitHubButton, WebsiteGoogleButton, WebsiteYouTubeButton })
            button.IsEnabled = !_websiteSession.IsBusy;
        WebsiteUrlTextBox.IsEnabled = !_websiteSession.IsBusy;
        WebsiteTimeColumn.Header = _localizer.T("LogTime");
        WebsiteHistoryHintText.Text = _localizer.T("WebsiteHistoryHint");
        _websiteRows.Clear();
        foreach (var (entry, at) in _websiteHistory)
            _websiteRows.Add(new WebsiteTestRow(string.IsNullOrWhiteSpace(entry.Name) ? entry.Host : entry.Name,
                _localizer.T(entry.Success ? "Pass" : "Fail"), entry.StatusCode?.ToString() ?? "-",
                FormatLatency(entry.DnsLatencyMs), FormatLatency(entry.HttpLatencyMs),
                string.IsNullOrWhiteSpace(entry.Address) ? "-" : entry.Address,
                string.IsNullOrWhiteSpace(entry.Detail) ? entry.Url : entry.Detail, at.ToString("yyyy-MM-dd HH:mm:ss")));
        WebsiteEmptyText.Text = _localizer.T("NoWebsiteRows");
        WebsiteGrid.ToolTip = new TextBlock { Text = _localizer.T("WebsiteHistoryHint"), TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        SetDiagnosticActivity(WebsiteCancelButton, WebsiteActivity, _websiteSession.IsBusy, _websiteSession.Phase == DiagnosticPhase.Running);
        WebsiteStatusText.Text = _websiteSession.Result is { } result
            ? string.Format(_localizer.T("WebsiteTestDone"), result.Name, _localizer.T(result.Success ? "Pass" : "Fail"), FormatLatency(result.HttpLatencyMs))
            : DiagnosticStatus(_websiteSession);
    }

    private void RefreshIpToolUi()
    {
        if (_resourcesCleaned) return;
        IpInfoRefreshButton.IsEnabled = !_ipSession.IsBusy;
        SetDiagnosticActivity(IpInfoCancelButton, IpInfoActivity, _ipSession.IsBusy, _ipSession.Phase == DiagnosticPhase.Running);
        _ipInfoRows.Clear();
        IpInfoStatusText.Text = DiagnosticStatus(_ipSession);
        if (_ipSession.Result is { } info)
        {
            AddIpInfoRow("PublicIp", info.Ip);
            AddIpInfoRow("CountryRegion", $"{info.Country} ({info.CountryCode})");
            AddIpInfoRow("City", string.Join(", ", new[] { info.Region, info.City }.Where(item => !string.IsNullOrWhiteSpace(item) && item != "-")));
            AddIpInfoRow("Isp", info.Isp);
            AddIpInfoRow("Organization", info.Organization);
            AddIpInfoRow("AutonomousSystem", info.AutonomousSystem);
            AddIpInfoRow("Timezone", info.Timezone);
            AddIpInfoRow("IpCoordinates", PublicIpPresentation.Coordinates(info.Latitude, info.Longitude, _localizer.T("UnknownData")));
            AddIpInfoRow("LastRefresh", info.RefreshedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            IpInfoStatusText.Text = string.Format(_localizer.T("IpInfoQueryResult"), info.Ip, info.RefreshedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }
        UpdateEmptyStates();
    }

    private void RefreshDiagnosticReportUi()
    {
        if (_resourcesCleaned) return;
        ReportRunButton.IsEnabled = !_diagnosticSession.IsBusy;
        SetDiagnosticActivity(ReportCancelButton, ReportActivity, _diagnosticSession.IsBusy, _diagnosticSession.Phase == DiagnosticPhase.Running);
        _reportRows.Clear();
        ReportStatusText.Text = DiagnosticStatus(_diagnosticSession);
        if (_diagnosticSession.Result is { } report)
        {
            var summary = ConnectivityQualitySummary.From(report);
            AddReportRow("HealthState", $"{StateText(report.State)} · {string.Format(_localizer.T("QualityChecksPassed"), summary.PassedChecks, summary.TotalChecks)}", AdviceForState(report.State));
            AddReportRow("CurrentNetwork", string.IsNullOrWhiteSpace(report.CurrentSsid) ? "-" : report.CurrentSsid, _localizer.T("CheckWifiPriority"));
            AddReportRow("IPv4", string.IsNullOrWhiteSpace(report.Ipv4) ? "-" : report.Ipv4, string.IsNullOrWhiteSpace(report.Ipv4) ? _localizer.T("CheckAdapter") : _localizer.T("NoActionNeeded"));
            AddReportRow("Gateway", string.IsNullOrWhiteSpace(report.Gateway) ? "-" : report.Gateway, report.State == NetworkState.GatewayIssue ? _localizer.T("CheckRouter") : _localizer.T("NoActionNeeded"));
            AddReportRow("DnsTools", ProbeSummary(report, "DNS"), report.State == NetworkState.DnsIssue ? _localizer.T("TryDnsSettings") : _localizer.T("NoActionNeeded"));
            AddReportRow("Internet", ProbeSummary(report, "HTTPS"), report.State == NetworkState.InternetIssue ? _localizer.T("CheckProxyOrIsp") : _localizer.T("NoActionNeeded"));
            ReportStatusText.Text = $"{report.CheckedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {StateText(report.State)} - {LocalizeSummary(report.Summary)}";
        }
        UpdateEmptyStates();
    }

    private void SetDiagnosticActivity(Button cancel, ProgressBar activity, bool busy, bool canCancel)
    {
        cancel.Content = _localizer.T("Cancel");
        cancel.Visibility = activity.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        cancel.IsEnabled = canCancel;
    }

    private void WebsiteCancel_Click(object sender, RoutedEventArgs e) { _websiteSession.Cancel(); RefreshWebsiteUi(); }
    private void IpInfoCancel_Click(object sender, RoutedEventArgs e) { _ipSession.Cancel(); RefreshIpToolUi(); }
    private void ReportCancel_Click(object sender, RoutedEventArgs e) { _diagnosticSession.Cancel(); RefreshDiagnosticReportUi(); }
}
