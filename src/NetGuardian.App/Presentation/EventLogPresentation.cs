using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App.Presentation;

internal static class EventLogPresentation
{
    public static void UpdateEmptyActions(FrameworkElement root, int total, int visible)
    {
        ((TextBlock)root.FindName("LogEmptyText")).Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
        ((Button)root.FindName("LogEmptyAction")).Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        ((Button)root.FindName("LogResetFilterButton")).Visibility = total > 0 && visible == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static void ResetFilters(FrameworkElement root)
    {
        ((TextBox)root.FindName("LogSearchTextBox")).Clear();
        ((ComboBox)root.FindName("LogLevelComboBox")).SelectedIndex = 0;
        ((ComboBox)root.FindName("LogSourceComboBox")).SelectedIndex = 0;
    }
}
