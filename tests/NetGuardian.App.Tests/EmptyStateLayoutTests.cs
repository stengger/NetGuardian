using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class EmptyStateLayoutTests
{
    [Theory]
    [InlineData("Quality", "QualityPanel", "")]
    [InlineData("Website", "ToolsPanel", "WebsiteToolsTab")]
    [InlineData("IpInfo", "ToolsPanel", "IpInfoToolsTab")]
    [InlineData("Report", "ToolsPanel", "ReportToolsTab")]
    public void 空结果有可聚焦引导且跟随原操作禁用状态(string prefix, string page, string tab)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, page);
            if (tab.Length > 0) ((TabControl)window.FindName("ToolsTabControl")).SelectedItem = window.FindName(tab);
            var message = (TextBlock)window.FindName(prefix + "EmptyText");
            message.Text = "尚未检测。开始检查后显示结果。";
            message.Visibility = Visibility.Visible;
            var original = (Button)window.FindName(prefix == "IpInfo" ? "IpInfoRefreshButton" : prefix + "RunButton");
            original.Content = "开始检查";
            DesktopFixture.Layout(window, 1040, 660);
            var guide = Assert.IsType<Button>(window.FindName(prefix + "EmptyAction"));
            Assert.True(guide.IsVisible && guide.Focusable && guide.ActualHeight >= 32);
            original.IsEnabled = false;
            window.UpdateLayout();
            Assert.False(guide.IsEnabled);
            DesktopFixture.Save(window, $"empty-{prefix}.png");
        });
    }
}
