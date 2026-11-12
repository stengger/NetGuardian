using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class ButtonStateTests
{
    [Theory]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Dark)]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Light)]
    public void 按钮反馈仅作用于表面且普通危险入口不使用实心填充(Wpf.Ui.Appearance.ApplicationTheme theme)
    {
        DesktopFixture.Run(window =>
        {
            var button = (Button)window.FindName("RemoveButton");
            button.IsEnabled = true;
            button.ApplyTemplate();
            Assert.Equal(((SolidColorBrush)window.FindResource("ControlFillColorDefaultBrush")).Color,
                Assert.IsType<SolidColorBrush>(button.Background).Color);
            Assert.NotNull(button.Template.FindName("InteractionLayer", button));
            foreach (var property in new[] { UIElement.IsMouseOverProperty, ButtonBase.IsPressedProperty })
            {
                var trigger = button.Template.Triggers.OfType<Trigger>().Single(t => t.Property == property);
                Assert.Contains(trigger.Setters.OfType<Setter>(), setter => setter.TargetName == "InteractionLayer");
                Assert.DoesNotContain(trigger.Setters.OfType<Setter>(), setter => setter.TargetName == "Chrome" && setter.Property == UIElement.OpacityProperty);
            }
        }, theme);
    }
}
