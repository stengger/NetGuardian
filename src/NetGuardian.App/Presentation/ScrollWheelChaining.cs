using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NetGuardian.App.Presentation;

public static class ScrollWheelChaining
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ScrollWheelChaining), new PropertyMetadata(false, EnabledChanged));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer) return;
        viewer.PreviewMouseWheel -= PreviewWheel;
        if ((bool)args.NewValue) viewer.PreviewMouseWheel += PreviewWheel;
    }

    private static void PreviewWheel(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || args.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None) return;
        var outer = (ScrollViewer)sender;
        var current = args.OriginalSource as DependencyObject;
        while (current is not null && current is not ScrollViewer)
            current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        if (current is not ScrollViewer inner || ReferenceEquals(inner, outer)) return;
        if (args.Delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight) return;
        if (args.Delta > 0 ? outer.VerticalOffset <= 0 : outer.VerticalOffset >= outer.ScrollableHeight) return;
        // 只在内部滚动已到边界时交给正文，由 WPF 自己处理行数和滚轮设置。
        args.Handled = true;
        outer.RaiseEvent(new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta)
        {
            RoutedEvent = Mouse.MouseWheelEvent
        });
    }
}
