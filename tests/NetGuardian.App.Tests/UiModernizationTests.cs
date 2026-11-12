using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class UiModernizationTests
{
    [Theory]
    [InlineData(1040, 660)]
    [InlineData(1180, 758)]
    [InlineData(1600, 1000)]
    public void 多条展开提示保留正文和两个无线列表入口(double width, double height)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var host = (FeedbackPresenter)window.FindName("RootSnackbar");
            for (var i = 0; i < 3; i++)
                host.ShowMessage("审查错误" + i, "摘要\n" + new string('长', 500), "Danger");
            foreach (var expander in DesktopFixture.Descendants(host).OfType<Expander>()) expander.IsExpanded = true;
            DesktopFixture.Layout(window, width, height);
            var panel = (FrameworkElement)window.FindName("WifiPanel");
            var scroller = (ScrollViewer)host.Parent;
            Assert.True(scroller.ActualHeight <= height - 560, $"提示挤压正文：{scroller.ActualHeight}");
            foreach (var name in new[] { "NearbyNetworksExpander", "PriorityNetworksExpander" })
            {
                var expander = (FrameworkElement)window.FindName(name);
                var bounds = expander.TransformToAncestor(panel).TransformBounds(new Rect(expander.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= panel.ActualHeight + 1, $"{name} 越出正文：{bounds} / {panel.ActualHeight}");
            }
            Assert.True(scroller.ScrollableHeight > 0);
            DesktopFixture.Save(window, $"modern-feedback-{width}-{height}.png");
        });
    }

}
