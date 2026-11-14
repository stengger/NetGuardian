using System.Windows;
using System.Windows.Controls;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class CurrentWifiLayoutTests
{
    [Theory]
    [InlineData(960, 500, "Dark")]
    [InlineData(1040, 660, "Dark")]
    [InlineData(1180, 760, "Dark")]
    [InlineData(960, 500, "Light")]
    [InlineData(1040, 660, "Light")]
    [InlineData(1180, 760, "Light")]
    [InlineData(960, 500, "HighContrast")]
    [InlineData(1040, 660, "HighContrast")]
    [InlineData(1180, 760, "HighContrast")]
    public void 摘要连接徽标与恢复统计在整张卡片垂直居中(double width, double height, string theme)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960;
            window.MinHeight = 500;
            window.Resources.MergedDictionaries.Add(NetGuardian.App.Presentation.AppThemePolicy.CreatePalette(
                Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme)));
            DesktopFixture.ShowPage(window, "WifiPanel");
            ((TextBlock)window.FindName("CurrentWifiNameText")).Text = "Redmi_9F56_8B311 5g";
            ((TextBlock)window.FindName("CurrentWifiDetailsText")).Text = "已连接 · 信号89% · WPA2 · IPv4 192.168.31.89";
            var badge = (Border)window.FindName("CurrentWifiConnectedBadge");
            badge.Visibility = Visibility.Visible;
            var summary = (TextBlock)window.FindName("WifiRecoverySummaryText");
            summary.Text = "恢复优先级：已启用54/57";
            DesktopFixture.Layout(window, width, height);
            var card = (Border)window.FindName("CurrentWifiCard");
            var badgeBounds = badge.TransformToAncestor(card).TransformBounds(new Rect(badge.RenderSize));
            var summaryBounds = summary.TransformToAncestor(card).TransformBounds(new Rect(summary.RenderSize));
            var badgeCenter = badgeBounds.Top + badgeBounds.Height / 2;
            var summaryCenter = summaryBounds.Top + summaryBounds.Height / 2;
            Assert.InRange(Math.Abs(badgeCenter - summaryCenter), 0, 1);
            Assert.InRange(Math.Abs(badgeCenter - card.ActualHeight / 2), 0, 1);
            Assert.True(badgeBounds.Right <= summaryBounds.Left, "徽标不能覆盖恢复统计。");
            Assert.Equal(22, badge.ActualHeight);
            Assert.InRange(card.ActualHeight, 1, 64);
            DesktopFixture.Save(window, $"summary-alignment-{theme}-{width}.png");
        }, theme == "Light" ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }

    [Theory]
    [InlineData(960, 500)]
    [InlineData(1180, 760)]
    public void 当前连接独立显示且不被选择提示或列表遮挡(double width, double height)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960;
            window.MinHeight = 500;
            DesktopFixture.ShowPage(window, "WifiPanel");
            var current = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("CurrentWifiCard"));
            var ssid = Assert.IsType<TextBlock>(window.FindName("CurrentWifiNameText"));
            var details = Assert.IsType<TextBlock>(window.FindName("CurrentWifiDetailsText"));
            ((TextBlock)window.FindName("CurrentWifiTitle")).Text = "当前连接";
            ssid.Text = "当前网络 Redmi_9F56_8B311 5g";
            details.Text = "已连接 · 信号 89% · WPA2-Personal · IPv4 192.0.2.10";
            ((TextBlock)window.FindName("CurrentWifiSelectionText")).Text = "目标网络连接失败：特定的网络不可用。";
            ((DataGrid)window.FindName("NearbyGrid")).ItemsSource = new[] { new WifiNetwork("目标网络", 80, "WPA2", "AES", true, true) };
            ((DataGrid)window.FindName("ProfileGrid")).ItemsSource = new[] { new WifiProfile { Ssid = "实际连接", Priority = 1 } };
            DesktopFixture.Layout(window, width, height);
            var currentBounds = current.TransformToAncestor(window).TransformBounds(new Rect(current.RenderSize));
            var region = (FrameworkElement)window.FindName("WifiListsGrid");
            var regionBounds = region.TransformToAncestor(window).TransformBounds(new Rect(region.RenderSize));
            Assert.True(current.IsVisible && currentBounds.Height > 0 && currentBounds.Height <= 64);
            Assert.True(currentBounds.Bottom <= regionBounds.Top && currentBounds.Left >= 0 && currentBounds.Right <= width,
                $"摘要越界或覆盖列表：摘要={currentBounds}，列表={regionBounds}，窗口宽度={width}");
            Assert.Contains("Redmi", ssid.Text);
            Assert.DoesNotContain("目标网络", ssid.Text);
            var page = (ScrollViewer)window.FindName("WifiContentScrollViewer");
            foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
            {
                // 小于应用常规最小高度的工作区，通过折叠已读分区腾出阅读空间。
                if (height < 600 && name == "ProfileGrid")
                {
                    ((Expander)window.FindName("NearbyNetworksExpander")).IsExpanded = false;
                    window.UpdateLayout();
                }
                var grid = (DataGrid)window.FindName(name);
                var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.True(row.ActualHeight >= 38);
                var header = grid.HeadersVisibility.HasFlag(DataGridHeadersVisibility.Column) ? grid.ColumnHeaderHeight : 0;
                Assert.True(grid.ActualHeight >= header + row.ActualHeight,
                    $"{name} 首行空间不足：表格={grid.ActualHeight}，表头={header}，行={row.ActualHeight}");
                row.BringIntoView(); window.UpdateLayout();
                var bounds = row.TransformToAncestor(page).TransformBounds(new Rect(row.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= page.ActualHeight);
            }
            page.ScrollToTop(); window.UpdateLayout();
            DesktopFixture.Save(window, $"current-wifi-{width}.png");
        });
    }
}
