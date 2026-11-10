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
    public void 详情继承属性传播引起文本更新时不会同步重建行定义()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "NetworkPanel");
            var grid = (DataGrid)window.FindName("AdapterGrid");
            grid.ItemsSource = new[] { new { Name = "接口", Type = "无线", Status = "已连接", Ipv4 = "192.0.2.1", Gateway = "", DnsServers = "2001:4860:4860::8888" } };
            DesktopFixture.Layout(window, 1040, 660);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            DesktopFixture.Descendants(row).OfType<ToggleButton>().First().IsChecked = true;
            ExpansionInteractionTests.Settle(window);
            var details = DesktopFixture.Descendants(row).OfType<NetGuardian.App.Presentation.TableRowDetails>().Single();
            var value = details.Children.OfType<TextBox>().First();
            // 用继承的 Language 稳定重现日志中的“树遍历 → 绑定更新 → TextChanged”路径。
            value.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Language.IetfLanguageTag")
            {
                Mode = System.Windows.Data.BindingMode.OneWay,
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.Self)
            });
            details.Language = System.Windows.Markup.XmlLanguage.GetLanguage("fr-FR");
            ExpansionInteractionTests.Settle(window);
            Assert.Equal("fr-fr", value.Text);
            Assert.True(details.RowDefinitions.Count > 0);
            Assert.NotNull(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(grid)!.GetChildren());
        });
    }

    [Theory]
    [InlineData("更新上下文")]
    [InlineData("刷新列表")]
    [InlineData("卸载详情")]
    public void 已展开详情刷新和回收时不会在树遍历中修改布局(string operation)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "NetworkPanel");
            var grid = (DataGrid)window.FindName("AdapterGrid");
            var first = new { Name = "原接口", Type = "无线", Status = "已连接", Ipv4 = "192.0.2.1", Gateway = "", DnsServers = "2001:4860:4860::8888" };
            var second = new { Name = "更新接口", Type = "有线", Status = "未连接", Ipv4 = "192.0.2.2", Gateway = "", DnsServers = "2001:4860:4860::8844" };
            grid.ItemsSource = new[] { first };
            DesktopFixture.Layout(window, 1040, 660);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            DesktopFixture.Descendants(row).OfType<ToggleButton>().First().IsChecked = true;
            ExpansionInteractionTests.Settle(window);
            var details = DesktopFixture.Descendants(row).OfType<NetGuardian.App.Presentation.TableRowDetails>().Single();
            if (operation == "更新上下文") details.DataContext = second;
            else if (operation == "刷新列表") grid.ItemsSource = new[] { second };
            else row.DetailsTemplate = null;
            ExpansionInteractionTests.Settle(window);
            if (operation == "更新上下文")
                Assert.Contains(details.Children.OfType<TextBox>(), value => value.Text == "更新接口");
            // 异常之后仍需能够刷新、重新展开并显示最新原值。
            grid.ItemsSource = new[] { second };
            DesktopFixture.Layout(window, 1040, 660);
            row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            row.ClearValue(DataGridRow.DetailsTemplateProperty);
            DesktopFixture.Descendants(row).OfType<ToggleButton>().First().IsChecked = true;
            ExpansionInteractionTests.Settle(window);
            Assert.Contains(DesktopFixture.Descendants(row).OfType<TextBox>(), value => value.Text == "更新接口");
            Assert.NotNull(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(grid)!.GetChildren());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 长详情减少短字段重复且标签在窄区域上下排列(bool compact)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "NetworkPanel");
            var grid = (DataGrid)window.FindName("AdapterGrid");
            grid.Columns[5].Header = "DNS（IPv4 / IPv6）";
            grid.ItemsSource = new[] { new { Name = "接口名称", Type = "无线", Status = "已连接", Ipv4 = "192.0.2.1", Gateway = "192.0.2.254", DnsServers = string.Join("\n", Enumerable.Repeat("2001:4860:4860::8888", 10)) } };
            if (compact)
            {
                grid.Width = 420; grid.MinWidth = 0; grid.HorizontalAlignment = HorizontalAlignment.Left;
                foreach (var column in grid.Columns) { column.MinWidth = 0; column.Width = new DataGridLength(1, DataGridLengthUnitType.Star); }
            }
            DesktopFixture.Layout(window, 1040, 660);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            DesktopFixture.Descendants(row).OfType<ToggleButton>().First().IsChecked = true;
            ExpansionInteractionTests.Settle(window);
            var value = DesktopFixture.Descendants(row).OfType<TextBox>().Single(text => text.Text.Contains("2001:4860:4860::8888"));
            var host = (Grid)value.Parent;
            var label = host.Children.OfType<TextBlock>().Single(text => text.Text == "DNS（IPv4 / IPv6）");
            Assert.InRange(host.Children.OfType<TextBox>().Count(text => text.IsVisible), 2, 5);
            Assert.DoesNotContain(host.Children.OfType<TextBox>(), text => text.IsVisible && text.Text == "已连接");
            if (grid.Columns[1].Visibility != Visibility.Visible)
                Assert.Contains(host.Children.OfType<TextBox>(), text => text.IsVisible && text.Text == "无线");
            if (compact)
            {
                Assert.Equal(0, Grid.GetColumn(value));
                Assert.Equal(Grid.GetRow(label) + 1, Grid.GetRow(value));
            }
            else
            {
                Assert.Equal(1, Grid.GetColumn(value));
                Assert.True(label.ActualHeight <= label.FontSize * 1.8, $"标签被强制换行：{label.RenderSize}");
            }
            var scroll = DesktopFixture.Descendants(grid).OfType<ScrollViewer>().Single(viewer => viewer.Name == "DG_ScrollViewer");
            Assert.Equal(0, scroll.ScrollableWidth);
            DesktopFixture.Save(window, compact ? "details-label-compact.png" : "details-label-natural.png");
        });
    }

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
            var label = host.Children.OfType<TextBlock>().Single(text => text.IsVisible && Grid.GetRow(text) == Grid.GetRow(value));
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
            var details = DesktopFixture.Descendants(presenter).OfType<NetGuardian.App.Presentation.TableRowDetails>().Single();
            Assert.True(presenter.ActualHeight >= details.ActualHeight && details.ActualHeight > 0, "详情实际内容被裁切。");
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
