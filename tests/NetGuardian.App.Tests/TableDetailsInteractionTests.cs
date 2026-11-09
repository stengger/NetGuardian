using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class TableDetailsInteractionTests
{
    [Fact]
    public void 详情分列显示全部字段且长值完整参与行高()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "NetworkPanel");
            var grid = (DataGrid)window.FindName("AdapterGrid");
            var longValue = string.Join("\n", Enumerable.Repeat("2001:4860:4860::8888 长地址清单", 20));
            grid.ItemsSource = new[] { new { Name = "含 空格的接口名称", Type = "无线", Status = "已连接", Ipv4 = "192.0.2.1", Gateway = "192.0.2.254", DnsServers = longValue } };
            DesktopFixture.Layout(window, 1040, 660);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            DesktopFixture.Descendants(row).OfType<ToggleButton>().First().IsChecked = true;
            ExpansionInteractionTests.Settle(window);
            var values = DesktopFixture.Descendants(row).OfType<TextBox>().ToArray();
            Assert.Equal(6, values.Length);
            var value = Assert.Single(values, text => text.Text.Contains("2001:4860:4860::8888"));
            Assert.Equal(longValue, value.Text);
            Assert.True(value.IsReadOnly && value.Focusable);
            Assert.True(value.ActualHeight > 160, $"长详情仍被截高：{value.ActualHeight}");
            Assert.Equal(ScrollBarVisibility.Disabled, value.VerticalScrollBarVisibility);
            var host = (Grid)value.Parent;
            Assert.Equal(1, Grid.GetColumn(value));
            var label = host.Children.OfType<TextBlock>().Single(text => Grid.GetRow(text) == Grid.GetRow(value));
            Assert.Equal(0, Grid.GetColumn(label));
            var scroll = DesktopFixture.Descendants(grid).OfType<ScrollViewer>().Single(viewer => viewer.Name == "DG_ScrollViewer");
            Assert.Equal(0, scroll.ScrollableWidth);
            DesktopFixture.Save(window, "details-long-expanded.png");
        });
    }

    [Fact]
    public void 普通行单击和回车空格共用同一展开状态()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "NetworkPanel");
            var grid = (DataGrid)window.FindName("AdapterGrid");
            grid.ItemsSource = new[] { new { Name = "接口", Type = "无线", Status = "已连接", Ipv4 = "192.0.2.1", Gateway = "", DnsServers = "" } };
            DesktopFixture.Layout(window, 1180, 760);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var text = grid.Columns[0].GetCellContent(row);
            Click(text);
            ExpansionInteractionTests.Settle(window);
            var toggle = DesktopFixture.Descendants(row).OfType<ToggleButton>().First();
            Assert.Equal(Visibility.Visible, row.DetailsVisibility);
            Assert.True(toggle.IsChecked);
            Key(row, window, System.Windows.Input.Key.Enter);
            ExpansionInteractionTests.Settle(window);
            Assert.Equal(Visibility.Collapsed, row.DetailsVisibility);
            Assert.False(toggle.IsChecked);
            Key(row, window, System.Windows.Input.Key.Space);
            ExpansionInteractionTests.Settle(window);
            Assert.Equal(Visibility.Visible, row.DetailsVisibility);
            Assert.True(toggle.IsChecked);
            var value = DesktopFixture.Descendants(row).OfType<TextBox>().First();
            value.SelectAll();
            Click(value);
            Key(value, window, System.Windows.Input.Key.Space);
            Assert.Equal(Visibility.Visible, row.DetailsVisibility);
            Assert.NotEmpty(value.SelectedText);
        });
    }

    [Fact]
    public void 选择行不会改变内容边界和尺寸()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "NetworkPanel");
            var grid = (DataGrid)window.FindName("AdapterGrid");
            grid.ItemsSource = new[] { new { Name = "接口", Type = "无线", Status = "已连接", Ipv4 = "192.0.2.1", Gateway = "", DnsServers = "" } };
            DesktopFixture.Layout(window, 1040, 660);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var text = grid.Columns[0].GetCellContent(row);
            var bounds = text.TransformToAncestor(row).TransformBounds(new Rect(text.RenderSize));
            row.IsSelected = true;
            window.UpdateLayout();
            Assert.Equal(bounds, text.TransformToAncestor(row).TransformBounds(new Rect(text.RenderSize)));
        });
    }

    [Fact]
    public void 详情展开收起期间保持可见并在结束后恢复自适应高度()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "NetworkPanel");
            var grid = (DataGrid)window.FindName("AdapterGrid");
            grid.ItemsSource = new[] { new { Name = "接口", Type = "无线", Status = "已连接", Ipv4 = "192.0.2.1", Gateway = "", DnsServers = "" } };
            DesktopFixture.Layout(window, 1180, 760);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var toggle = DesktopFixture.Descendants(row).OfType<ToggleButton>().First();
            toggle.IsChecked = true;
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var presenter = DesktopFixture.Descendants(row).OfType<DataGridDetailsPresenter>().Single();
            Assert.True(DependencyPropertyHelper.GetValueSource(presenter, FrameworkElement.HeightProperty).IsAnimated);
            ExpansionInteractionTests.Settle(window);
            Assert.True(double.IsNaN(presenter.Height));
            Assert.True(presenter.ActualHeight > 100);
            toggle.IsChecked = false;
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.True(presenter.IsVisible);
            Assert.True(DependencyPropertyHelper.GetValueSource(presenter, FrameworkElement.HeightProperty).IsAnimated);
            ExpansionInteractionTests.Settle(window);
            Assert.False(presenter.IsVisible);
            Assert.Equal(Visibility.Collapsed, row.DetailsVisibility);
        });
    }
    internal static void Click(UIElement element)
    {
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
    }

    internal static void Key(UIElement element, Window window, Key key) => element.RaiseEvent(
        new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
}
