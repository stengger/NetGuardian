using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Connectivity;

namespace NetGuardian.App;

public partial class MainWindow
{
    private readonly QualityCheckSession _qualitySession = new();
    private CancellationTokenSource? _qualityCancellation;
    private string? _qualityAnnouncement;

    private async Task RunQualityCheckAsync()
    {
        if (_qualityCancellation is not null || _resourcesCleaned) return;
        using var cancellation = new CancellationTokenSource();
        _qualityCancellation = cancellation;
        var runId = _qualitySession.Begin();
        RefreshQualityUi();
        try
        {
            var ssid = await _wifi.GetCurrentSsidAsync(cancellation.Token);
            var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(cancellation.Token);
            var report = await _connectivityTester.RunFullCheckAsync(_settings.Policy, ssid, diagnostics, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _qualitySession.Complete(runId, report);
            Log($"{_localizer.T("QualityCheckTitle")}: {StateText(report.State)} - {report.Summary}", report.Ok ? UiLogLevel.Information : UiLogLevel.Warning, UiLogSource.Diagnostic);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _qualitySession.Cancel(runId);
        }
        catch (Exception ex)
        {
            _qualitySession.Fail(runId, ex.Message);
            Log($"{_localizer.T("QualityCheckFailed")}: {ex.Message}", UiLogLevel.Error, UiLogSource.Diagnostic);
        }
        finally
        {
            _qualityCancellation = null;
            if (!_resourcesCleaned) RefreshQualityUi();
        }
    }

    private void QualityCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_qualityCancellation is null) return;
        _qualitySession.RequestCancel();
        _qualityCancellation.Cancel();
        RefreshQualityUi();
    }

    private void ResetQualityUi() => RefreshQualityUi();

    private void RefreshQualityUi()
    {
        string T(string key) => _localizer.T(key);
        var phase = _qualitySession.Phase;
        var report = _qualitySession.Report;
        QualityRunButton.IsEnabled = !_qualitySession.IsBusy;
        QualityRunButton.Content = T(phase == QualityCheckPhase.Idle ? "RunQualityCheck" : "QualityCheckAgain");
        QualityCancelButton.Visibility = _qualitySession.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        QualityCancelButton.IsEnabled = phase == QualityCheckPhase.Running;
        QualityActivityBar.Visibility = _qualitySession.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        _qualityRows.Clear();
        var stateKey = phase switch
        {
            QualityCheckPhase.Running => "Checking", QualityCheckPhase.Cancelling => "QualityCheckCancelling",
            QualityCheckPhase.Cancelled => "QualityCheckCancelled", QualityCheckPhase.Failed => "QualityCheckFailed", _ => "NotChecked"
        };
        QualityStateText.Text = report is null ? T(stateKey) : StateText(report.State);
        QualityStateText.Foreground = ThemeBrush(report is null ? "TextFillColorPrimaryBrush" : report.Ok ? "StatusHealthyBrush" : "StatusWarningBrush");
        QualityPassedText.Text = T("QualityNoCurrentResult");
        QualityStatusText.Text = phase switch
        {
            QualityCheckPhase.Running => T("QualityCheckRunning"),
            QualityCheckPhase.Cancelling => T("QualityCheckCancelling"),
            QualityCheckPhase.Cancelled => T("QualityCheckCancelledHint"),
            QualityCheckPhase.Failed => $"{T("QualityCheckFailedHint")} {_qualitySession.Error}",
            _ => T("NotChecked")
        };
        SetQualityTiming(QualityGatewayValue, QualityGatewaySamples, null);
        SetQualityTiming(QualityDnsValue, QualityDnsSamples, null);
        SetQualityTiming(QualityHttpsValue, QualityHttpsSamples, null);
        if (report is not null)
        {
            var summary = ConnectivityQualitySummary.From(report);
            QualityPassedText.Text = string.Format(T("QualityCheckCounts"), summary.PassedChecks, summary.TotalChecks,
                string.IsNullOrWhiteSpace(report.CurrentSsid) ? T("QualityNoSsid") : report.CurrentSsid,
                report.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            SetQualityTiming(QualityGatewayValue, QualityGatewaySamples, summary.Gateway);
            SetQualityTiming(QualityDnsValue, QualityDnsSamples, summary.Dns);
            SetQualityTiming(QualityHttpsValue, QualityHttpsSamples, summary.Https);
            QualityStatusText.Text = LocalizeSummary(report.Summary);
            foreach (var probe in report.Probes)
                _qualityRows.Add(new(LocalizeProbeName(probe.Name), T(probe.Ok ? "Pass" : "Fail"),
                    probe.LatencyMs is { } time && double.IsFinite(time) && time >= 0 ? $"{time:F0} ms" : "—", LocalizeProbeDetail(probe.Detail)));
        }
        QualityEmptyText.Text = _qualitySession.IsBusy ? T("QualityCheckRunning") : T("NoQualityRows");
        UpdateEmptyStates();
        var announcement = QualityStateText.Text + QualityStatusText.Text;
        if (_qualityAnnouncement != announcement)
        {
            _qualityAnnouncement = announcement;
            var peer = UIElementAutomationPeer.FromElement(QualityStateText) ?? UIElementAutomationPeer.CreatePeerForElement(QualityStateText);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void SetQualityTiming(TextBlock value, TextBlock samples, ProbeTimingSummary? timing)
    {
        value.Text = timing?.AverageMs is { } average ? $"{average:F0} ms" : "—";
        samples.Text = timing is null || timing.Attempted == 0 ? _localizer.T("QualityNotMeasured")
            : string.Format(_localizer.T("QualityTimingSamples"), timing.Succeeded, timing.Attempted, timing.Measured);
    }
}
