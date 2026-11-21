using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App.Presentation;

internal static class EventLogPresentation
{
    public static void ImportHistory(DataGrid grid, EventLogStore store, IEnumerable<PersistedLogEntry> entries, DateTimeOffset now)
    {
        // 有筛选条件的 WPF 视图在导入时会查询当前位置，逐条通知必须在视图可用时发送。
        var selected = grid.SelectedItem as UiLogEntry;
        store.Import(entries, now);
        if (selected is not null && grid.SelectedItem is null && grid.Items.Contains(selected)) grid.SelectedItem = selected;
    }

    public static void UpdateEmptyActions(FrameworkElement root, int total, int visible)
    {
        ((TextBlock)root.FindName("LogEmptyText")).Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
        ((Button)root.FindName("LogEmptyAction")).Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        ((Button)root.FindName("LogResetFilterButton")).Visibility = total > 0 && visible == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static void ResetFilters(FrameworkElement root, bool recoveryOnly = false)
    {
        ((TextBox)root.FindName("LogSearchTextBox")).Clear();
        ((ComboBox)root.FindName("LogLevelComboBox")).SelectedIndex = 0;
        var source = (ComboBox)root.FindName("LogSourceComboBox");
        source.IsEnabled = !recoveryOnly;
        source.SelectedItem = source.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, recoveryOnly ? "Recovery" : "all"));
    }
}
