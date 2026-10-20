using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class SmallWorkAreaTests
{
    [Theory]
    [InlineData(1180, 760, "zh-CN", false)]
    [InlineData(1180, 760, "en-US", true)]
    [InlineData(960, 500, "zh-CN", true)]
    [InlineData(960, 500, "en-US", false)]
    public void 无线操作反馈和列表归属于一致的页面内容表面(double width, double height, string language, bool light)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960; window.MinHeight = 500;
            DesktopFixture.ShowPage(window, "WifiPanel");
            ApplyWifiLabels(window, language);
            DesktopFixture.Layout(window, width, height);
            var page = (FrameworkElement)window.FindName("WifiPanel");
            var action = (FrameworkElement)window.FindName("ScanButton");
            var surfaces = new List<Border>();
            for (DependencyObject? current = VisualTreeHelper.GetParent(action); current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is Border border && border.Background is SolidColorBrush brush && brush.Color.A > 0 && border.Padding.Left >= 16)
                    surfaces.Add(border);
                if (ReferenceEquals(current, page)) break;
            }
            var surface = Assert.Single(surfaces);
            var reference = (Border)window.FindName("ToolsPanel");
            Assert.Equal(((SolidColorBrush)reference.Background).Color, ((SolidColorBrush)surface.Background).Color);
            Assert.Equal(reference.Padding, surface.Padding);
            foreach (var name in new[] { "ScanButton", "ConnectButton", "WifiSelectionBar", "NearbyNetworksCard", "PriorityNetworksCard" })
            {
                var child = (FrameworkElement)window.FindName(name);
                var bounds = child.TransformToAncestor(surface).TransformBounds(new Rect(child.RenderSize));
                Assert.True(bounds.Left >= surface.Padding.Left && bounds.Right <= surface.ActualWidth - surface.Padding.Right + 1
                    && bounds.Top >= surface.Padding.Top && bounds.Bottom <= surface.ActualHeight - surface.Padding.Bottom + 1,
                    $"{name} 没有完整归属内容表面：{bounds}；表面={surface.RenderSize}");
            }
            DesktopFixture.Save(window, $"wifi-page-surface-{width}-{language}-{light}.png");
        }, light ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 侧栏展开和收起时只显示对应流量样式(bool expanded)
    {
        DesktopFixture.Run(window =>
        {
            ((NetGuardian.App.Presentation.PanelNavigationView)window.FindName("RootNavigation")).IsPaneOpen = expanded;
            window.UpdateLayout();
            Assert.Equal(expanded ? Visibility.Visible : Visibility.Collapsed, ((FrameworkElement)window.FindName("SidebarSpeedCard")).Visibility);
            Assert.Equal(expanded ? Visibility.Collapsed : Visibility.Visible, ((FrameworkElement)window.FindName("SidebarSpeedCompact")).Visibility);
        });
    }

    [Fact]
    public void 短窗口的流量区不覆盖滚动后的导航项()
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960; window.MinHeight = 500;
            ((TextBlock)window.FindName("SidebarSpeedTitle")).Text = "本机接口流量合计";
            ((TextBlock)window.FindName("SidebarSpeedScopeText")).Text = "活动接口合计，非互联网测速";
            DesktopFixture.Layout(window, 960, 500);
            var item = (FrameworkElement)window.FindName("LogNavButton");
            item.BringIntoView(); window.UpdateLayout();
            var card = (FrameworkElement)window.FindName("SidebarSpeedCard");
            var itemBounds = item.TransformToAncestor(window).TransformBounds(new Rect(item.RenderSize));
            var cardBounds = card.TransformToAncestor(window).TransformBounds(new Rect(card.RenderSize));
            Assert.False(itemBounds.IntersectsWith(cardBounds), $"导航 {itemBounds} 与流量 {cardBounds} 重叠");
            Assert.True(itemBounds.Top >= 32 && itemBounds.Bottom <= 500);
            var navigation = (FrameworkElement)window.FindName("RootNavigation");
            var peer = UIElementAutomationPeer.CreatePeerForElement(navigation)!;
            Assert.Single(peer.GetChildren()!, child => child.GetAutomationControlType() == AutomationControlType.Group
                && child.GetName() == "本机接口流量合计");
            var children = peer.GetChildren()!;
            Assert.True(children.FindIndex(child => child.GetName() == "本机接口流量合计")
                < children.FindIndex(child => child.GetName() == "设置"));
            DesktopFixture.Save(window, "small-workarea-sidebar.png");
        });
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 短窗口无线列表至少可以显示一个完整结果行(string language)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960; window.MinHeight = 500;
            DesktopFixture.ShowPage(window, "WifiPanel");
            ApplyWifiLabels(window, language);
            ((DataGrid)window.FindName("NearbyGrid")).ItemsSource = new[] { new WifiNetwork("完整的长网络名称-5GHz", 85, "WPA2-PSK", "AES", true, true) };
            ((DataGrid)window.FindName("ProfileGrid")).ItemsSource = new[] { new WifiProfile { Ssid = "完整的长网络名称-5GHz", Priority = 1, Enabled = true } };
            DesktopFixture.Layout(window, 960, 500);
            foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
            {
                Assert.True(((DataGrid)window.FindName(name)).ActualHeight >= 90, $"{name} 只剩表头");
                var row = (DataGridRow)((DataGrid)window.FindName(name)).ItemContainerGenerator.ContainerFromIndex(0);
                Assert.True(row.ActualHeight >= 38);
                var grid = (DataGrid)window.FindName(name);
                var scroll = DesktopFixture.Descendants(grid).OfType<ScrollViewer>().Single(viewer => viewer.Name == "DG_ScrollViewer");
                var viewport = DesktopFixture.Descendants(scroll).OfType<ScrollContentPresenter>().First();
                var rowBounds = row.TransformToAncestor(viewport).TransformBounds(new Rect(row.RenderSize));
                Assert.True(rowBounds.Top >= 0 && rowBounds.Bottom <= viewport.ActualHeight,
                    $"{name} 第一行未完整处于结果视口：{rowBounds}；视口高={viewport.ActualHeight}");
                scroll.ScrollToRightEnd(); window.UpdateLayout();
                var lastCell = grid.Columns[^1].GetCellContent(row);
                var cellBounds = lastCell.TransformToAncestor(grid).TransformBounds(new Rect(lastCell.RenderSize));
                Assert.True(scroll.HorizontalOffset > 0 && cellBounds.Left >= 0 && cellBounds.Right <= grid.ActualWidth,
                    $"{name} 最后一列无法通过横向滚动读取：{cellBounds}；表宽={grid.ActualWidth}，偏移={scroll.HorizontalOffset}，可滚宽={scroll.ScrollableWidth}，视口={scroll.ViewportWidth}");
                scroll.ScrollToLeftEnd(); window.UpdateLayout();
            }
            DesktopFixture.Save(window, $"small-workarea-wifi-with-data-{language}.png");
        });
    }

    private static void ApplyWifiLabels(Window window, string language)
    {
        var localizer = new Localizer(); localizer.SetLanguage(language);
        foreach (var name in new[] { "ScanButton", "AddSelectedButton", "SetPasswordButton", "ImportPasswordButton", "AddManualButton", "MoveUpButton", "MoveDownButton", "RemoveButton", "ConnectButton" })
            ((Button)window.FindName(name)).Content = localizer.T(name switch
            {
                "ScanButton" => "Scan", "AddSelectedButton" => "AddSelected", "SetPasswordButton" => "SetPassword",
                "ImportPasswordButton" => "ImportPassword", "AddManualButton" => "AddManual", "MoveUpButton" => "MoveUp",
                "MoveDownButton" => "MoveDown", "RemoveButton" => "Remove", _ => "Connect"
            });
        foreach (var (name, key) in new[] { ("PageTitleText", "Wifi"), ("NearbyNetworksTitle", "NearbyNetworks"),
            ("PriorityListTitle", "PriorityList"), ("CurrentWifiSelectionText", "SelectWifiFirst"),
            ("DragHintText", "DoubleClickWifiHint"), ("WifiScanStatusText", "NoNearbyWifiRows") })
            ((TextBlock)window.FindName(name)).Text = localizer.T(key);
        foreach (var (name, key) in new[] { ("NearbySsidColumn", "Ssid"), ("NearbySignalColumn", "Signal"),
            ("NearbySecurityColumn", "Security"), ("NearbyPriorityColumn", "PriorityState"), ("ProfileSsidColumn", "Ssid"),
            ("ProfilePriorityColumn", "Priority"), ("ProfileCredentialColumn", "CredentialSaved"),
            ("ProfileEnabledColumn", "Enabled"), ("ProfileFailuresColumn", "Failures") })
            ((DataGridColumn)window.FindName(name)).Header = localizer.T(key);
    }

    [Fact]
    public void 导航滚动的异步状态在所属调度线程完成()
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960; window.MinHeight = 500;
            ((TextBlock)window.FindName("SidebarSpeedTitle")).Text = "本机接口流量合计";
            ((TextBlock)window.FindName("SidebarSpeedScopeText")).Text = "活动接口合计，非互联网测速";
            DesktopFixture.Layout(window, 960, 500);
            var navigation = (FrameworkElement)window.FindName("RootNavigation");
            var scroll = DesktopFixture.Descendants(navigation).OfType<Wpf.Ui.Controls.DynamicScrollViewer>()
                .First(viewer => viewer.ScrollableHeight > 40);
            scroll.ScrollToEnd(); window.UpdateLayout();
            Assert.True(scroll.IsScrollingVertically);
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(scroll.Timeout + 200) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Assert.False(scroll.IsScrollingVertically);
        });
    }

    [Theory]
    [InlineData("PolicyPanel", "SavePolicyButton")]
    [InlineData("WifiPanel", "ScanButton")]
    [InlineData("DnsPanel", "DnsApplyButton")]
    [InlineData("NetworkPanel", "AdapterRenewDhcpButton")]
    public void 小工作区内容诊断(string page, string actionName)
    {
        DesktopFixture.Run(window =>
        {
            // 仅隔离内容布局；生产窗口最小尺寸尚未适配此工作区。
            window.MinWidth = 960; window.MinHeight = 500;
            DesktopFixture.ShowPage(window, page);
            DesktopFixture.Layout(window, 960, 500);
            foreach (var name in new[] { "CheckNowButton", "StartMonitorButton", "StopButton", actionName })
            {
                var action = (FrameworkElement)window.FindName(name);
                action.BringIntoView(); window.UpdateLayout();
                var bounds = action.TransformToAncestor(window).TransformBounds(new Rect(action.RenderSize));
                Assert.True(bounds.Left >= 0 && bounds.Right <= 960 && bounds.Top >= 0 && bounds.Bottom <= 500,
                    $"{page}/{name} 超出工作区：{bounds}");
                Assert.True(action.ActualHeight >= 28);
            }
            DesktopFixture.Save(window, $"small-workarea-{page}.png");
        });
    }
}
