using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NetGuardian.App.Presentation;
using NetGuardian.App.Views;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class RemainingResultsLayoutTests
{
    [Theory]
    [InlineData("QualityPanel", "QualityProbeGrid", "QualityBodyScrollViewer")]
    [InlineData("StatusPanel", "ProbeGrid", "StatusPanel")]
    public void 页面结果按内容测量且长列表没有内部纵向滚动(string page, string name, string scrollName)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, page);
            var grid = (DataGrid)window.FindName(name);
            grid.ItemsSource = Enumerable.Range(1, 12).Select(i => new { Name = $"检查-{i}", ResultText = "通过", LatencyText = "10ms", Detail = "完整结果" }).ToArray();
            ((ListBox)window.FindName("RecentEventsList")).ItemsSource = Enumerable.Range(1, 20).Select(i => $"最近事件-{i}").ToArray();
            DesktopFixture.Layout(window, 1040, 660);
            DesktopFixture.Save(window, $"natural-{name}.png");
            var outer = (ScrollViewer)window.FindName(scrollName);
            var viewport = outer.TransformToAncestor(window).TransformBounds(new Rect(outer.RenderSize));
            Assert.True(viewport.Top >= 0 && viewport.Bottom <= window.ActualHeight, $"页面滚动视口超出窗口：{viewport}");
            var inner = DesktopFixture.Descendants(grid).OfType<ScrollViewer>().Single(viewer => viewer.Name == "DG_ScrollViewer");
            Assert.Equal(0, inner.ScrollableHeight);
            Assert.True(grid.ActualHeight >= 12 * 38);
            var last = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(11);
            last.BringIntoView(); window.UpdateLayout();
            var bounds = last.TransformToAncestor(outer).TransformBounds(new Rect(last.RenderSize));
            Assert.True(bounds.Top >= 0 && bounds.Bottom <= outer.ActualHeight);
            if (page == "StatusPanel")
            {
                var events = (ListBox)window.FindName("RecentEventsList");
                Assert.All(DesktopFixture.Descendants(events).OfType<ScrollViewer>(), viewer => Assert.Equal(0, viewer.ScrollableHeight));
                Assert.True(events.ActualHeight > 240);
            }
        });
    }

    [Fact]
    public void DNS和测速长文本由所属页面滚动而非自身截高()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "DnsPanel");
            var dns = (TextBlock)window.FindName("DnsCurrentText");
            dns.Text = string.Join("\n", Enumerable.Repeat("2001:4860:4860::8888", 16));
            DesktopFixture.Layout(window, 1040, 660);
            DesktopFixture.Save(window, "dns-long-addresses.png");
            Assert.True(dns.ActualHeight > 48);
            Assert.InRange(dns.DesiredSize.Height - dns.Margin.Top - dns.Margin.Bottom - dns.ActualHeight, -1, 1);
            DesktopFixture.ShowPage(window, "ToolsPanel");
            ((TabControl)window.FindName("ToolsTabControl")).SelectedItem = window.FindName("SpeedToolsTab");
            var speed = (SpeedTestView)window.FindName("SpeedView");
            var failure = (Expander)speed.FindName("FailureExpander");
            failure.Visibility = Visibility.Visible; failure.IsExpanded = true;
            var text = (TextBox)speed.FindName("FailureText");
            text.Text = string.Join("\n", Enumerable.Repeat("完整测速错误原文", 16));
            DesktopFixture.Layout(window, 1040, 660);
            DesktopFixture.Save(window, "speed-long-error.png");
            Assert.True(text.ActualHeight > 120);
            Assert.Equal(ScrollBarVisibility.Disabled, text.VerticalScrollBarVisibility);
        });
    }

    [Theory]
    [InlineData(1040, 660)]
    [InlineData(960, 500)]
    public void 日志列表和原文是独立滚动区域且五百条记录保留虚拟化(double width, double height)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960; window.MinHeight = 500;
            DesktopFixture.ShowPage(window, "LogPanel");
            ((TextBlock)window.FindName("LogEmptyText")).Visibility = Visibility.Collapsed;
            var store = new EventLogStore(new Localizer());
            for (var i = 0; i < 500; i++) store.Add($"记录-{i}\n" + string.Join("\n", Enumerable.Repeat("完整消息和异常内容，不应扩大横向测量宽度。", 20)), UiLogLevel.Error, UiLogSource.Network);
            var grid = (DataGrid)window.FindName("LogGrid");
            grid.ItemsSource = store.Entries; grid.SelectedIndex = 0;
            ((Expander)window.FindName("LogDetailsExpander")).IsExpanded = true;
            DesktopFixture.Layout(window, width, height);
            ExpansionInteractionTests.Settle(window);
            DesktopFixture.Save(window, $"log-independent-{width}.png");
            Assert.Null(ResultTableInteraction.Ancestor<ScrollViewer>(System.Windows.Media.VisualTreeHelper.GetParent(grid)));
            var scroll = DesktopFixture.Descendants(grid).OfType<ScrollViewer>().Single(viewer => viewer.Name == "DG_ScrollViewer");
            Assert.Equal(0, scroll.ScrollableWidth);
            Assert.True(grid.ActualHeight >= DesktopFixture.Descendants(grid).OfType<DataGridColumnHeadersPresenter>().First().ActualHeight + 38, $"日志视口不足一条完整记录：{grid.ActualHeight}");
            Assert.InRange(DesktopFixture.Descendants(grid).OfType<DataGridRow>().Count(), 1, 99);
            var original = (TextBox)window.FindName("LogDetailsTextBox");
            Assert.True(original.IsVisible && original.ActualHeight > 0);
            grid.ScrollIntoView(grid.Items[499]); window.UpdateLayout();
            Assert.NotNull(grid.ItemContainerGenerator.ContainerFromIndex(499));
            Assert.InRange(DesktopFixture.Descendants(grid).OfType<DataGridRow>().Count(), 1, 99);
            grid.ScrollIntoView(grid.Items[0]); window.UpdateLayout();
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            DesktopFixture.Descendants(row).OfType<ToggleButton>().First().IsChecked = true;
            ExpansionInteractionTests.Settle(window);
            var value = DesktopFixture.Descendants(row).OfType<TextBox>().OrderByDescending(text => text.ActualHeight).First();
            value.BringIntoView(new Rect(0, value.ActualHeight - 16, Math.Min(value.ActualWidth, 100), 16)); window.UpdateLayout();
            var bottom = value.TranslatePoint(new Point(0, value.ActualHeight), scroll).Y;
            Assert.InRange(bottom, 16, scroll.ActualHeight);
            Assert.Equal(0, scroll.ScrollableWidth);
        });
    }

    [Theory]
    [InlineData("WifiPanel", "Nearby", "ScanButton")]
    [InlineData("WifiPanel", "Profile", "AddManualButton")]
    [InlineData("StatusPanel", "RecentEvents", "CheckNowButton")]
    public void 补充空态复用既有文案操作并同步禁用状态(string page, string prefix, string originalName)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, page);
            var message = (TextBlock)window.FindName(prefix + "EmptyText");
            message.Text = "现有空状态文案"; message.Visibility = Visibility.Visible;
            var original = (Button)window.FindName(originalName);
            original.Content = "现有操作";
            DesktopFixture.Layout(window, 1040, 660);
            var guide = Assert.IsType<Button>(window.FindName(prefix + "EmptyAction"));
            guide.BringIntoView(); window.UpdateLayout();
            Assert.True(guide.IsVisible && guide.Focusable && guide.ActualHeight >= 32);
            Assert.Equal(original.Content, guide.Content);
            original.IsEnabled = false; window.UpdateLayout();
            Assert.False(guide.IsEnabled);
            DesktopFixture.Save(window, $"empty-extra-{prefix}.png");
        });
    }
}
