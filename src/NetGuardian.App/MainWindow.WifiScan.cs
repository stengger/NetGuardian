using System.Threading.Tasks;
using System.Windows;
using NetGuardian.App.Presentation;

namespace NetGuardian.App;

public partial class MainWindow
{
    private readonly WifiScanSession _wifiScan = new();

    private async Task ScanWifiAsync()
    {
        if (_resourcesCleaned || _wifiScan.IsBusy) return;
        await _wifiScan.RunAsync(async token =>
        {
            await ImportSavedWifiProfilesAsync(token);
            token.ThrowIfCancellationRequested();
            return await _wifi.ScanAsync(token);
        }, () =>
        {
            if (_resourcesCleaned) return;
            _nearbyNetworks.Clear();
            if (_wifiScan.Networks is { } networks)
                foreach (var network in networks) _nearbyNetworks.Add(CreateNearbyWifiRow(network));
            RefreshWifiScanUi();
            SetToolOutput(WifiScanStatusText.Text);
            RefreshNearbyPriorityMarkers();
            UpdateWifiSelectionUi();
            UpdateEmptyStates();
        });
        if (_resourcesCleaned) return;
        SetToolOutput(WifiScanStatusText.Text);
        Log(WifiScanStatusText.Text, _wifiScan.Phase == WifiScanPhase.Failed ? UiLogLevel.Error : UiLogLevel.Information);
    }

    private void WifiScanCancel_Click(object sender, RoutedEventArgs e)
    {
        _wifiScan.Cancel();
        RefreshWifiScanUi();
        SetToolOutput(WifiScanStatusText.Text);
    }

    private void RefreshWifiScanUi()
    {
        ScanButton.IsEnabled = ToolsScanButton.IsEnabled = !_wifiScan.IsBusy;
        WifiScanCancelButton.Content = _localizer.T("Cancel");
        ToolsScanCancelButton.Content = _localizer.T("Cancel");
        WifiScanCancelButton.Visibility = _wifiScan.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        ToolsScanCancelButton.Visibility = WifiScanCancelButton.Visibility;
        WifiScanCancelButton.IsEnabled = _wifiScan.Phase == WifiScanPhase.Running;
        ToolsScanCancelButton.IsEnabled = WifiScanCancelButton.IsEnabled;
        WifiScanActivity.Visibility = _wifiScan.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        WifiScanStatusText.Text = _wifiScan.Phase switch
        {
            WifiScanPhase.Running => _localizer.T("Scanning"),
            WifiScanPhase.Cancelling => _localizer.T("WifiScanCancelling"),
            WifiScanPhase.Cancelled => _localizer.T("WifiScanCancelled"),
            WifiScanPhase.Failed => $"{_localizer.T("WifiScanFailed")} {_wifiScan.Error}",
            WifiScanPhase.Complete => string.Format(_localizer.T("ScanDone"), _wifiScan.Networks?.Count ?? 0),
            _ => _localizer.T("NoNearbyWifiRows")
        };
        NearbyEmptyText.Text = _localizer.T(_wifiScan.Phase == WifiScanPhase.Complete ? "WifiScanNone" : "WifiScanNoCurrentResults");
    }
}
