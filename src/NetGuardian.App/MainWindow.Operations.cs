using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.Network;
using NetGuardian.Core.Notifications;

namespace NetGuardian.App;

public partial class MainWindow
{
    private QualityBaselineStore? _baselineStore;
    private AlertNotificationService? _notificationService;
    private bool _offlineAlertSentInEpisode;
    private int _notifyFlushInFlight;
    private int _proxyBannerRefreshId;
    private int _proxyBannerRunning;
    private DateTimeOffset _nextProxyBannerRefresh;

    private void InitializeOperationsServices(string dataDirectory)
    {
        var logsDir = Path.Combine(dataDirectory, "logs");
        _baselineStore = new QualityBaselineStore(logsDir);
        _notificationService = new AlertNotificationService(dataDirectory, _fileLog);
    }

    private void RecordCheckObservability(ConnectivityReport report)
    {
        var signal = _currentWifiInfo is { ReadSucceeded: true, Connected: true }
            ? _currentWifiInfo.SignalPercent
            : null;
        _baselineStore?.TryRecord(report, signal);

        if (_notificationService is null || !_engine.IsMonitoring)
        {
            return;
        }

        var notifyCfg = _settings.Notifications;
        if (report.Ok)
        {
            _offlineAlertSentInEpisode = false;
            if (notifyCfg.IsConfigured()
                && _notificationService.GetPendingSnapshot().Count > 0
                && Interlocked.CompareExchange(ref _notifyFlushInFlight, 1, 0) == 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var flushed = await _notificationService.FlushPendingAsync(notifyCfg);
                        if (flushed > 0 && !_resourcesCleaned)
                        {
                            _ = Dispatcher.BeginInvoke(UpdateOperationsSupportUi);
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _notifyFlushInFlight, 0);
                    }
                });
            }
            return;
        }

        if (!_offlineAlertSentInEpisode
            && _engine.FailureStreak >= _settings.Policy.ConsecutiveFailuresBeforeRecovery
            && notifyCfg.ShouldNotify(AlertEventKind.OfflineDetected))
        {
            _offlineAlertSentInEpisode = true;
            var msg = new AlertNotificationMessage(
                Guid.NewGuid().ToString("N"),
                AlertEventKind.OfflineDetected,
                "NetGuardian 断网告警",
                $"连续 {_engine.FailureStreak} 次探测异常（{report.State}）：{report.Summary}",
                report.CurrentSsid,
                DateTimeOffset.UtcNow);
            _ = DispatchNotificationAsync(notifyCfg, msg);
        }
    }

    private async Task DispatchNotificationAsync(NotificationSettings cfg, AlertNotificationMessage msg)
    {
        if (_notificationService is null) return;
        await _notificationService.NotifyAsync(cfg, msg);
        if (!_resourcesCleaned)
        {
            _ = Dispatcher.BeginInvoke(UpdateOperationsSupportUi);
        }
    }

    private void HandleOperationsEngineEvent(EngineEventArgs e)
    {
        var args = e.Args;
        switch (e.Kind)
        {
            case EngineEventKind.RecoverySuppressedByMaintenanceOrPause:
                EngineLog(string.Format(_localizer.T("RecoverySuppressedLog"), args[0]));
                break;
            case EngineEventKind.RecoverySuppressedByHealthyEthernet:
                EngineLog(string.Format(_localizer.T("RecoverySuppressedEthernetLog"), args[0]));
                break;
            case EngineEventKind.PrimaryFailbackStarted:
                Log(string.Format(_localizer.T("PrimaryFailbackStartedLog"), args[0]), source: UiLogSource.Recovery);
                break;
            case EngineEventKind.PrimaryFailbackSucceeded:
            {
                var ssid = args[0]?.ToString() ?? "";
                Log(string.Format(_localizer.T("PrimaryFailbackSucceededLog"), ssid), source: UiLogSource.Recovery);
                if (_settings.Notifications.ShouldNotify(AlertEventKind.PrimaryFailbackSucceeded))
                {
                    _ = DispatchNotificationAsync(_settings.Notifications, new AlertNotificationMessage(
                        Guid.NewGuid().ToString("N"),
                        AlertEventKind.PrimaryFailbackSucceeded,
                        "NetGuardian 已回切主 Wi-Fi",
                        $"已自动切回主网络 {ssid} 并验证公网可达。",
                        ssid,
                        DateTimeOffset.UtcNow));
                }
                break;
            }
            case EngineEventKind.PrimaryFailbackRolledBack:
                Log(string.Format(_localizer.T("PrimaryFailbackRolledBackLog"), args[0], args[1]), UiLogLevel.Warning, UiLogSource.Recovery);
                break;
            case EngineEventKind.RecoveryWifiConnected:
            {
                _offlineAlertSentInEpisode = false;
                var ssid = args[0]?.ToString();
                if (_engine.IsMonitoring && _settings.Notifications.ShouldNotify(AlertEventKind.RecoverySucceeded))
                {
                    _ = DispatchNotificationAsync(_settings.Notifications, new AlertNotificationMessage(
                        Guid.NewGuid().ToString("N"),
                        AlertEventKind.RecoverySucceeded,
                        "NetGuardian 网络已恢复",
                        $"已自动恢复连接并验证公网可达（{ssid ?? "-"}）。",
                        ssid,
                        DateTimeOffset.UtcNow));
                }
                break;
            }
            case EngineEventKind.RecoveryFailed:
            {
                if (_engine.IsMonitoring && _settings.Notifications.ShouldNotify(AlertEventKind.RecoveryFailed))
                {
                    var latest = _reportSelection.Report;
                    _ = DispatchNotificationAsync(_settings.Notifications, new AlertNotificationMessage(
                        Guid.NewGuid().ToString("N"),
                        AlertEventKind.RecoveryFailed,
                        "NetGuardian 自动恢复失败",
                        $"本轮恢复结束但公网尚未恢复（状态：{latest?.State}）。",
                        latest?.CurrentSsid,
                        DateTimeOffset.UtcNow));
                }
                break;
            }
            case EngineEventKind.HighRiskActionCompleted:
            {
                if (_engine.IsMonitoring && _settings.Notifications.ShouldNotify(AlertEventKind.HighRiskActionTriggered))
                {
                    var action = args[0]?.ToString() ?? "";
                    var commandOk = args[1] is true;
                    var networkOk = args[2] is true;
                    _ = DispatchNotificationAsync(_settings.Notifications, new AlertNotificationMessage(
                        Guid.NewGuid().ToString("N"),
                        AlertEventKind.HighRiskActionTriggered,
                        "NetGuardian 已执行高风险设备恢复",
                        $"动作：{action}，命令执行：{commandOk}，公网恢复：{networkOk}。",
                        _reportSelection.Report?.CurrentSsid,
                        DateTimeOffset.UtcNow));
                }
                break;
            }
        }
    }

    private void RefreshObservabilitySummary()
    {
        if (_incidentView is null || _resourcesCleaned) return;
        var baseline = _baselineStore;
        var history = IncidentHistory;
        _ = Task.Run(() =>
        {
            var sla7 = NetworkObservabilityAnalyzer.ComputeSla(history, 7);
            var sla30 = NetworkObservabilityAnalyzer.ComputeSla(history, 30);
            var samples = baseline?.ReadWindow(7) ?? [];
            var trend = NetworkObservabilityAnalyzer.SummarizeQualityTrend(samples);
            return (sla7, sla30, trend);
        }).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && !_resourcesCleaned)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_resourcesCleaned)
                    {
                        _incidentView?.ShowObservability(t.Result.sla7, t.Result.sla30, t.Result.trend);
                    }
                });
            }
        });
    }

    private void RefreshProxyAndVpnIfDue()
    {
        if (!_resourcesCleaned
            && Volatile.Read(ref _proxyBannerRunning) == 0
            && (AdapterConfigTab.IsVisible || IpInfoToolsTab.IsVisible)
            && DateTimeOffset.UtcNow >= _nextProxyBannerRefresh)
        {
            RefreshProxyAndVpnBanners();
        }
    }

    private void RefreshProxyAndVpnBanners()
    {
        if (_resourcesCleaned) return;
        var seq = Interlocked.Increment(ref _proxyBannerRefreshId);
        Interlocked.Exchange(ref _proxyBannerRunning, 1);
        _nextProxyBannerRefresh = DateTimeOffset.UtcNow.AddSeconds(5);
        _ = Task.Run(() =>
        {
            var snap = ProxyAndVpnInspector.Inspect();
            var defaultIdx = NetworkEnvironmentInspector.GetDefaultIpv4RouteInterfaceIndex();
            var defaultAdapter = NetworkEnvironmentInspector.GetDefaultRouteAdapterName(defaultIdx);
            return (snap, defaultAdapter);
        }).ContinueWith(t =>
        {
            Interlocked.Exchange(ref _proxyBannerRunning, 0);
            _nextProxyBannerRefresh = DateTimeOffset.UtcNow.AddSeconds(5);
            if (t.IsCompletedSuccessfully && !_resourcesCleaned && seq == Volatile.Read(ref _proxyBannerRefreshId))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_resourcesCleaned)
                    {
                        ApplyProxyAndVpnSnapshot(t.Result.snap, t.Result.defaultAdapter);
                    }
                });
            }
        });
    }

    internal void ApplyProxyAndVpnSnapshot(ProxyAndVpnSnapshot snapshot, string? defaultRouteAdapter, bool refreshIpTable = true)
    {
        _latestProxySnapshot = snapshot;
        _latestDefaultRouteAdapter = defaultRouteAdapter;
        var summary = FormatProxyAndVpnBannerText(snapshot, defaultRouteAdapter);
        if (AdapterProxyVpnText is not null) AdapterProxyVpnText.Text = summary;
        if (AdapterConfigTab.IsVisible)
        {
            RenderSelectedAdapterDetail(AdapterTargetComboBox.SelectedItem as NetworkAdapterCandidate);
        }
        if (refreshIpTable && _ipSession.Result is not null)
        {
            RefreshIpToolUi();
        }
    }

    internal string FormatProxyAndVpnBannerText(ProxyAndVpnSnapshot snapshot, string? defaultRouteAdapter)
        => ProxyVpnPresentation.FormatBanner(snapshot, defaultRouteAdapter, _localizer);

    private void RenderSelectedAdapterDetail(NetworkAdapterCandidate? target)
    {
        if (AdapterDetailText is null) return;
        if (target is null)
        {
            AdapterDetailText.Text = _localizer.T("AdapterDetailEmpty");
            return;
        }

        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(i => i.Id == target.Id);
        if (nic is null)
        {
            AdapterDetailText.Text = _localizer.T("AdapterDetailEmpty");
            return;
        }

        var detail = NetworkEnvironmentInspector.ReadAdapterDetail(nic);
        AdapterDetailText.Text = ProxyVpnPresentation.FormatAdapterDetail(detail, _localizer);
    }

    private void ApplyOperationsPolicyToUi()
    {
        EnableMaintenanceWindowCheckBox.IsChecked = _settings.Policy.EnableMaintenanceWindow;
        MaintenanceStartTextBox.Text = _settings.Policy.MaintenanceStartTime;
        MaintenanceEndTextBox.Text = _settings.Policy.MaintenanceEndTime;
        EnablePrimaryFailbackCheckBox.IsChecked = _settings.Policy.EnablePrimaryFailback;
        PrimaryFailbackIntervalBox.Value = _settings.Policy.PrimaryFailbackIntervalMinutes;
        PreferEthernetCheckBox.IsChecked = _settings.Policy.PreferEthernetWhenHealthy;
        UpdatePauseRecoveryBannerUi();
    }

    private void ReadOperationsPolicyFromUi(MonitorPolicy policy)
    {
        policy.EnableMaintenanceWindow = EnableMaintenanceWindowCheckBox.IsChecked == true;
        policy.MaintenanceStartTime = MaintenanceStartTextBox.Text?.Trim() ?? policy.MaintenanceStartTime;
        policy.MaintenanceEndTime = MaintenanceEndTextBox.Text?.Trim() ?? policy.MaintenanceEndTime;
        policy.EnablePrimaryFailback = EnablePrimaryFailbackCheckBox.IsChecked == true;
        if (PrimaryFailbackIntervalBox.Value is double interval && double.IsFinite(interval))
        {
            policy.PrimaryFailbackIntervalMinutes = (int)Math.Round(interval);
        }
        policy.PreferEthernetWhenHealthy = PreferEthernetCheckBox.IsChecked == true;
    }

    private void UpdatePauseRecoveryBannerUi()
    {
        var pausedUntil = _settings.Policy.PauseRecoveryUntilUtc;
        var active = pausedUntil is { } until && until > DateTimeOffset.UtcNow;
        PauseRecoveryStatusText.Text = active
            ? string.Format(_localizer.T("PauseRecoveryStatusActive"), pausedUntil!.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
            : _localizer.T("PauseRecoveryStatusNone");
        PauseRecoveryButton.Content = _localizer.T(active ? "ResumeRecoveryNowButton" : "PauseRecoveryOneHourButton");
    }

    private void PauseRecovery_Click(object sender, RoutedEventArgs e)
    {
        var currentlyPaused = _settings.Policy.PauseRecoveryUntilUtc is { } u && u > DateTimeOffset.UtcNow;
        _settings.Policy.PauseRecoveryUntilUtc = currentlyPaused
            ? null
            : DateTimeOffset.UtcNow.AddHours(1);
        SaveSettings();
        UpdatePauseRecoveryBannerUi();
        ShowFeedback(_localizer.T("PolicyOpsTitle"), PauseRecoveryStatusText.Text, FeedbackSeverity.Info);
    }

    private void UpdateOperationsSupportUi()
    {
        OperationsSupportTitle.Text = _localizer.T("OperationsSupportTitle");
        ConfigureAlertsButton.Content = _localizer.T("ConfigureAlertsButton");
        ExportDiagnosticBundleButton.Content = _localizer.T("ExportDiagnosticBundleButton");

        var cfg = _settings.Notifications;
        var pending = _notificationService?.GetPendingSnapshot().Count ?? 0;
        if (cfg.Enabled)
        {
            var channelLabel = cfg.Channel switch
            {
                NotificationChannelType.WeCom => _localizer.T("ChannelWeCom"),
                NotificationChannelType.Feishu => _localizer.T("ChannelFeishu"),
                NotificationChannelType.DingTalk => _localizer.T("ChannelDingTalk"),
                _ => _localizer.T("ChannelGenericWebhook")
            };
            var masked = NotificationSettings.RedactWebhookUrl(cfg.WebhookUrl);
            RemoteAlertsSummaryText.Text = string.Format(_localizer.T("RemoteAlertsSummary"), channelLabel, string.IsNullOrWhiteSpace(masked) ? "-" : masked, pending);
        }
        else
        {
            RemoteAlertsSummaryText.Text = _localizer.T("RemoteAlertsDisabled");
        }
    }

    private void ApplyOperationsLanguage()
    {
        ProxyVpnBannerTitleAdapter.Text = _localizer.T("ProxyVpnBannerTitle");
        AdapterDetailTitleText.Text = _localizer.T("AdapterDetailTitle");
        PolicyOpsTitle.Text = _localizer.T("PolicyOpsTitle");
        EnableMaintenanceWindowCheckBox.Content = _localizer.T("EnableMaintenanceWindowLabel");
        MaintenanceStartLabel.Text = _localizer.T("MaintenanceStartLabel");
        MaintenanceEndLabel.Text = _localizer.T("MaintenanceEndLabel");
        EnablePrimaryFailbackCheckBox.Content = _localizer.T("EnablePrimaryFailbackLabel");
        PrimaryFailbackIntervalLabel.Text = _localizer.T("PrimaryFailbackIntervalLabel");
        PreferEthernetCheckBox.Content = _localizer.T("PreferEthernetWhenHealthyLabel");
        PolicyOpsTipText.Text = _localizer.T("PolicyOpsTip");
        UpdatePauseRecoveryBannerUi();
        UpdateOperationsSupportUi();
        RenderSelectedAdapterDetail(AdapterTargetComboBox.SelectedItem as NetworkAdapterCandidate);
        RefreshProxyAndVpnBanners();
    }

    private void ConfigureAlerts_Click(object sender, RoutedEventArgs e)
    {
        var svc = _notificationService ?? new AlertNotificationService(Path.GetDirectoryName(_store.Path), _fileLog);
        var dialog = new NotificationSettingsDialog(_settings.Notifications, _localizer, svc, _currentWifiInfo?.Ssid)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            _settings.Notifications = dialog.ResultSettings;
            SaveSettings();
            UpdateOperationsSupportUi();
        }
    }

    private void ExportDiagnosticBundle_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Zip Archive (*.zip)|*.zip",
            FileName = $"NetGuardian-Diagnostic-{DateTimeOffset.Now:yyyyMMdd-HHmm}.zip",
            DefaultExt = ".zip"
        };
        if (dlg.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dlg.FileName))
        {
            return;
        }

        try
        {
            var dataDir = Path.GetDirectoryName(_store.Path);
            var result = DiagnosticBundleExporter.Export(dlg.FileName, _settings, dataDir);
            var msg = string.Format(_localizer.T("DiagnosticBundleExportSuccess"), result.OutputZipPath, result.FileCount);
            OperationsBundleStatusText.Text = msg;
            ShowFeedback(_localizer.T("ExportDiagnosticBundleButton"), msg, FeedbackSeverity.Success);
        }
        catch (Exception ex)
        {
            var msg = string.Format(_localizer.T("DiagnosticBundleExportFailed"), ex.Message);
            OperationsBundleStatusText.Text = msg;
            ShowFeedback(_localizer.T("ExportDiagnosticBundleButton"), msg, FeedbackSeverity.Danger);
        }
    }
}
