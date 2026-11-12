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
        var shortWindow = root.ActualHeight < (double)root.FindResource("ShortWindowBreakpoint");
        var sections = (Grid)root.FindName("WifiListsGrid");
        var headerHeight = shortWindow ? 0 : (double)root.FindResource("GridHeaderHeight");
        foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
        {
            var table = (DataGrid)root.FindName(name);
            table.HeadersVisibility = shortWindow ? DataGridHeadersVisibility.Row : DataGridHeadersVisibility.All;
            table.ColumnHeaderHeight = headerHeight;
        }
        var filterHeight = (double)root.FindResource(compact ? "ExpansionHitSize" : "ControlMinHeight");
        root.Resources["WifiFilterControlHeight"] = filterHeight;
        var rowHeight = (double)root.FindResource("GridRowHeight");
        var readableSection = filterHeight + headerHeight + rowHeight;
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
        ((ScrollViewer)root.FindName("DnsEditorScroller")).MaxHeight = (double)root.FindResource(compact ? "DnsEditorCompactMaxHeight" : "DnsEditorMaxHeight");
        // 短窗口单行滚动操作栏，避免英文按钮换行挤掉列表数据行。
        ((ScrollViewer)root.FindName("WifiPageActions")).HorizontalScrollBarVisibility =
            shortWindow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        // 说明保留在可滚动区域内，不再因紧凑模式直接隐藏。
        ((FrameworkElement)root.FindName("DragHintText")).Visibility = Visibility.Visible;
        ((Border)root.FindName("WifiSelectionBar")).SetResourceReference(Border.PaddingProperty,
            compact ? "WifiSelectionCompactPadding" : "SpacingControlPadding");
        ((FrameworkElement)root.FindName("WifiScanStatusPanel")).SetResourceReference(FrameworkElement.MarginProperty,
            compact ? "SpacingNone" : "SpacingFieldGap");
    }
}
