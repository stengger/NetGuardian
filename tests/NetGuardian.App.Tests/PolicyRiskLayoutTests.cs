using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class PolicyRiskLayoutTests
{
    [Theory]
    [InlineData(1040, 660)]
    [InlineData(960, 500)]
    public void 短窗口可滚动到完整风险说明及探测目标(double width, double height)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960; window.MinHeight = 500;
            DesktopFixture.ShowPage(window, "PolicyPanel");
            DesktopFixture.Layout(window, width, height);
            foreach (var name in new[] { "HighRiskTipText", "HttpsProbeHostsTextBox" })
            {
                var control = (FrameworkElement)window.FindName(name);
                control.BringIntoView();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                window.UpdateLayout();
                var bounds = control.TransformToAncestor(window).TransformBounds(new Rect(control.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= height - 12, $"{name} 不可达：{bounds}");
                DesktopFixture.Save(window, $"policy-{name}-{width}.png");
            }
        });
    }
}
