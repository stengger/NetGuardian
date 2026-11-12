using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class ButtonFocusPresentationTests
{
    [Theory]
    [InlineData("ConnectButton")]
    [InlineData("RemoveButton")]
    public void 业务按钮使用单一键盘装饰且焦点不改变尺寸(string name)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var button = (Button)window.FindName(name);
            button.IsEnabled = true;
            DesktopFixture.Layout(window, 1040, 660);
            var before = button.RenderSize;
            Keyboard.Focus(button);
            window.UpdateLayout();
            Assert.Equal(before, button.RenderSize);
            Assert.NotNull(button.FocusVisualStyle);
            // 模板不再按 IsKeyboardFocused 绘制另一层焦点框，鼠标聚焦不产生双框。
            Assert.Null(button.Template.FindName("FocusRing", button));
            var sample = new Control { Style = button.FocusVisualStyle, Resources = window.Resources };
            sample.ApplyTemplate();
            var ring = Assert.IsType<Border>(sample.Template.FindName("KeyboardFocusRing", sample));
            Assert.Equal(new Thickness(2), ring.BorderThickness);
        });
    }

    [Fact]
    public void 测速按钮复用业务按钮模板与焦点样式()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "ToolsPanel");
            ((TabControl)window.FindName("ToolsTabControl")).SelectedItem = window.FindName("SpeedToolsTab");
            DesktopFixture.Layout(window, 1040, 660);
            var view = (FrameworkElement)window.FindName("SpeedView");
            var button = (Button)view.FindName("RunButton");
            var connect = (Button)window.FindName("ConnectButton");
            Assert.Same(connect.Template, button.Template);
            Assert.Same(connect.FocusVisualStyle, button.FocusVisualStyle);
        });
    }
}
