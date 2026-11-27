using System.Windows;
using System.Windows.Controls;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WifiWorkspaceLayoutTests
{
    [Theory]
    [InlineData(1180, 758, 6)]
    [InlineData(1040, 660, 1)]
    public void 默认双栏与最小窗口上下排列均保留有限视口(double width, double height, int minimum)
    {
        DesktopFixture.Run(window =>
        {
            Populate(window);
            DesktopFixture.Layout(window, width, height);
            DesktopFixture.Save(window, $"workspace-{width}.png");
            foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
            {
                var table = (DataGrid)window.FindName(name);
                var row = DesktopFixture.Descendants(table).OfType<DataGridRow>().First();
                var cell = DesktopFixture.Descendants(row).OfType<DataGridCell>().First();
                Assert.True(CompleteRows(table) >= minimum, $"{name} 完整行数={CompleteRows(table)}，行高={row.ActualHeight}，单元格={cell.ActualHeight}，表高={table.ActualHeight}");
                Assert.Equal(0, WifiSectionLayoutTests.Scroll(table).ScrollableWidth);
            }
            Assert.InRange(((FrameworkElement)window.FindName("CurrentWifiCard")).ActualHeight, 1, 64);
            Assert.Equal(0, ((ScrollViewer)window.FindName("WifiContentScrollViewer")).ScrollableHeight);
            DesktopFixture.Save(window, $"workspace-{width}.png");
        });
    }

    [Theory]
    [InlineData(1178, false)]
    [InlineData(1179, true)]
    [InlineData(1180, true)]
    public void 窗口宽度断点切换且没有横向溢出(double width, bool horizontal)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 800;
            Populate(window);
            DesktopFixture.Layout(window, width, 758);
            var nearby = (FrameworkElement)window.FindName("NearbyNetworksCard");
            var priority = (FrameworkElement)window.FindName("PriorityNetworksCard");
            var region = (FrameworkElement)window.FindName("WifiListsGrid");
            var a = nearby.TransformToAncestor(region).TransformBounds(new Rect(nearby.RenderSize));
            var b = priority.TransformToAncestor(region).TransformBounds(new Rect(priority.RenderSize));
            Assert.Equal(horizontal, b.Left > a.Left + 1);
            Assert.True(a.Right <= region.ActualWidth + 1 && b.Right <= region.ActualWidth + 1);
            Assert.InRange(horizontal ? b.Left - a.Right : b.Top - a.Bottom, 16, 24);
            foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
                Assert.Equal(0, WifiSectionLayoutTests.Scroll((DataGrid)window.FindName(name)).ScrollableWidth);
            DesktopFixture.Save(window, $"workspace-breakpoint-{width}.png");
        });
    }

    internal static void Populate(Window window)
    {
        DesktopFixture.ShowPage(window, "WifiPanel");
        ((TextBlock)window.FindName("CurrentWifiNameText")).Text = "实验室-长SSID-012345678901234567890123456789";
        ((TextBlock)window.FindName("CurrentWifiDetailsText")).Text = "已连接 · 信号 89% · WPA2-Personal · IPv4 192.0.2.10";
        ((DataGrid)window.FindName("NearbyGrid")).ItemsSource = Enumerable.Range(1, 60)
            .Select(i => new { Ssid = $"实验室网络-{i}-长名称012345678901234567890", SignalPercent = 80, Authentication = "WPA2-Personal", PriorityBadge = "可添加" }).ToArray();
        ((DataGrid)window.FindName("ProfileGrid")).ItemsSource = Enumerable.Range(1, 60)
            .Select(i => new WifiProfile { Ssid = $"恢复网络-{i}-长名称012345678901234567890", Priority = i, Enabled = true }).ToArray();
    }

    [Fact]
    public void 两种方向比例独立且重载折叠状态不写业务配置()
    {
        var saved = new NetGuardian.App.Presentation.WifiLayoutPreferences(.6, .35, true, true);
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 800;
            Populate(window);
            NetGuardian.App.Presentation.WifiWorkspaceLayout.Restore(window, saved);
            DesktopFixture.Layout(window, 1500, 900);
            var region = (Grid)window.FindName("WifiListsGrid");
            Assert.Equal(.6, region.ColumnDefinitions[0].Width.Value, 2);
            DesktopFixture.Layout(window, 959, 900);
            Assert.Equal(.35, region.RowDefinitions[0].Height.Value, 2);
            ((Expander)window.FindName("NearbyNetworksExpander")).IsExpanded = false;
            window.UpdateLayout();
            saved = NetGuardian.App.Presentation.WifiWorkspaceLayout.Snapshot(window);
            Assert.False(saved.NearbyExpanded);
            Assert.True(((DataGrid)window.FindName("ProfileGrid")).ActualHeight > 400);
        });
        DesktopFixture.Run(window =>
        {
            Populate(window);
            NetGuardian.App.Presentation.WifiWorkspaceLayout.Restore(window, saved);
            DesktopFixture.Layout(window, 1180, 758);
            Assert.False(((DataGrid)window.FindName("NearbyGrid")).IsVisible);
            Assert.True(((DataGrid)window.FindName("ProfileGrid")).IsVisible);
        });
        Assert.Equal(.5, NetGuardian.App.Presentation.WifiLayoutPreferences.Normalize(double.NaN));
        Assert.Equal(.25, NetGuardian.App.Presentation.WifiLayoutPreferences.Normalize(-100));
        Assert.Equal(.75, NetGuardian.App.Presentation.WifiLayoutPreferences.Normalize(100));
    }

    [Fact]
    public void 更多与过滤菜单绑定原入口和状态()
    {
        DesktopFixture.Run(window =>
        {
            Populate(window); DesktopFixture.Layout(window, 1180, 758);
            var button = (Button)window.FindName("WifiMoreButton");
            var menu = button.ContextMenu;
            menu.PlacementTarget = button; menu.IsOpen = true; window.UpdateLayout();
            var source = (Button)window.FindName("SetPasswordButton");
            var item = (MenuItem)menu.Items[0];
            Assert.Equal(source.Content, item.Header);
            source.IsEnabled = false; window.UpdateLayout(); Assert.False(item.IsEnabled);
            source.IsEnabled = true; window.UpdateLayout(); Assert.True(item.IsEnabled);
            Assert.Equal(5, menu.Items.OfType<MenuItem>().Count());
            var showAll = menu.Items.OfType<MenuItem>().Last();
            Assert.True(showAll.IsCheckable);
            Assert.False(showAll.IsChecked);
            Assert.False(string.IsNullOrWhiteSpace(showAll.Header?.ToString()));
            menu.IsOpen = false;
            var filter = (Button)window.FindName("NearbyFilterButton");
            filter.ContextMenu.PlacementTarget = filter; filter.ContextMenu.IsOpen = true; window.UpdateLayout();
            ((MenuItem)filter.ContextMenu.Items[0]).IsChecked = true; window.UpdateLayout();
            Assert.True(((CheckBox)window.FindName("NearbyOnlyNewCheckBox")).IsChecked);
            filter.ContextMenu.IsOpen = false;
        });
    }

    [Theory]
    [InlineData(800, 660)]
    [InlineData(1040, 660)]
    [InlineData(1180, 758)]
    public void 空态原文和操作在有限视口中完整可达(double width, double height)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 800;
            DesktopFixture.ShowPage(window, "WifiPanel");
            var localizer = new NetGuardian.Core.Localization.Localizer();
            foreach (var (name, key) in new[] { ("NearbyEmptyText", "WifiScanNone"), ("ProfileEmptyText", "NoPriorityWifiRows") })
            {
                var text = (TextBlock)window.FindName(name); text.Text = localizer.T(key); text.Visibility = Visibility.Visible;
            }
            DesktopFixture.Layout(window, width, height);
            foreach (var name in new[] { "NearbyEmptyAction", "ProfileEmptyAction" })
            {
                var action = (Button)window.FindName(name);
                var bounds = action.TransformToAncestor(window).TransformBounds(new Rect(action.RenderSize));
                Assert.Equal(32, action.ActualHeight);
                Assert.True(bounds.Left >= 0 && bounds.Right <= width && bounds.Bottom <= height - 16);
                var content = (Grid)((Border)((StackPanel)action.Parent).Parent).Parent;
                var local = action.TransformToAncestor(content).TransformBounds(new Rect(action.RenderSize));
                Assert.True(local.Top >= 0 && local.Bottom <= content.ActualHeight + 1, $"{name} 被空态裁切：{local} / {content.RenderSize}");
            }
            DesktopFixture.Save(window, $"workspace-empty-{width}.png");
        });
    }

    [Fact]
    public void 折叠再展开后名称列重新占满剩余宽度()
    {
        DesktopFixture.Run(window =>
        {
            Populate(window); DesktopFixture.Layout(window, 1180, 758);
            var table = (DataGrid)window.FindName("ProfileGrid");
            var initial = table.Columns[1].ActualWidth;
            var section = (Expander)window.FindName("NearbyNetworksExpander");
            section.IsExpanded = false; DesktopFixture.Layout(window, 1180, 758);
            section.IsExpanded = true; DesktopFixture.Layout(window, 1180, 758);
            Assert.True(table.Columns[1].ActualWidth >= initial - 1, $"名称列从 {initial} 缩至 {table.Columns[1].ActualWidth}");
            table.Columns[1].Width = 190;
            DesktopFixture.Layout(window, 1400, 758);
            Assert.Equal(190, table.Columns[1].ActualWidth, 1);
            Assert.Equal(0, WifiSectionLayoutTests.Scroll(table).ScrollableWidth);
        });
    }

    internal static int CompleteRows(DataGrid table)
    {
        var viewport = DesktopFixture.Descendants(table).OfType<ScrollContentPresenter>().Single(p => p.Name == "PART_ScrollContentPresenter");
        return DesktopFixture.Descendants(table).OfType<DataGridRow>().Count(row =>
        {
            var bounds = row.TransformToAncestor(viewport).TransformBounds(new Rect(row.RenderSize));
            return bounds.Top >= -.5 && bounds.Bottom <= viewport.ActualHeight + .5;
        });
    }
}
