using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App;

public partial class MainWindow
{
    private DateTimeOffset? _lastSuccessfulWifiScan;

    private void WifiMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void RefreshWifiWorkspaceSummary()
    {
        WifiRecoverySummaryText.Text = string.Format(_localizer.T("WifiRecoverySummary"), _profiles.Count(p => p.Enabled), _profiles.Count);
        WifiRecoverySummaryText.ToolTip = WifiRecoverySummaryText.Text;
        WifiMoreButton.Content = _localizer.T("WifiMore");
        WifiLastScanText.Text = string.Format(_localizer.T("WifiLastScan"), _lastSuccessfulWifiScan?.ToString("HH:mm:ss") ?? "—");
        var connectPrimary = ConnectButton.IsEnabled || _isNetworkActionRunning;
        ScanButton.Style = (Style)FindResource(connectPrimary ? "ToolbarFixedButtonStyle" : "ToolbarPrimaryButtonStyle");
        ConnectButton.Style = (Style)FindResource(connectPrimary ? "ToolbarPrimaryButtonStyle" : "ToolbarFixedButtonStyle");
    }
}
