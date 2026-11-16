using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Presentation;

internal sealed class NearbyPriorityIndexConverter(NearbySavedWifiView nearby) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        nearby.GetPriority(value as WifiProfile)?.ToString(culture) ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}