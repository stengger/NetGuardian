using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App.Presentation;

internal static class ResponsiveLayout
{
    public static void ApplyWifi(FrameworkElement root)
    {
        var compact = root.ActualHeight < 600;
        var sections = (Grid)root.FindName("WifiListsGrid");
        var headerHeight = compact ? 0 : (double)root.FindResource("GridHeaderHeight");
        foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
        {
            var table = (DataGrid)root.FindName(name);
            table.HeadersVisibility = compact ? DataGridHeadersVisibility.Row : DataGridHeadersVisibility.All;
            table.ColumnHeaderHeight = headerHeight;
        }
        var filterHeight = (double)root.FindResource(compact ? "ExpansionHitSize" : "ControlMinHeight");
        root.Resources["WifiFilterControlHeight"] = filterHeight;
        var readableSection = filterHeight + headerHeight + (double)root.FindResource("GridRowHeight");
        // 筛选控件位于展开按钮的描边内，标题最小高度包含上下描边。
        var headerStroke = (Thickness)root.FindResource("StrokeControlThickness");
        sections.Resources["WifiNearbyMinimumHeight"] = readableSection + headerStroke.Top + headerStroke.Bottom + ((Thickness)root.FindResource("SpacingFieldGap")).Bottom;
        sections.Resources["WifiPriorityMinimumHeight"] = readableSection;
        var context = (Expander)root.FindName("CurrentWifiDetailsExpander");
        // 只在跨越紧凑断点时调整默认展开状态，日常刷新保留用户的选择。
        if (context.Tag is not bool previousCompact || previousCompact != compact)
        {
            context.IsExpanded = !compact;
            context.Tag = compact;
        }
        ((Border)root.FindName("CurrentWifiCard")).SetResourceReference(Border.PaddingProperty,
            compact ? "WifiContextCompactPadding" : "SpacingControlPadding");
        foreach (var name in new[] { "CurrentWifiCard", "WifiSelectionBar" })
            ((FrameworkElement)root.FindName(name)).SetResourceReference(FrameworkElement.MarginProperty,
                compact ? "SpacingWifiContextCompactGap" : "SpacingWifiContextGap");
        ((ScrollViewer)root.FindName("DnsEditorScroller")).MaxHeight = compact ? 140 : 290;
        // 短窗口单行滚动操作栏，避免英文按钮换行挤掉列表数据行。
        ((ScrollViewer)root.FindName("WifiPageActions")).HorizontalScrollBarVisibility =
            compact ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ((FrameworkElement)root.FindName("DragHintText")).Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ((Border)root.FindName("WifiSelectionBar")).Padding = new Thickness(12, compact ? 4 : 8, 12, compact ? 4 : 8);
        ((FrameworkElement)root.FindName("WifiScanStatusPanel")).SetResourceReference(FrameworkElement.MarginProperty,
            compact ? "SpacingNone" : "SpacingFieldGap");
    }
}
