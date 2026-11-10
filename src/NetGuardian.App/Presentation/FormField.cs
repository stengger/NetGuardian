using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NetGuardian.App.Presentation;

// 校验状态仅供控件样式使用，不进入配置或业务模型。
public static class FormField
{
    public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.RegisterAttached(
        "IsInvalid", typeof(bool), typeof(FormField), new PropertyMetadata(false));
    public static readonly DependencyProperty RevealLabelProperty = DependencyProperty.RegisterAttached(
        "RevealLabel", typeof(string), typeof(FormField), new PropertyMetadata(null, RevealLabelChanged));

    public static bool GetIsInvalid(DependencyObject element) => (bool)element.GetValue(IsInvalidProperty);
    public static void SetIsInvalid(DependencyObject element, bool value) => element.SetValue(IsInvalidProperty, value);
    public static string? GetRevealLabel(DependencyObject element) => (string?)element.GetValue(RevealLabelProperty);
    public static void SetRevealLabel(DependencyObject element, string value) => element.SetValue(RevealLabelProperty, value);

    private static void RevealLabelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Control control) return;
        control.Loaded -= PrepareReveal;
        control.Loaded += PrepareReveal;
        if (control.IsLoaded) PrepareReveal(control, new RoutedEventArgs());
    }

    private static void PrepareReveal(object sender, RoutedEventArgs args)
    {
        var control = (Control)sender;
        if (control.Template?.FindName("RevealButton", control) is not Button reveal) return;
        reveal.IsTabStop = true;
        AutomationProperties.SetName(reveal, GetRevealLabel(control) ?? "");
        AutomationProperties.SetHelpText(reveal, AutomationProperties.GetHelpText(control));
    }
}
