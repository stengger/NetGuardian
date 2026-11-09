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
            var glyph = DesktopFixture.Descendants(toggle).OfType<TextBlock>().Single(text => text.Name == "ExpansionGlyph");
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
            var glyph = DesktopFixture.Descendants(expander).OfType<TextBlock>().Single(text => text.Name == "ExpansionGlyph");
            Assert.InRange(((RotateTransform)glyph.RenderTransform).Angle, 89.9, 90.1);
            Assert.Equal(ExpandCollapseState.Expanded, provider.ExpandCollapseState);
            provider.Collapse();
            Settle(window);
            Assert.InRange(((RotateTransform)glyph.RenderTransform).Angle, -0.1, 0.1);
        });
    }

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
