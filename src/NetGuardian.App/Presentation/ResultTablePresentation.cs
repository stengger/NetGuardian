using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace NetGuardian.App.Presentation;

// 仅改变列的可见性；完整原值仍可从行详情和原生复制命令读取。
public static class ResultTablePresentation
{
    public static readonly DependencyProperty CompactColumnsProperty = DependencyProperty.RegisterAttached(
        "CompactColumns", typeof(string), typeof(ResultTablePresentation), new PropertyMetadata("", Changed));

    public static string GetCompactColumns(DependencyObject target) => (string)target.GetValue(CompactColumnsProperty);
    public static void SetCompactColumns(DependencyObject target, string value) => target.SetValue(CompactColumnsProperty, value);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not DataGrid grid) return;
        grid.SizeChanged -= Resize;
        grid.SizeChanged += Resize;
    }

    private static void Resize(object sender, SizeChangedEventArgs args)
    {
        var grid = (DataGrid)sender;
        foreach (var index in GetCompactColumns(grid).Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(index, out var column) && column >= 0 && column < grid.Columns.Count)
                grid.Columns[column].Visibility = grid.ActualWidth < 920 ? Visibility.Collapsed : Visibility.Visible;
    }

}

public sealed class RowDetailsToggleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Visible : Visibility.Collapsed;
}
