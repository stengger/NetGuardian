using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;

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

    private void ShowAllWifiProfiles_Click(object sender, RoutedEventArgs e)
    {
        _nearbySavedWifi.SetShowAll(ShowAllWifiProfilesMenuItem.IsChecked);
        UpdateWifiSelectionUi();
        UpdateEmptyStates();
    }

    private void RefreshWifiWorkspaceSummary()
    {
        WifiRecoverySummaryText.Text = string.Format(_localizer.T("WifiRecoverySummary"), _profiles.Count(p => p.IsRecoveryCandidate), _profiles.Count);
        WifiRecoverySummaryText.ToolTip = _localizer.T("WifiRecoverySummaryHint");
        ShowAllWifiProfilesMenuItem.Header = _localizer.T("ShowAllWifiProfiles");
        ProfileEmptyText.Text = _localizer.T(_nearbySavedWifi.ShowsAll ? "NoConfiguredWifiRows" : "NoPriorityWifiRows");
        WifiMoreButton.Content = _localizer.T("WifiMore");
        WifiWorkspacePresentation.ApplyScanStatus(this,
            string.Format(_localizer.T("WifiLastScan"), _lastSuccessfulWifiScan?.ToString("HH:mm:ss") ?? "—"),
            WifiScanStatusText.Text, _wifiScan.Phase == WifiScanPhase.Failed);
        WifiWorkspacePresentation.ApplyActions(this, _isNetworkActionRunning);
        ScanButton.ToolTip = _localizer.T(ScanButton.IsEnabled ? "ScanTip" : "Scanning");
        System.Windows.Automation.AutomationProperties.SetHelpText(ScanButton, ScanButton.ToolTip.ToString());
        WifiScanCancelButton.ToolTip = _localizer.T(WifiScanCancelButton.IsEnabled ? "Cancel" : "WifiScanCancelling");
    }
}
