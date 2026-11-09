using System;
using System.Globalization;
using System.Linq;
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
        grid.Loaded -= Loaded;
        grid.Loaded += Loaded;
    }

    private static void Loaded(object sender, RoutedEventArgs args) => Resize(sender, null!);

    internal static double RequiredWidth(DataGrid grid) => grid.RowHeaderWidth + SystemParameters.VerticalScrollBarWidth + 4
        + grid.Columns.Sum(column => column.Width.IsAbsolute ? Math.Max(column.Width.Value, column.MinWidth) : Math.Max(120, column.MinWidth));

    private static void Resize(object sender, SizeChangedEventArgs args)
    {
        var grid = (DataGrid)sender;
        foreach (var index in GetCompactColumns(grid).Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(index, out var column) && column >= 0 && column < grid.Columns.Count)
                grid.Columns[column].Visibility = grid.ActualWidth < RequiredWidth(grid) ? Visibility.Collapsed : Visibility.Visible;
    }

}

public sealed class RowDetailsToggleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Visible : Visibility.Collapsed;
}

// 原文与列表是并列区域；原文上限随实际可用高度变化。
public sealed class ViewportFractionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double height && double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction)
            ? Math.Max(0, height * fraction) : double.PositiveInfinity;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
