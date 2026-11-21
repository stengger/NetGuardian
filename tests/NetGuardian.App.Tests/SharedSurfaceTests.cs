using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using Xunit;
namespace NetGuardian.App.Tests;
[Collection("WPF 桌面")]
public sealed class SharedSurfaceTests
{
    [Fact]
    public void 共用卡片描边动态跟随设计令牌()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            window.Resources["StrokeControlThickness"] = new Thickness(2);
            window.UpdateLayout();
            Assert.Equal(new Thickness(2), ((Border)window.FindName("WifiPanel")).BorderThickness);
        });
    }

    [Fact]
    public void 测速和设置分组复用共用资源()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "ToolsPanel");
            ((TabControl)window.FindName("ToolsTabControl")).SelectedItem = window.FindName("SpeedToolsTab");
            DesktopFixture.Layout(window, 1180, 758);
            var speed = (FrameworkElement)window.FindName("SpeedView");
            Assert.Same(window.FindResource("SectionBorderStyle"), ((Border)speed.FindName("DownloadCard")).Style);
            foreach (var name in new[] { "AppearanceSettingsCard", "StartupSettingsCard", "UpdateCard" })
                Assert.Same(window.FindResource("SectionBorderStyle"), ((Border)window.FindName(name)).Style);
        });
    }

    [Theory]
    [InlineData(1040, 660)]
    [InlineData(1180, 758)]
    public void 正常窗口双列表至少呈现两条网络(double width, double height)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            ((DataGrid)window.FindName("NearbyGrid")).ItemsSource = Enumerable.Range(0, 36)
                .Select(i => new WifiNetwork("审查周围网络" + i, 80, "WPA2-Personal", "CCMP", false, true)).ToArray();
            ((DataGrid)window.FindName("ProfileGrid")).ItemsSource = Enumerable.Range(0, 36)
                .Select(i => new WifiProfile { Ssid = "审查优先网络" + i, Enabled = true }).ToArray();
            ((TextBlock)window.FindName("CurrentWifiDetailsText")).Text = new string('长', 300);
            ((TextBlock)window.FindName("CurrentWifiSelectionText")).Text = new string('长', 300);
            DesktopFixture.Layout(window, width, height);
            foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
            {
                var table = (DataGrid)window.FindName(name);
                Assert.True(table.ActualHeight >= table.ColumnHeaderHeight + 80, $"{name} 只剩 {table.ActualHeight} DIP");
            }
            DesktopFixture.Save(window, $"modern-wifi-two-rows-{width}.png");
        });
    }
}
