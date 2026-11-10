using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NetGuardian.App.Presentation;

// 只跟随原生展开状态；加载和回收时清除动画时钟，避免显示上一行的中间角度。
public static class ExpansionAnimation
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.RegisterAttached(
        "IsExpanded", typeof(bool), typeof(ExpansionAnimation), new PropertyMetadata(false, Changed));
    private static readonly DependencyProperty IsTrackingProperty = DependencyProperty.RegisterAttached(
        "IsTracking", typeof(bool), typeof(ExpansionAnimation), new PropertyMetadata(false));

    public static bool GetIsExpanded(DependencyObject element) => (bool)element.GetValue(IsExpandedProperty);
    public static void SetIsExpanded(DependencyObject element, bool value) => element.SetValue(IsExpandedProperty, value);

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        if (!(bool)element.GetValue(IsTrackingProperty))
        {
            element.SetValue(IsTrackingProperty, true);
            element.Loaded += Synchronize;
            element.Unloaded += Synchronize;
        }
        Apply(element, element.IsLoaded && SystemParameters.ClientAreaAnimation);
    }

    private static void Synchronize(object sender, RoutedEventArgs args) => Apply((FrameworkElement)sender, false);

    private static void Apply(FrameworkElement element, bool animate)
    {
        if (element.RenderTransform is not RotateTransform rotation) return;
        // 模板中的 Freezable 可能被 WPF 冻结共享，动画必须使用当前图标自己的实例。
        if (rotation.IsFrozen)
        {
            rotation = rotation.CloneCurrentValue();
            element.RenderTransform = rotation;
        }
        var from = rotation.Angle;
        var target = GetIsExpanded(element) ? 90d : 0d;
        rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        rotation.Angle = target;
        // 从视觉树移除到 Unloaded 派发之间，IsLoaded 仍可能为真，但已无法查找主题资源。
        if (!animate || element.TryFindResource("ExpansionAnimationDuration") is not Duration duration) return;
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(from, target, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);
    }
}
