using System;
using System.Windows;
using Wpf.Ui.Appearance;

namespace NetGuardian.App.Presentation;

internal static class AppThemePolicy
{
    internal static void BindBrush(FrameworkElement element, DependencyProperty property, string key) =>
        element.SetResourceReference(property, key);

    // Unknown 表示跟随系统；高对比不会改写用户保存的主题偏好。
    internal static ApplicationTheme Resolve(string mode, bool highContrast) => highContrast
        ? ApplicationTheme.HighContrast
        : mode switch
        {
            "light" => ApplicationTheme.Light,
            "dark" => ApplicationTheme.Dark,
            _ => ApplicationTheme.Unknown
        };

    internal static void Apply(string mode, bool highContrast, Action<ApplicationTheme> apply, Action applySystem)
    {
        var theme = Resolve(mode, highContrast);
        if (theme == ApplicationTheme.Unknown) applySystem();
        else apply(theme);
    }

    internal static ResourceDictionary CreatePalette(ApplicationTheme theme)
    {
        var name = theme switch
        {
            ApplicationTheme.Light => "Light",
            ApplicationTheme.HighContrast => "HighContrast",
            _ => "Dark"
        };
        return new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/NetGuardian;component/Themes/Palette.{name}.xaml")
        };
    }
}
