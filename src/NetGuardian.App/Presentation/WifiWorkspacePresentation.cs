using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App.Presentation;

// 复用原有按钮的可用性，只切换外观，不决定扫描或连接动作。
internal static class WifiWorkspacePresentation
{
    public static void ApplyActions(FrameworkElement root, bool isNetworkActionRunning)
    {
        var connect = (Button)root.FindName("ConnectButton");
        var scan = (Button)root.FindName("ScanButton");
        var connectPrimary = connect.IsEnabled || isNetworkActionRunning;
        scan.Style = (Style)root.FindResource(connectPrimary ? "ToolbarFixedButtonStyle" : "ToolbarPrimaryButtonStyle");
        connect.Style = (Style)root.FindResource(connectPrimary ? "ToolbarPrimaryButtonStyle" : "ToolbarFixedButtonStyle");
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
