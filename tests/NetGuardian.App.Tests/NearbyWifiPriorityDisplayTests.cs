using System.Collections.ObjectModel;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class NearbyWifiPriorityDisplayTests
{
    [Fact]
    public void 附近恢复列表显示连续序号且扫描和重排不会显示全局编号()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var profiles = new ObservableCollection<WifiProfile>(Enumerable.Range(1, 63)
                .Select(i => new WifiProfile { Ssid = $"网络-{i}", Priority = i, CredentialSaved = true }));
            var nearby = new NearbySavedWifiView(profiles);
            var table = (DataGrid)window.FindName("ProfileGrid");
            table.ItemsSource = nearby.View;
            nearby.BindPriorityColumn((DataGridTextColumn)window.FindName("ProfilePriorityColumn"));
            nearby.Update(["网络-3", "网络-38", "网络-56", "网络-58"]);
            DesktopFixture.Layout(window, 1180, 758);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Assert.Equal(new[] { "1", "2", "3", "4" }, ReadPriorities(window));
            Assert.Equal(Enumerable.Range(1, 63), profiles.Select(p => p.Priority));

            nearby.Update(["网络-38", "网络-56"]);
            DesktopFixture.Layout(window, 1180, 758);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Assert.Equal(new[] { "1", "2" }, ReadPriorities(window));
            profiles.Move(55, 37);
            for (var index = 0; index < profiles.Count; index++) profiles[index].Priority = index + 1;
            nearby.View.Refresh();
            DesktopFixture.Layout(window, 1180, 758);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Assert.Equal(new[] { "网络-56", "网络-38" }, table.Items.Cast<WifiProfile>().Select(p => p.Ssid));
            Assert.Equal(new[] { "1", "2" }, ReadPriorities(window));

            nearby.Update(["网络-3", "网络-38", "网络-56", "网络-58"]);
            DesktopFixture.Layout(window, 1180, 758);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Assert.Equal(new[] { "1", "2", "3", "4" }, ReadPriorities(window));
            Assert.Equal(63, profiles.Count);
        });
    }

    [Fact]
    public void 滚动复用行和缩小扫描范围后仍显示当前列表序号()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var profiles = new ObservableCollection<WifiProfile>(Enumerable.Range(1, 80)
                .Select(i => new WifiProfile { Ssid = $"网络-{i}", Priority = i + 100, CredentialSaved = true }));
            var nearby = new NearbySavedWifiView(profiles);
            var table = (DataGrid)window.FindName("ProfileGrid");
            var column = (DataGridTextColumn)window.FindName("ProfilePriorityColumn");
            table.ItemsSource = nearby.View;
            nearby.BindPriorityColumn((DataGridTextColumn)window.FindName("ProfilePriorityColumn"));
            nearby.Update(profiles.Select(p => p.Ssid));
            DesktopFixture.Layout(window, 1180, 758);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            table.ScrollIntoView(profiles[55]);
            DesktopFixture.Layout(window, 1180, 758);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Assert.Equal("56", ((TextBlock)column.GetCellContent(profiles[55])).Text);
            nearby.Update(["网络-56", "网络-70"]);
            table.ScrollIntoView(profiles[55]);
            DesktopFixture.Layout(window, 1180, 758);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Assert.Equal(new[] { "1", "2" }, ReadPriorities(window));
            Assert.Equal(156, profiles[55].Priority);
        });
    }
    private static string[] ReadPriorities(System.Windows.Window window)
    {
        var table = (DataGrid)window.FindName("ProfileGrid");
        var column = (DataGridTextColumn)window.FindName("ProfilePriorityColumn");
        return table.Items.Cast<WifiProfile>().Select(profile => ((TextBlock)column.GetCellContent(profile)).Text).ToArray();
    }
}