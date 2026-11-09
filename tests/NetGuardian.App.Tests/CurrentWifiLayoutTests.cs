using System.Windows;
using System.Windows.Controls;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class CurrentWifiLayoutTests
{
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
            var selection = (FrameworkElement)window.FindName("WifiSelectionBar");
            var selectionBounds = selection.TransformToAncestor(window).TransformBounds(new Rect(selection.RenderSize));
            Assert.True(current.IsVisible && currentBounds.Height > 0);
            Assert.True(currentBounds.Bottom <= selectionBounds.Top && currentBounds.Left >= 0 && currentBounds.Right <= width,
                $"当前连接卡片越界或覆盖提示：卡片={currentBounds}，提示={selectionBounds}，窗口宽度={width}");
            Assert.Contains("Redmi", ssid.Text);
            Assert.DoesNotContain("目标网络", ssid.Text);
            Assert.True(((DataGrid)window.FindName("NearbyGrid")).ActualHeight >= 90);
            Assert.True(((DataGrid)window.FindName("ProfileGrid")).ActualHeight >= 90);
            DesktopFixture.Save(window, $"current-wifi-{width}.png");
        });
    }
}
