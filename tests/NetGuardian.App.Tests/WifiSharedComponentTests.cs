using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WifiSharedComponentTests
{
    [Fact]
    public void 编译分区头内容能绑定同一窗口的数量和菜单状态源()
    {
        DesktopFixture.Run(window =>
        {
            // 主窗口先加载隐藏的 Wi-Fi 页，导航后才首次展开视觉模板。
            var fixture = new Fixtures.CompiledSectionHeader { Visibility = Visibility.Collapsed };
            ((Panel)window.Content).Children.Add(fixture);
            fixture.Networks.ItemsSource = new[] { "网络一", "网络二" };
            DesktopFixture.Layout(window, 1180, 758);
            fixture.Visibility = Visibility.Visible;
            DesktopFixture.Layout(window, 1180, 758);
            Assert.Equal("2", fixture.CountText.Text);
            Assert.Same(fixture.FilterSource, fixture.FilterButton.Tag);
            fixture.Networks.ItemsSource = new[] { "网络一", "网络二", "网络三" };
            window.UpdateLayout();
            Assert.Equal("3", fixture.CountText.Text);
        });
    }

    [Fact]
    public void 紧凑工具按钮保持三十二高且忙碌内容不改变尺寸()
    {
        DesktopFixture.Run(window =>
        {
            var style = Assert.IsType<Style>(window.TryFindResource("ToolbarPrimaryButtonStyle"));
            var button = new Button { Content = "连接", Style = style, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top };
            ((Panel)window.Content).Children.Add(button);
            DesktopFixture.Layout(window, 1180, 758);
            Assert.Equal(32, button.ActualHeight);
            var width = button.ActualWidth;
            using (new ButtonActivityPresentation(button, "连接中…"))
            {
                window.UpdateLayout();
                Assert.Equal(32, button.ActualHeight);
                Assert.Equal(width, button.ActualWidth);
                DesktopFixture.Save(window, "shared-toolbar-busy.png");
            }
            Assert.Equal(width, button.ActualWidth);
            Assert.NotNull(button.FocusVisualStyle);
            var existing = (Button)window.FindName("StartMonitorButton");
            Assert.True(existing.ActualHeight > 32, "新增工具样式不得连带修改其他页与全局按钮。");
        });
    }

    [Fact]
    public void 共用分区头容纳筛选工具且折叠后只保留标题行()
    {
        DesktopFixture.Run(window =>
        {
            var type = typeof(WifiRowPresentation).Assembly.GetType("NetGuardian.App.Views.ListSectionHeader");
            Assert.NotNull(type);
            var header = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type!));
            header.Width = 320;
            header.HorizontalAlignment = HorizontalAlignment.Right;
            header.VerticalAlignment = VerticalAlignment.Top;
            var title = new TextBlock { Text = "周围网络 32", VerticalAlignment = VerticalAlignment.Center };
            var search = new Wpf.Ui.Controls.TextBox { PlaceholderText = "搜索网络", Height = 32, MinHeight = 32,
                VerticalContentAlignment = VerticalAlignment.Center };
            type!.GetProperty("Heading")!.SetValue(header, title);
            type.GetProperty("Tools")!.SetValue(header, search);
            ((Panel)window.Content).Children.Add(header);
            DesktopFixture.Layout(window, 1180, 758);
            Assert.Equal(32, search.ActualHeight);
            var bounds = search.TransformToAncestor(header).TransformBounds(new Rect(search.RenderSize));
            Assert.True(bounds.Left >= 0 && bounds.Right <= header.ActualWidth);
            Assert.Equal(72, header.ActualHeight);
            DesktopFixture.Save(window, "shared-section-header.png");
            type.GetProperty("IsExpanded")!.SetValue(header, false);
            window.UpdateLayout();
            Assert.False(search.IsVisible);
            Assert.Equal(32, header.ActualHeight);
        });
    }

    [Fact]
    public void 覆盖滚动模板保留虚拟化和最后一行可达性()
    {
        DesktopFixture.Run(window =>
        {
            var style = Assert.IsType<Style>(window.TryFindResource("WifiWorkspaceListStyle"));
            var table = new DataGrid { Style = style, Width = 320, Height = 180, AutoGenerateColumns = false,
                CanUserAddRows = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            table.Columns.Add(new DataGridTextColumn { Header = "网络", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            table.ItemsSource = Enumerable.Range(0, 60).Select(i => new { Name = $"网络-{i}" }).ToArray();
            ((Panel)window.Content).Children.Add(table);
            DesktopFixture.Layout(window, 1180, 758);
            var scroll = WifiSectionLayoutTests.Scroll(table);
            Assert.True(scroll.ScrollableHeight > 0);
            Assert.True(DesktopFixture.Descendants(table).OfType<DataGridRow>().Count() < 60);
            var bar = DesktopFixture.Descendants(scroll).OfType<System.Windows.Controls.Primitives.ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
            var thumb = DesktopFixture.Descendants(bar).OfType<System.Windows.Controls.Primitives.Thumb>().Single();
            Assert.True(thumb.ActualHeight >= 24);
            Assert.Equal(12, bar.ActualWidth);
            scroll.ScrollToBottom();
            window.UpdateLayout();
            Assert.NotNull(table.ItemContainerGenerator.ContainerFromIndex(59));
            Assert.Equal(0, scroll.ScrollableWidth);
            DesktopFixture.Save(window, "shared-overlay-scroll.png");
        });
    }
}
