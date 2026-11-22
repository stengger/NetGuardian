using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WorkspaceDesignTests
{
    [Fact]
    public void 首页三个指标均能从键盘打开诊断且事件入口保留恢复范围()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "StatusPanel");
            foreach (var metric in new[] { "Gateway", "Dns", "Https" })
            {
                var link = Assert.IsType<Button>(window.FindName($"Home{metric}Link"));
                Assert.True(link.IsVisible && link.Focusable);
                Assert.Equal(metric, link.Tag);
            }
            var source = (ComboBox)window.FindName("LogSourceComboBox");
            EventLogPresentation.ResetFilters(window, recoveryOnly: true);
            Assert.False(source.IsEnabled);
            Assert.Equal("Recovery", ((ComboBoxItem)source.SelectedItem).Tag);
            EventLogPresentation.ResetFilters(window);
            Assert.True(source.IsEnabled);
            Assert.Equal("all", ((ComboBoxItem)source.SelectedItem).Tag);
        });
    }

    [Fact]
    public void 常用任务可从侧栏直接打开且设置独立()
    {
        DesktopFixture.Run(window =>
        {
            var nav = (PanelNavigationView)window.FindName("RootNavigation");
            Assert.Equal(new[] { "StatusPanel", "IncidentPanel", "WifiPanel", "PolicyPanel", "ToolsPanel" },
                nav.MenuItems.OfType<PanelNavigationItem>().Select(item => item.Tag));
            Assert.Equal("SettingsPanel", Assert.Single(nav.FooterMenuItems.OfType<PanelNavigationItem>()).Tag);
        });
    }

    [Fact]
    public void 网络页仅切换无线与网卡配置且故障页有原始日志入口()
    {
        DesktopFixture.Run(window =>
        {
            var picker = (ComboBox)window.FindName("NetworkSectionPicker");
            var sections = picker.Items.Cast<ComboBoxItem>().Select(item => (string)item.Tag).ToArray();
            Assert.Equal(new[] { "WifiPanel", "ConfigurationWorkspace" }, sections);
            foreach (var section in sections)
            {
                DesktopFixture.ShowPage(window, section);
                Assert.True(picker.Focusable && picker.IsVisible);
                Assert.Equal(section, picker.SelectedValue);
                Assert.True(((FrameworkElement)window.FindName(section)).IsVisible);
            }
            foreach (var page in new[] { "PolicyPanel", "ToolsPanel", "IncidentPanel" })
            {
                DesktopFixture.ShowPage(window, page);
                Assert.False(picker.IsVisible);
            }
            Assert.True(((Button)window.FindName("IncidentRawLogButton")).IsVisible);
        });
    }

    [Fact]
    public void 窄窗和放大字号使概览指标自然换行()
    {
        DesktopFixture.Run(_ =>
        {
            var grid = new DashboardMetricGrid();
            for (int i = 0; i < 3; i++) grid.Children.Add(new Button());
            grid.SetValue(Control.FontSizeProperty, 20d);
            foreach (var (width, columns) in new[] { (800, 3), (500, 2), (300, 1) })
            {
                grid.Measure(new Size(width, double.PositiveInfinity));
                Assert.Equal(columns, grid.Columns);
            }
        });
    }

    [Fact]
    public void 连通性是默认诊断并可从测速返回相应工具栏()
    {
        DesktopFixture.Run(window =>
        {
            var tabs = (TabControl)window.FindName("ToolsTabControl");
            DesktopFixture.ShowPage(window, "ToolsPanel");
            Assert.Equal("QualityToolsTab", ((TabItem)tabs.SelectedItem).Name);
            Assert.True(((Button)window.FindName("QualityRunButton")).IsVisible);
            tabs.SelectedItem = window.FindName("SpeedToolsTab");
            DesktopFixture.ShowPage(window, "ToolsPanel");
            Assert.True(((FrameworkElement)window.FindName("SpeedPageActions")).IsVisible);
            DesktopFixture.ShowPage(window, "QualityPanel");
            Assert.Equal("QualityToolsTab", ((TabItem)tabs.SelectedItem).Name);
            Assert.True(((FrameworkElement)window.FindName("QualityPageActions")).IsVisible);
        });
    }

    [Fact]
    public void 切换接口概览与域名解析始终使用同一目标控件()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "NetworkPanel");
            var picker = Assert.IsType<ComboBox>(window.FindName("AdapterTargetComboBox"));
            picker.Items.Add("WLAN 2"); picker.SelectedIndex = 0;
            DesktopFixture.ShowPage(window, "DnsPanel");
            Assert.True(picker.IsVisible);
            Assert.Equal("WLAN 2", picker.SelectedItem);
            Assert.Null(window.FindName("DnsAdapterComboBox"));
            Assert.True(((FrameworkElement)window.FindName("DnsPageActions")).IsVisible);
        });
    }
}
