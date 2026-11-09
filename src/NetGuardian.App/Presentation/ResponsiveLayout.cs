using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App.Presentation;

internal static class ResponsiveLayout
{
    public static void ApplyWifi(FrameworkElement root)
    {
        var grid = (Grid)root.FindName("WifiListsGrid");
        var nearby = (Border)root.FindName("NearbyNetworksCard");
        var priority = (Border)root.FindName("PriorityNetworksCard");
        var compact = root.ActualHeight < 600;
        ((ScrollViewer)root.FindName("DnsEditorScroller")).MaxHeight = compact ? 140 : 290;
        // 短窗口单行滚动操作栏，避免英文按钮换行挤掉列表数据行。
        ((ScrollViewer)root.FindName("WifiPageActions")).HorizontalScrollBarVisibility =
            compact ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ((FrameworkElement)root.FindName("DragHintText")).Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ((Border)root.FindName("WifiSelectionBar")).Padding = new Thickness(12, compact ? 4 : 8, 12, compact ? 4 : 8);
        ((FrameworkElement)root.FindName("WifiScanStatusPanel")).Margin = new Thickness(0, 0, 0, compact ? 4 : 12);
        foreach (var title in new[] { "NearbyNetworksTitle", "PriorityListTitle" })
            ((FrameworkElement)root.FindName(title)).Margin = new Thickness(0, 0, 0, compact ? 6 : 10);
        // 高度交给页面滚动；只有两表的完整列宽预算都满足时才并排。
        var nearbyWidth = ResultTablePresentation.RequiredWidth((DataGrid)root.FindName("NearbyGrid"));
        var priorityWidth = ResultTablePresentation.RequiredWidth((DataGrid)root.FindName("ProfileGrid"));
        var wide = grid.ActualWidth >= nearbyWidth + priorityWidth + 12;
        grid.ColumnDefinitions[0].Width = new GridLength(nearbyWidth + 12, GridUnitType.Star);
        grid.ColumnDefinitions[1].Width = new GridLength(priorityWidth, GridUnitType.Star);
        Place(nearby, 0, 0, 1, wide ? 1 : 2, wide ? new Thickness(0, 0, 12, 0) : new Thickness(0, 0, 0, 24));
        Place(priority, wide ? 0 : 1, wide ? 1 : 0, 1, wide ? 1 : 2, new Thickness(0));
    }

    private static void Place(Border card, int row, int column, int rows, int columns, Thickness margin)
    {
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
        Grid.SetRowSpan(card, rows);
        Grid.SetColumnSpan(card, columns);
        card.Margin = margin;
    }
}
