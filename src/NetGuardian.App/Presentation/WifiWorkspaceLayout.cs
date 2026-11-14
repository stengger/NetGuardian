using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace NetGuardian.App.Presentation;

internal static class WifiWorkspaceLayout
{
    private sealed class State
    {
        internal WifiLayoutPreferences Preferences = new();
        internal bool Persistent, Horizontal, Applying;
    }
    private static readonly ConditionalWeakTable<FrameworkElement, State> States = new();

    internal static void EnablePersistence(FrameworkElement root)
    {
        var state = GetState(root);
        Restore(root, WifiLayoutPreferences.Load());
        state.Persistent = true;
    }

    internal static WifiLayoutPreferences Snapshot(FrameworkElement root) => GetState(root).Preferences;

    internal static void Restore(FrameworkElement root, WifiLayoutPreferences preferences)
    {
        var state = GetState(root);
        state.Preferences = preferences with { HorizontalRatio = WifiLayoutPreferences.Normalize(preferences.HorizontalRatio),
            VerticalRatio = WifiLayoutPreferences.Normalize(preferences.VerticalRatio) };
        state.Applying = true;
        ((Expander)root.FindName("NearbyNetworksExpander")).IsExpanded = state.Preferences.NearbyExpanded;
        ((Expander)root.FindName("PriorityNetworksExpander")).IsExpanded = state.Preferences.PriorityExpanded;
        state.Applying = false;
        Apply(root);
    }

    private static State GetState(FrameworkElement root) => States.GetValue(root, owner =>
    {
        var state = new State();
        foreach (var name in new[] { "NearbyNetworksExpander", "PriorityNetworksExpander" })
        {
            var section = (Expander)owner.FindName(name);
            section.Expanded += Changed;
            section.Collapsed += Changed;
        }
        void Changed(object sender, RoutedEventArgs args)
        {
            if (state.Applying) return;
            Remember(owner, state, ratio: false);
            Apply(owner);
        }
        ((GridSplitter)owner.FindName("WifiWorkspaceSplitter")).DragCompleted += (_, _) => Remember(owner, state, ratio: true);
        ((GridSplitter)owner.FindName("WifiWorkspaceSplitter")).PreviewKeyUp += (_, args) =>
        {
            if (args.Key is Key.Left or Key.Right or Key.Up or Key.Down) Remember(owner, state, ratio: true);
        };
        return state;
    });

    private static void Remember(FrameworkElement root, State state, bool ratio)
    {
        var region = (Grid)root.FindName("WifiListsGrid");
        var a = ((Expander)root.FindName("NearbyNetworksExpander")).IsExpanded;
        var b = ((Expander)root.FindName("PriorityNetworksExpander")).IsExpanded;
        var preferences = state.Preferences with { NearbyExpanded = a, PriorityExpanded = b };
        if (ratio && a && b)
        {
            var first = state.Horizontal ? region.ColumnDefinitions[0].ActualWidth : region.RowDefinitions[0].ActualHeight;
            var last = state.Horizontal ? region.ColumnDefinitions[2].ActualWidth : region.RowDefinitions[2].ActualHeight;
            var value = WifiLayoutPreferences.Normalize(first / Math.Max(1, first + last));
            preferences = state.Horizontal ? preferences with { HorizontalRatio = value } : preferences with { VerticalRatio = value };
        }
        state.Preferences = preferences;
        if (state.Persistent) preferences.Save();
    }

    internal static void Apply(FrameworkElement root)
    {
        var state = GetState(root);
        if (state.Applying) return;
        state.Applying = true;
        try
        {
            var region = (Grid)root.FindName("WifiListsGrid");
            var nearby = (FrameworkElement)root.FindName("NearbyNetworksCard");
            var priority = (FrameworkElement)root.FindName("PriorityNetworksCard");
            var splitter = (GridSplitter)root.FindName("WifiWorkspaceSplitter");
            var horizontal = root.ActualWidth >= (double)root.FindResource("WifiWorkspaceBreakpoint");
            var a = ((Expander)root.FindName("NearbyNetworksExpander")).IsExpanded;
            var b = ((Expander)root.FindName("PriorityNetworksExpander")).IsExpanded;
            state.Horizontal = horizontal;
            var ratio = horizontal ? state.Preferences.HorizontalRatio : state.Preferences.VerticalRatio;
            // 双栏折叠保留可操作标题宽度，竖排折叠自然只占标题高度。
            var first = a ? new GridLength(ratio, GridUnitType.Star) : horizontal ? new GridLength(168) : GridLength.Auto;
            var last = b ? new GridLength(1 - ratio, GridUnitType.Star) : horizontal ? new GridLength(168) : GridLength.Auto;
            var gap = new GridLength((double)root.FindResource("WifiSplitGap"));
            for (var i = 0; i < 3; i++)
            {
                var value = i == 0 ? first : i == 1 ? gap : last;
                region.ColumnDefinitions[i].Width = horizontal ? value : i == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                region.RowDefinitions[i].Height = horizontal ? i == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0) : value;
                // 约束分隔条不能把搜索或核心列拖出面板。
                region.ColumnDefinitions[i].MinWidth = horizontal && i != 1 && (i == 0 ? a : b)
                    ? Math.Min(296, Math.Max(0, (region.ActualWidth - gap.Value) / 2)) : 0;
                region.RowDefinitions[i].MinHeight = !horizontal && i != 1 && (i == 0 ? a : b)
                    ? Math.Min(122, Math.Max(0, (region.ActualHeight - gap.Value) / 2)) : 0;
            }
            Grid.SetColumn(nearby, 0); Grid.SetRow(nearby, 0);
            Grid.SetColumn(priority, horizontal ? 2 : 0); Grid.SetRow(priority, horizontal ? 0 : 2);
            Grid.SetColumn(splitter, horizontal ? 1 : 0); Grid.SetRow(splitter, horizontal ? 0 : 1);
            splitter.ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows;
            splitter.Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS;
            splitter.IsEnabled = a && b;
            foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
            {
                var table = (DataGrid)root.FindName(name);
                table.HeadersVisibility = DataGridHeadersVisibility.All;
                table.ColumnHeaderHeight = (double)root.FindResource("WifiTableHeaderHeight");
                table.SizeChanged -= TableResized;
                table.SizeChanged += TableResized;
                ApplyColumns(table);
            }
            var summary = (TextBlock)root.FindName("WifiRecoverySummaryText");
            summary.MaxWidth = Math.Max(100, ((FrameworkElement)root.FindName("CurrentWifiCard")).ActualWidth * .42);
            summary.TextTrimming = TextTrimming.CharacterEllipsis;
            summary.ToolTip = summary.Text;
        }
        finally { state.Applying = false; }
    }

    private static void TableResized(object sender, SizeChangedEventArgs args) => ApplyColumns((DataGrid)sender);
    private static void ApplyColumns(DataGrid table)
    {
        var optional = table.Name == "NearbyGrid" ? new[] { 3 } : new[] { 3, 5 };
        var required = table.RowHeaderWidth + 4 + table.Columns.Sum(c => c.Width.IsAbsolute ? Math.Max(c.Width.Value, c.MinWidth) : Math.Max(180, c.MinWidth));
        foreach (var i in optional) table.Columns[i].Visibility = table.ActualWidth + .5 >= required ? Visibility.Visible : Visibility.Collapsed;
    }
}
