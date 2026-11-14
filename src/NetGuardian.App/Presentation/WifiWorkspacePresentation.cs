using System.Windows;
using System.Windows.Controls;
using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

// 复用原有按钮的可用性，只切换外观，不决定扫描或连接动作。
internal static class WifiWorkspacePresentation
{
    public static void ApplyActions(FrameworkElement root, bool isNetworkActionRunning)
    {
        var connect = (Button)root.FindName("ConnectButton");
        var scan = (Button)root.FindName("ScanButton");
        var add = (Button)root.FindName("AddSelectedButton");
        var scanning = ((Button)root.FindName("WifiScanCancelButton")).Visibility == Visibility.Visible;
        var connectPrimary = !scanning && (connect.IsEnabled || isNetworkActionRunning);
        var addPrimary = !scanning && !connectPrimary && add.IsEnabled
            && ((DataGrid)root.FindName("NearbyGrid")).SelectedItem is not null;
        scan.Style = (Style)root.FindResource(connectPrimary || addPrimary ? "ToolbarFixedButtonStyle" : "WifiToolbarPrimaryButtonStyle");
        connect.Style = (Style)root.FindResource(connectPrimary ? "WifiToolbarPrimaryButtonStyle" : "ToolbarFixedButtonStyle");
        add.Style = (Style)root.FindResource(addPrimary ? "WifiToolbarAccentButtonStyle" : "ToolbarButtonStyle");
    }

    internal static void ApplyDisabledReasons(FrameworkElement root, Localizer localizer, bool busy, int profileIndex, string? nearbySsid)
    {
        Set("ScanButton", ((Button)root.FindName("ScanButton")).IsEnabled ? "ScanTip" : "Scanning");
        var add = (Button)root.FindName("AddSelectedButton");
        Set("AddSelectedButton", busy ? "ManualNetworkRunning" : add.IsEnabled ? "AddSelectedTip" : "SelectNearbyWifiFirst");
        if (!busy && !add.IsEnabled && nearbySsid is not null)
            add.ToolTip = string.Format(localizer.T("WifiProfileExists"), nearbySsid);
        Set("RemoveButton", busy ? "ManualNetworkRunning" : profileIndex < 0 ? "SelectProfileFirst" : "RemoveWifiTip");
        Set("MoveUpButton", busy ? "ManualNetworkRunning" : profileIndex < 0 ? "SelectProfileFirst" : profileIndex == 0 ? "AlreadyFirstProfile" : "MoveUpTip");
        Set("MoveDownButton", busy ? "ManualNetworkRunning" : profileIndex < 0 ? "SelectProfileFirst"
            : !((Button)root.FindName("MoveDownButton")).IsEnabled ? "AlreadyLastProfile" : "MoveDownTip");
        void Set(string name, string key)
        {
            var button = (Button)root.FindName(name);
            button.ToolTip = localizer.T(key);
            ToolTipService.SetShowOnDisabled(button, true);
            System.Windows.Automation.AutomationProperties.SetHelpText(button, button.ToolTip.ToString());
        }
    }

    public static void ApplyScanStatus(FrameworkElement root, string lastScan, string status, bool failed)
    {
        var caption = (TextBlock)root.FindName("WifiLastScanText");
        caption.Text = lastScan;
        caption.ToolTip = $"{lastScan}\n{status}";
        var state = (Button)root.FindName("WifiScanStateButton");
        state.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        state.ToolTip = status;
        System.Windows.Automation.AutomationProperties.SetName(state, status);
    }
}
