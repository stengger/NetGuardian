using System;
using System.Threading;
using System.Threading.Tasks;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Runtime;

namespace NetGuardian.App;

public partial class MainWindow
{
    private bool _isNetworkActionRunning;
    private string? _networkActionStatusKey;
    private CancellationTokenSource? _networkActionCancellation;

    private Task<bool> RunExclusiveNetworkActionAsync(Func<Task<bool>> action)
        => RunExclusiveNetworkActionAsync(_ => action());

    private async Task<bool> RunExclusiveNetworkActionAsync(Func<CancellationToken, Task<bool>> action)
    {
        if (_resourcesCleaned || _isNetworkActionRunning) return false;
        using var cancellation = new CancellationTokenSource();
        _networkActionCancellation = cancellation;
        var previousDnsStatus = DnsStatusText.Text;
        _isNetworkActionRunning = true;
        _networkActionStatusKey = "ManualNetworkWaiting";
        RefreshNetworkActionUi();
        var resume = _engine.IsMonitoring;
        try
        {
            if (resume || _monitorLease is not null)
            {
                _engine.Stop();
                await ReleaseMonitorLeaseWhenIdleAsync();
            }
            using var access = await ManualNetworkAccess.AcquireAsync(GuardianPaths.MachineDirectory, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _networkActionStatusKey = "ManualNetworkRunning";
            RefreshNetworkActionUi();
            return await action(cancellation.Token);
        }
        catch (ManualControlException ex)
        {
            var key = ex.Reason switch
            {
                ManualControlFailure.Busy => "ManualNetworkBusy",
                ManualControlFailure.UpgradeRequired => "ManualNetworkUpgrade",
                ManualControlFailure.TimedOut => "ManualNetworkTimeout",
                _ => "ManualNetworkUnavailable"
            };
            Log(_localizer.T(key), UiLogLevel.Warning);
            ShowFeedback(_localizer.T("ActionFailedTitle"), _localizer.T(key), FeedbackSeverity.Warning);
            return false;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return false; }
        finally
        {
            _networkActionCancellation = null;
            _isNetworkActionRunning = false;
            _networkActionStatusKey = null;
            if (!_resourcesCleaned)
            {
                if (DnsStatusText.Text == _localizer.T("ManualNetworkWaiting") || DnsStatusText.Text == _localizer.T("ManualNetworkRunning"))
                    DnsStatusText.Text = previousDnsStatus;
                RefreshNetworkActionUi();
                if (resume && !_isExiting) await StartMonitoringAsync(checkImmediately: false);
            }
        }
    }

    private void RefreshNetworkActionUi()
    {
        if (_resourcesCleaned) return;
        UpdateWifiSelectionUi();
        UpdateMonitorStatus();
        UpdateDnsApplyAvailability();
        DnsFlushButton.IsEnabled = !_isNetworkActionRunning && !_isApplyingDns;
        AdapterRenewDhcpButton.IsEnabled = !_isNetworkActionRunning && AdapterTargetComboBox.Items.Count > 0;
        AddManualButton.IsEnabled = !_isNetworkActionRunning;
        if (_isNetworkActionRunning && _networkActionStatusKey is not null)
        {
            var message = _localizer.T(_networkActionStatusKey);
            CurrentWifiSelectionText.Text = message;
            DnsStatusText.Text = message;
            SetToolOutput(message);
        }
    }
}
