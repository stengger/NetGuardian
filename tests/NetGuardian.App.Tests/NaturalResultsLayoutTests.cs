using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class NaturalResultsLayoutTests
{
    [Theory]
    [InlineData(960, 500)]
    [InlineData(1040, 660)]
    [InlineData(1600, 900)]
    public void 无线两表完整测量六行和展开详情且只有页面纵向滚动(double width, double height)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960; window.MinHeight = 500;
            DesktopFixture.ShowPage(window, "WifiPanel");
            var nearby = (DataGrid)window.FindName("NearbyGrid");
            var profiles = (DataGrid)window.FindName("ProfileGrid");
            nearby.ItemsSource = Enumerable.Range(1, 6).Select(i => new { Ssid = $"实验室 含空格长网络名称-5GHz-{i}", SignalPercent = 85 - i, Authentication = "WPA2-Personal", PriorityBadge = "已加入" }).ToArray();
            profiles.ItemsSource = Enumerable.Range(1, 6).Select(i => new WifiProfile { Ssid = $"实验室 含空格长网络名称-5GHz-{i}", Priority = i, Enabled = true }).ToArray();
            DesktopFixture.Layout(window, width, height);
            var first = (DataGridRow)nearby.ItemContainerGenerator.ContainerFromIndex(0);
            DesktopFixture.Descendants(first).OfType<ToggleButton>().First().IsChecked = true;
            ExpansionInteractionTests.Settle(window);
            DesktopFixture.Save(window, $"wifi-six-expanded-{width}.png");
            var page = Assert.IsType<ScrollViewer>(window.FindName("WifiContentScrollViewer"));
            foreach (var grid in new[] { nearby, profiles })
            {
                Assert.True(grid.ActualHeight >= 6 * 38 + 38);
                var inner = DesktopFixture.Descendants(grid).OfType<ScrollViewer>().Single(viewer => viewer.Name == "DG_ScrollViewer");
                Assert.Equal(0, inner.ScrollableHeight);
                Assert.Equal(0, inner.ScrollableWidth);
                var last = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(5));
                last.BringIntoView(); window.UpdateLayout();
                var bounds = last.TransformToAncestor(page).TransformBounds(new Rect(last.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= page.ActualHeight, $"第六行不可滚入页面：{bounds}");
            }
            page.ScrollToTop(); window.UpdateLayout();
            Assert.True(first.ActualHeight > 100);
            var tooltip = nearby.Columns[0].GetCellContent(first).ToolTip?.ToString();
            Assert.Contains("含空格", tooltip);
            Assert.Equal(0, page.ScrollableWidth);
            DesktopFixture.Save(window, $"wifi-six-expanded-{width}.png");
            page.ScrollToBottom(); window.UpdateLayout();
            DesktopFixture.Save(window, $"wifi-six-bottom-{width}.png");
        });
    }

    [Theory]
    [InlineData("AdapterGrid", "NetworkPanel", "", 960)]
    [InlineData("AdapterGrid", "NetworkPanel", "", 1100)]
    [InlineData("WebsiteGrid", "ToolsPanel", "WebsiteToolsTab", 960)]
    [InlineData("WebsiteGrid", "ToolsPanel", "WebsiteToolsTab", 1160)]
    [InlineData("WebsiteGrid", "ToolsPanel", "WebsiteToolsTab", 1240)]
    public void 表格按真实列宽预算切换而非统一阈值(string name, string page, string tab, double width)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, page);
            if (tab.Length > 0) ((TabControl)window.FindName("ToolsTabControl")).SelectedItem = window.FindName(tab);
            var grid = (DataGrid)window.FindName(name);
            grid.Width = width; grid.HorizontalAlignment = HorizontalAlignment.Left;
            grid.ItemsSource = new[] { new { Name = "探测接口", Type = "无线", Status = "已连接", Ipv4 = "192.0.2.1", Gateway = "192.0.2.254", DnsServers = "223.5.5.5", TimeText = "2026-11-09", HttpStatus = "200", DnsLatency = "10ms", HttpLatency = "20ms", Address = "192.0.2.1", Detail = "正常" } };
            DesktopFixture.Layout(window, 1800, 900);
            var scroll = DesktopFixture.Descendants(grid).OfType<ScrollViewer>().Single(viewer => viewer.Name == "DG_ScrollViewer");
            Assert.Equal(0, scroll.ScrollableWidth);
            if (width >= (name == "WebsiteGrid" ? 1240 : 1100))
                Assert.All(grid.Columns, column => Assert.Equal(Visibility.Visible, column.Visibility));
        });
    }
}
