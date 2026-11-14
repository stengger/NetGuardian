using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace NetGuardian.App.Presentation;

internal static class WifiWorkspaceLayout
{
    private sealed class State
    {
        internal WifiLayoutPreferences Preferences = new();
        internal bool Persistent, Horizontal, Applying;
    }
    private static readonly ConditionalWeakTable<FrameworkElement, State> States = new();
    private sealed class ColumnUpdate
    {
        internal bool Pending, Manual;
        internal double? LastAutomaticWidth;
    }
    private static readonly ConditionalWeakTable<DataGrid, ColumnUpdate> ColumnUpdates = new();

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
            var a = ((Expander)root.FindName("NearbyNetworksExpander")).IsExpanded;
            var b = ((Expander)root.FindName("PriorityNetworksExpander")).IsExpanded;
            var nearbyTable = (DataGrid)root.FindName("NearbyGrid");
            var priorityTable = (DataGrid)root.FindName("ProfileGrid");
            var leftMinimum = a ? MinimumWidth(nearbyTable) : 192;
            var rightMinimum = b ? MinimumWidth(priorityTable) : 192;
            var gap = new GridLength((double)root.FindResource("WifiSplitGap"));
            var horizontal = root.ActualWidth >= (double)root.FindResource("WifiWorkspaceBreakpoint")
                && region.ActualWidth + .5 >= leftMinimum + rightMinimum + gap.Value;
            state.Horizontal = horizontal;
            foreach (var name in new[] { "NearbySectionHeader", "PrioritySectionHeader" })
                ((Views.ListSectionHeader)root.FindName(name)).IsCompact = !horizontal;
            var page = (FrameworkElement)root.FindName("WifiPanel");
            var shortViewport = !horizontal && root.ActualHeight < 600;
            // 短工作区缩减留白，所有字段及操作仍保留在各自分区中。
            page.Resources["WifiSectionGap"] = new Thickness(0, 0, 0, shortViewport ? 4 : 20);
            page.Resources["WifiSectionContentGap"] = new Thickness(0, shortViewport ? 0 : 8, 0, 0);
            page.Resources["WifiSummaryPadding"] = new Thickness(12, shortViewport ? 0 : 4, 12, shortViewport ? 0 : 4);
            var actions = (FrameworkElement)root.FindName("WifiPageActions");
            actions.Resources["WifiPrimaryActionWidth"] = shortViewport ? 112d : 144d;
            actions.Resources["ToolbarControlPadding"] = shortViewport ? new Thickness(8, 0, 8, 0) : root.FindResource("ToolbarControlPadding");
            var viewport = (ScrollViewer)root.FindName("WifiContentScrollViewer");
            if (shortViewport) viewport.Padding = new Thickness(0);
            else viewport.SetResourceReference(Control.PaddingProperty, "SpacingScrollViewport");
            var ratio = horizontal ? state.Preferences.HorizontalRatio : state.Preferences.VerticalRatio;
            if (horizontal && a && b)
                ratio = Math.Clamp(ratio, leftMinimum / (region.ActualWidth - gap.Value), 1 - rightMinimum / (region.ActualWidth - gap.Value));
            // 双栏折叠保留可操作标题宽度，竖排折叠自然只占标题高度。
            var first = a ? new GridLength(ratio, GridUnitType.Star) : horizontal ? new GridLength(192) : GridLength.Auto;
            var last = b ? new GridLength(1 - ratio, GridUnitType.Star) : horizontal ? new GridLength(192) : GridLength.Auto;
            for (var i = 0; i < 3; i++)
            {
                var value = i == 0 ? first : i == 1 ? gap : last;
                region.ColumnDefinitions[i].Width = horizontal ? value : i == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                region.RowDefinitions[i].Height = horizontal ? i == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0) : value;
                // 约束分隔条不能把搜索或核心列拖出面板。
                region.ColumnDefinitions[i].MinWidth = horizontal && i != 1 && (i == 0 ? a : b)
                    ? i == 0 ? leftMinimum : rightMinimum : 0;
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
                QueueColumns(table);
            }
            var summary = (TextBlock)root.FindName("WifiRecoverySummaryText");
            summary.MaxWidth = Math.Max(100, ((FrameworkElement)root.FindName("CurrentWifiCard")).ActualWidth * .42);
            summary.TextTrimming = TextTrimming.CharacterEllipsis;
            summary.ToolTip = summary.Text;
            ((FrameworkElement)root.FindName("WifiSelectionBar")).MaxWidth = Math.Max(0, priority.ActualWidth - (horizontal ? 16 : 240));
        }
        finally { state.Applying = false; }
    }

    private static double MinimumWidth(DataGrid table) => table.RowHeaderWidth
        + (double)table.FindResource("OverlayScrollHitWidth")
        + table.Columns.Where(c => table.Name == "NearbyGrid" || c != table.Columns[3] && c != table.Columns[5])
            .Sum(c => c.MinWidth);

    private static void TableResized(object sender, SizeChangedEventArgs args) => QueueColumns((DataGrid)sender);
    private static void QueueColumns(DataGrid table)
    {
        var state = ColumnUpdates.GetOrCreateValue(table);
        if (state.Pending) return;
        state.Pending = true;
        // 合并同一轮布局触发的列更新，等视口完成测量后再使用实际宽度预算。
        table.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            state.Pending = false;
            ApplyColumns(table);
        }));
    }
    private static void ApplyColumns(DataGrid table)
    {
        if (table.ActualWidth <= 0) return;
        var state = ColumnUpdates.GetOrCreateValue(table);
        var fill = table.Columns[table.Name == "NearbyGrid" ? 0 : 1];
        // 自动填充使用视口逻辑宽度预算；用户改过名称列后不再覆盖其选择。
        if (state.LastAutomaticWidth is { } previous && fill.Width.IsAbsolute && Math.Abs(fill.Width.Value - previous) > .5)
            state.Manual = true;
        var optional = table.Name == "NearbyGrid" ? Array.Empty<int>() : new[] { 3, 5 };
        var reserve = (double)table.FindResource("OverlayScrollHitWidth");
        var required = table.RowHeaderWidth + reserve + table.Columns.Sum(c => ReferenceEquals(c, fill)
            ? Math.Max(state.Manual ? fill.Width.Value : c.MinWidth, c.MinWidth) : c.Width.IsAbsolute ? Math.Max(c.Width.Value, c.MinWidth) : c.MinWidth);
        foreach (var i in optional) table.Columns[i].Visibility = table.ActualWidth + .5 >= required ? Visibility.Visible : Visibility.Collapsed;
        if (state.Manual) return;
        var remaining = Math.Max(fill.MinWidth, table.ActualWidth - table.RowHeaderWidth - reserve - table.Columns
            .Where(c => !ReferenceEquals(c, fill) && c.Visibility == Visibility.Visible)
            .Sum(c => c.Width.IsAbsolute ? Math.Max(c.Width.Value, c.MinWidth) : c.ActualWidth));
        fill.Width = new DataGridLength(remaining);
        state.LastAutomaticWidth = remaining;
    }
}
