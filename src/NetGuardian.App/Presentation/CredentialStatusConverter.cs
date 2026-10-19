using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

public sealed class CredentialStatusConverter : IValueConverter
{
    public Localizer? Localizer { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var localizer = Localizer ?? new Localizer();
        return localizer.T(parameter as string == "hint" ? "CredentialStatusHint"
            : value is true ? "CredentialAvailable" : "CredentialUnconfirmed");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
