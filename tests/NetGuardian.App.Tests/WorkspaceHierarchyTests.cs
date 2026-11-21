using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WorkspaceHierarchyTests
{
    [Theory]
    [InlineData(1180, 760, "zh-CN")]
    [InlineData(1040, 660, "en-US")]
    [InlineData(960, 500, "en-US")]
    public void 配置页页签与选网卡同排且常用命令在页首(double width, double height, string language)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960; window.MinHeight = 500;
            var text = new Localizer();
            text.SetLanguage(language);
            UiAuditCaptureTests.PopulateLabels(window, text);
            var picker = (ComboBox)window.FindName("AdapterTargetComboBox");
            picker.Items.Add("Ethernet 2");
            picker.SelectedIndex = 0;
            foreach (var (name, key) in new[] { ("AdapterConfigTab", "AdapterOverview"), ("DnsConfigTab", "DnsTools") })
                ((TabItem)window.FindName(name)).Header = text.T(key);
            foreach (var (page, command) in new[] { ("NetworkPanel", "AdapterRefreshButton"), ("DnsPanel", "DnsApplyButton"),
                ("PolicyPanel", "SavePolicyButton"), ("QualityPanel", "QualityRunButton"), ("LogPanel", "LogBackButton") })
            {
                PanelNavigationPresenter.Show(window, page, text);
                DesktopFixture.Layout(window, width, height);
                var title = Bounds(window, "PageTitleText");
                var action = Bounds(window, command);
                Assert.True(action.Top < title.Bottom && action.Left >= title.Right, $"{page} 标题 {title}，命令 {action}");
                Assert.True(action.Right <= width && action.Left >= 0);
                if (page is "NetworkPanel" or "DnsPanel")
                {
                    var tab = Bounds(window, "DnsConfigTab");
                    var target = Bounds(window, "AdapterTargetComboBox");
                    Assert.True(target.Top < tab.Bottom && tab.Top < target.Bottom && target.Left > tab.Right);
                    Assert.Equal("Ethernet 2", picker.SelectedItem);
                }
            }
        });
    }

    [Fact]
    public void 首页状态和诊断结论优先且无线优先级操作对齐列表右侧()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "StatusPanel");
            DesktopFixture.Layout(window, 1180, 760);
            var monitor = Bounds(window, "MonitorStateLabel");
            var network = Bounds(window, "CurrentNetworkLabel");
            Assert.True(Math.Abs(network.Top - monitor.Top) <= 2 && network.Left > monitor.Left);
            DesktopFixture.ShowPage(window, "QualityPanel");
            DesktopFixture.Layout(window, 1180, 760);
            Assert.True(Bounds(window, "QualityStateText").Top < Bounds(window, "QualityScopeText").Top);
            UiAuditCaptureTests.PopulateLabels(window);
            ((PanelNavigationView)window.FindName("RootNavigation")).SelectPanel("WifiPanel");
            DesktopFixture.ShowPage(window, "WifiPanel");
            var networks = new[] { "Studio-5G", "Office Wi-Fi", "Studio Guest" };
            ((DataGrid)window.FindName("NearbyGrid")).ItemsSource = networks.Select((ssid, index) =>
                new WifiNetwork(ssid, 92 - index * 12, "WPA2-Personal", "CCMP", index == 0, true)).ToArray();
            ((DataGrid)window.FindName("ProfileGrid")).ItemsSource = networks.Take(2).Select((ssid, index) =>
                new WifiProfile { Ssid = ssid, Enabled = true, Priority = index + 1 }).ToArray();
            foreach (var (name, value) in new[] { ("CurrentWifiTitle", "当前连接"), ("CurrentWifiNameText", networks[0]),
                ("CurrentWifiDetailsText", "信号 92% · WPA2-Personal · IPv4 192.0.2.18"),
                ("CurrentWifiSelectionText", "已选择 Studio-5G"), ("WifiRecoverySummaryText", "自动恢复候选：2/2") })
                ((TextBlock)window.FindName(name)).Text = value;
            ((DataGrid)window.FindName("ProfileGrid")).SelectedIndex = 0;
            DesktopFixture.Layout(window, 1180, 760);
            DesktopFixture.Save(window, "无线管理-示例.png");
            Assert.InRange(Math.Abs(Bounds(window, "RemoveButton").Right - Bounds(window, "WifiPageActions").Right), 0, 2);
            Assert.True(Bounds(window, "ScanButton").Right < Bounds(window, "MoveUpButton").Left);
        });
    }

    [Theory]
    [InlineData(1180, 760, "zh-CN")]
    [InlineData(1040, 660, "en-US")]
    public void 恢复条件在首行且风险说明先于历史记录(double width, double height, string language)
    {
        DesktopFixture.Run(window =>
        {
            var text = new Localizer();
            text.SetLanguage(language);
            UiAuditCaptureTests.PopulateLabels(window, text);
            ((PanelNavigationView)window.FindName("RootNavigation")).SelectPanel("PolicyPanel");
            PanelNavigationPresenter.Show(window, "PolicyPanel", text);
            ((TextBlock)window.FindName("PolicyStatusText")).Text = text.T("PolicySaveHint");
            DesktopFixture.Layout(window, width, height);
            DesktopFixture.Save(window, $"自动恢复-{language}.png");
            var interval = Bounds(window, "IntervalTextBox");
            var failures = Bounds(window, "FailuresTextBox");
            var cooldown = Bounds(window, "CooldownTextBox");
            Assert.InRange(Math.Abs(interval.Top - failures.Top), 0, 1);
            Assert.InRange(Math.Abs(interval.Top - cooldown.Top), 0, 1);
            Assert.True(interval.Right < failures.Left && failures.Right < cooldown.Left);
            var risk = Bounds(window, "HighRiskTipText");
            Assert.True(risk.Bottom < height - 16 && risk.Bottom < Bounds(window, "RecentRecoveryTitle").Top);
            Assert.True(Bounds(window, "DnsProbeHostsTextBox").Left < Bounds(window, "HttpsProbeHostsTextBox").Left);
        });
    }

    private static Rect Bounds(Window window, string name)
    {
        var element = (FrameworkElement)window.FindName(name);
        return element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
    }
}
