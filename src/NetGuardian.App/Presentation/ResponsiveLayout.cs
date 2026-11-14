using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App.Presentation;

internal static class ResponsiveLayout
{
    public static void ApplyWifi(FrameworkElement root)
    {
        var page = (FrameworkElement)root.FindName("WifiPanel");
        var compact = (page.ActualHeight > 0 ? page.ActualHeight : root.ActualHeight)
            < (double)root.FindResource("WifiContextBreakpoint");
        WifiWorkspaceLayout.Apply(root);
        ((ScrollViewer)root.FindName("DnsEditorScroller")).MaxHeight = (double)root.FindResource(compact ? "DnsEditorCompactMaxHeight" : "DnsEditorMaxHeight");
    }
}
