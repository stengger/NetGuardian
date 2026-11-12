using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using NetGuardian.Core.Monitoring;
using Wpf.Ui.Controls;

namespace NetGuardian.App.Presentation;

public static class WifiRowPresentation
{
    public static readonly DependencyProperty CurrentSsidProperty = DependencyProperty.RegisterAttached(
        "CurrentSsid", typeof(string), typeof(WifiRowPresentation), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    public static string? GetCurrentSsid(DependencyObject item) => (string?)item.GetValue(CurrentSsidProperty);
    public static void SetCurrentSsid(DependencyObject item, string? value) => item.SetValue(CurrentSsidProperty, value);
}

public sealed class WifiCurrentRowConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var matches = values.Length == 2 && values[0] is string ssid && values[1] is string current &&
            !string.IsNullOrWhiteSpace(current) && RecoveryPlanner.SsidEquals(ssid, current);
        return targetType == typeof(Visibility) ? matches ? Visibility.Visible : Visibility.Collapsed : matches;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class WifiSignalIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int signal
        ? signal switch { >= 75 => SymbolRegular.Wifi124, >= 50 => SymbolRegular.Wifi224, >= 25 => SymbolRegular.Wifi324, > 0 => SymbolRegular.Wifi424, _ => SymbolRegular.WifiOff24 }
        : SymbolRegular.WifiOff24;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
