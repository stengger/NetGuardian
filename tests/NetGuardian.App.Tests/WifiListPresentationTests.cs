using System.Windows;
using System.Collections.ObjectModel;
using NetGuardian.App.Presentation;
using System.Windows.Controls;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using Xunit;
using TextBox = System.Windows.Controls.TextBox;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WifiListPresentationTests
{
    [Fact]
    public void 周围网络提供有名称的搜索过滤和排序入口()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            Assert.IsAssignableFrom<TextBox>(window.FindName("NearbySearchTextBox"));
            Assert.IsType<CheckBox>(window.FindName("NearbyOnlyNewCheckBox"));
            Assert.IsType<ComboBox>(window.FindName("NearbySortComboBox"));
        });
    }

    [Fact]
    public void 信号列同时提供图标和数字()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var table = (DataGrid)window.FindName("NearbyGrid");
            table.ItemsSource = new[] { new { Ssid = "网络", SignalPercent = 80, Authentication = "WPA2" } };
            DesktopFixture.Layout(window, 1180, 760);
            var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
            var cell = table.Columns[1].GetCellContent(row);
            Assert.Contains(DesktopFixture.Descendants(cell), element => element is SymbolIcon);
        });
    }
    [Fact]
    public void 筛选排序不改变源顺序且可清除条件()
    {
        DesktopFixture.Run(_ =>
        {
            var source = new ObservableCollection<Network>
            {
                new("Beta", 20, true), new("Alpha", 90, false), new("Gamma", 50, false)
            };
            var view = new WifiListView(source, item => ((Network)item).Ssid,
                item => ((Network)item).Signal, item => ((Network)item).Added);
            view.Update("  a  ", true, true);
            Assert.Equal(new[] { "Alpha", "Gamma" }, view.View.Cast<Network>().Select(item => item.Ssid));
            view.Update("missing", false, false);
            Assert.Empty(view.View.Cast<object>());
            view.Update("", false, false);
            Assert.Equal(new[] { "Alpha", "Gamma", "Beta" }, view.View.Cast<Network>().Select(item => item.Ssid));
            Assert.Equal(new[] { "Beta", "Alpha", "Gamma" }, source.Select(item => item.Ssid));
        });
    }

    [Fact]
    public void 连接活动结束恢复内容但不覆盖禁用判断()
    {
        DesktopFixture.Run(window =>
        {
            var button = (Button)window.FindName("ConnectButton");
            var content = button.Content;
            var width = button.MinWidth;
            using (new ButtonActivityPresentation(button, "连接中…"))
            {
                Assert.IsType<StackPanel>(button.Content);
                Assert.Equal(width, button.MinWidth);
                button.IsEnabled = false;
            }
            Assert.Same(content, button.Content);
            Assert.False(button.IsEnabled);
        });
    }

    [Fact]
    public void 当前连接标记随快照更新且不覆盖选中框()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var table = (DataGrid)window.FindName("NearbyGrid");
            table.ItemsSource = new[] { new { Ssid = "当前网络", SignalPercent = 80, Authentication = "WPA2" } };
            WifiRowPresentation.SetCurrentSsid(table, "当前网络");
            DesktopFixture.Layout(window, 1180, 760);
            var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.Equal(FontWeights.SemiBold, row.FontWeight);
            row.IsSelected = true;
            window.UpdateLayout();
            Assert.Equal(window.FindResource("RowSelectionStrokeBrush"), row.BorderBrush);
            WifiRowPresentation.SetCurrentSsid(table, null);
            window.UpdateLayout();
            Assert.NotEqual(FontWeights.SemiBold, row.FontWeight);
        });
    }

    private sealed record Network(string Ssid, int Signal, bool Added);
}
