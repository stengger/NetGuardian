using System;
using System.Threading;
using System.Threading.Tasks;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Wifi;

namespace NetGuardian.App;

public partial class MainWindow
{
    private readonly CancellationTokenSource _currentWifiLifetime = new();
    private WifiConnectionInfo? _currentWifiInfo;
    private bool _currentWifiRefreshRunning;
    private bool _currentWifiRefreshRequested;
    private DateTimeOffset _nextCurrentWifiRefresh;

    private void RefreshCurrentWifiIfDue()
    {
        if (!_resourcesCleaned && !_currentWifiRefreshRunning && WifiPanel.IsVisible && DateTimeOffset.UtcNow >= _nextCurrentWifiRefresh)
            _ = RefreshCurrentWifiAsync();
    }

    private async Task RefreshCurrentWifiAsync()
    {
        if (_resourcesCleaned) return;
        _currentWifiRefreshRequested = true;
        if (_currentWifiRefreshRunning) return;
        _currentWifiRefreshRunning = true;
        try
        {
            while (_currentWifiRefreshRequested && !_resourcesCleaned)
            {
                _currentWifiRefreshRequested = false;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_currentWifiLifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                WifiConnectionInfo info;
                try { info = await _wifi.GetConnectionInfoAsync(deadline.Token); }
                catch (OperationCanceledException) when (_currentWifiLifetime.IsCancellationRequested) { return; }
                catch (OperationCanceledException) { info = new(false, false, Error: _localizer.T("WifiCurrentReadTimeout")); }
                catch (Exception ex) { info = new(false, false, Error: ex.Message); }
                // 查询期间又发生连接或刷新时，丢弃旧快照并重新读取。
                if (_resourcesCleaned || _currentWifiRefreshRequested) continue;
                _currentWifiInfo = info;
                RenderCurrentWifi();
            }
        }
        finally
        {
            _nextCurrentWifiRefresh = DateTimeOffset.UtcNow.AddSeconds(5);
            _currentWifiRefreshRunning = false;
        }
    }

    private void RenderCurrentWifi()
    {
        var view = CurrentWifiPresentation.Create(_currentWifiInfo, _localizer);
        CurrentWifiTitle.Text = _localizer.T("WifiCurrentConnection");
        CurrentWifiNameText.Text = view.Name;
        CurrentWifiDetailsText.Text = view.Details;
    }
}
