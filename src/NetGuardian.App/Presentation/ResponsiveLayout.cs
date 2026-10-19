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
        var wide = grid.ActualWidth >= 1120;
        Place(nearby, 0, 0, wide ? 2 : 1, wide ? 1 : 2, wide ? new Thickness(0, 0, 12, 0) : new Thickness(0, 0, 0, 12));
        Place(priority, wide ? 0 : 1, wide ? 1 : 0, wide ? 2 : 1, wide ? 1 : 2, new Thickness(0));
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
