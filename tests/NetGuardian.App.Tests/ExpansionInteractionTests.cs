using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class ExpansionInteractionTests
{
    [Fact]
    public void 行头展开箭头随原生状态旋转且热区不会随详情下移()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "NetworkPanel");
            var grid = (DataGrid)window.FindName("AdapterGrid");
            grid.ItemsSource = new[] { new { Name = "实验室接口", Type = "无线", Status = "已连接", Ipv4 = "192.0.2.1", Gateway = "192.0.2.254", DnsServers = new string('长', 600) } };
            DesktopFixture.Layout(window, 1180, 760);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var toggle = DesktopFixture.Descendants(row).OfType<ToggleButton>().First();
            Assert.True(toggle.ActualWidth >= 32 && toggle.ActualHeight >= 32, $"展开热区仅 {toggle.RenderSize}");
            var top = toggle.TranslatePoint(new Point(), row).Y;
            var border = row.BorderThickness;
            var peer = UIElementAutomationPeer.CreatePeerForElement(toggle)!;
            var provider = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!;
            provider.Toggle();
            Settle(window);
            var glyph = Glyph(toggle);
            Assert.Equal(new Point(0.5, 0.5), glyph.RenderTransformOrigin);
            Assert.Equal(glyph.ActualWidth, glyph.ActualHeight);
            Assert.True(glyph.ActualWidth > 0);
            Assert.InRange(((RotateTransform)glyph.RenderTransform).Angle, 89.9, 90.1);
            Assert.Equal(ToggleState.On, provider.ToggleState);
            Assert.InRange(toggle.TranslatePoint(new Point(), row).Y, top - 1, top + 1);
            Assert.Equal(border, row.BorderThickness);
            provider.Toggle();
            Settle(window);
            Assert.InRange(((RotateTransform)glyph.RenderTransform).Angle, -0.1, 0.1);
            Assert.Equal(ToggleState.Off, provider.ToggleState);
            DesktopFixture.Save(window, "expansion-toggle-collapsed.png");
        });
    }

    [Fact]
    public void 日志和测速折叠栏统一右下箭头并保留原生辅助技术状态()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "LogPanel");
            var expander = (Expander)window.FindName("LogDetailsExpander");
            expander.Header = "所选记录原文";
            DesktopFixture.Layout(window, 1040, 660);
            var peer = UIElementAutomationPeer.CreatePeerForElement(expander)!;
            var provider = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)!;
            provider.Expand();
            Settle(window);
            var glyph = Glyph(expander);
            Assert.InRange(((RotateTransform)glyph.RenderTransform).Angle, 89.9, 90.1);
            Assert.Equal(ExpandCollapseState.Expanded, provider.ExpandCollapseState);
            provider.Collapse();
            Settle(window);
            Assert.InRange(((RotateTransform)glyph.RenderTransform).Angle, -0.1, 0.1);
        });
    }

    [Theory]
    [InlineData("FailureExpander")]
    [InlineData("MethodExpander")]
    public void 测速折叠栏实际接入共用箭头状态(string name)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "ToolsPanel");
            ((TabControl)window.FindName("ToolsTabControl")).SelectedItem = window.FindName("SpeedToolsTab");
            var speed = (NetGuardian.App.Views.SpeedTestView)window.FindName("SpeedView");
            var expander = (Expander)speed.FindName(name);
            expander.Visibility = Visibility.Visible;
            DesktopFixture.Layout(window, 1040, 660);
            expander.IsExpanded = true;
            Settle(window);
            var glyph = Glyph(expander);
            Assert.InRange(((RotateTransform)glyph.RenderTransform).Angle, 89.9, 90.1);
            expander.IsExpanded = false;
            Settle(window);
            Assert.InRange(((RotateTransform)glyph.RenderTransform).Angle, -0.1, 0.1);
        });
    }
    [Fact]
    public void 展开图标初始加载和重新挂载后保持状态且旋转中心固定()
    {
        DesktopFixture.Run(window =>
        {
            var host = new StackPanel();
            var toggle = new ToggleButton { Style = (Style)window.FindResource("ExpansionToggleStyle"), IsChecked = true };
            host.Children.Add(toggle);
            ((Panel)window.Content).Children.Add(host);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            var glyph = Glyph(toggle);
            Assert.Equal(90, ((RotateTransform)glyph.RenderTransform).Angle);
            var center = glyph.TranslatePoint(new Point(glyph.ActualWidth / 2, glyph.ActualHeight / 2), toggle);
            toggle.IsChecked = false;
            toggle.IsChecked = true;
            toggle.IsChecked = false;
            Settle(window);
            var collapsedCenter = glyph.TranslatePoint(new Point(glyph.ActualWidth / 2, glyph.ActualHeight / 2), toggle);
            Assert.InRange((center - collapsedCenter).Length, 0, 0.01);
            Assert.InRange(((RotateTransform)glyph.RenderTransform).Angle, -0.1, 0.1);
            host.Children.Remove(toggle);
            toggle.IsChecked = true;
            host.Children.Add(toggle);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            Assert.Equal(90, ((RotateTransform)glyph.RenderTransform).Angle);
        });
    }

    private static FrameworkElement Glyph(DependencyObject root) => DesktopFixture.Descendants(root)
        .OfType<FrameworkElement>().Single(element => element.Name == "ExpansionGlyph");

    internal static void Settle(Window window)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        window.UpdateLayout();
    }
}
