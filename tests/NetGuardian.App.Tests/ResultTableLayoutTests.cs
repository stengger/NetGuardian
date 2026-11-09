using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class ResultTableLayoutTests
{
    [Theory]
    [InlineData(960, 500)]
    [InlineData(1180, 760)]
    public void 窄窗口适配器表无需横向滚动且可展开复制完整地址(double width, double height)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960; window.MinHeight = 500;
            DesktopFixture.ShowPage(window, "NetworkPanel");
            var grid = (DataGrid)window.FindName("AdapterGrid");
            grid.ItemsSource = new[] { new { Name = "完整的实验室网络接口名称", Type = "无线网络", Status = "链路已连接", Ipv4 = "192.0.2.10", Gateway = "192.0.2.1", DnsServers = "223.5.5.5, 2001:4860:4860::8888" } };
            DesktopFixture.Layout(window, width, height);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var toggle = DesktopFixture.Descendants(row).OfType<ToggleButton>().FirstOrDefault();
            Assert.NotNull(toggle);
            toggle.IsChecked = true;
            ExpansionInteractionTests.Settle(window);
            var values = DesktopFixture.Descendants(row).OfType<TextBox>().ToArray();
            var details = values.Single(value => value.Text.Contains("2001:4860:4860::8888"));
            Assert.Contains("2001:4860:4860::8888", details.Text);
            Assert.Contains(values, value => value.Text == "192.0.2.1");
            Assert.True(details.IsReadOnly && details.IsVisible && details.Focusable);
            var scroll = DesktopFixture.Descendants(grid).OfType<ScrollViewer>().Single(viewer => viewer.Name == "DG_ScrollViewer");
            Assert.Equal(0, scroll.ScrollableWidth);
            Assert.NotNull(grid.ContextMenu);
            DesktopFixture.Save(window, $"adapter-details-{width}.png");
        });
    }
}
