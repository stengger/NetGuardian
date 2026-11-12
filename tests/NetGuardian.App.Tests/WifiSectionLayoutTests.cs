using System.Windows;
using System.Windows.Controls;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WifiSectionLayoutTests
{
    [Theory]
    [InlineData(1040, 660, 32)]
    [InlineData(1180, 760, 100)]
    [InlineData(960, 500, 32)]
    public void 大量网络使用有限视口且优先级标题可见(double width, double height, int count)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960; window.MinHeight = 500;
            DesktopFixture.ShowPage(window, "WifiPanel");
            var nearby = (DataGrid)window.FindName("NearbyGrid");
            var priority = (DataGrid)window.FindName("ProfileGrid");
            nearby.ItemsSource = Enumerable.Range(1, count).Select(i => new { Ssid = $"实验室网络-{i}", SignalPercent = 80, Authentication = "WPA2", PriorityBadge = "可添加" }).ToArray();
            priority.ItemsSource = Enumerable.Range(1, 12).Select(i => new WifiProfile { Ssid = $"优先网络-{i}", Priority = i }).ToArray();
            DesktopFixture.Layout(window, width, height);
            DesktopFixture.Save(window, $"wifi-many-{width}-{count}.png");
            var nearbySection = Assert.IsType<Expander>(window.FindName("NearbyNetworksExpander"));
            var prioritySection = Assert.IsType<Expander>(window.FindName("PriorityNetworksExpander"));
            var region = (FrameworkElement)window.FindName("WifiListsGrid");
            Assert.True(nearbySection.IsExpanded && prioritySection.IsExpanded);
            Assert.True(nearbySection.ActualHeight <= ((FrameworkElement)window.FindName("WifiContentScrollViewer")).ActualHeight * .4 + 1);
            var title = (FrameworkElement)window.FindName("PriorityListTitle");
            var bounds = title.TransformToAncestor(window).TransformBounds(new Rect(title.RenderSize));
            Assert.InRange(bounds.Bottom, 1, height);
            Assert.True(DesktopFixture.Descendants(nearby).OfType<DataGridRow>().Count() < count);
            Assert.True(Scroll(nearby).ScrollableHeight > 0);
            Assert.Equal(0, ((ScrollViewer)window.FindName("WifiContentScrollViewer")).ScrollableHeight);
            nearbySection.IsExpanded = false;
            window.UpdateLayout();
            Assert.False(nearby.IsVisible);
            Assert.True(priority.IsVisible);
            nearbySection.IsExpanded = true;
            prioritySection.IsExpanded = false;
            window.UpdateLayout();
            Assert.True(nearby.IsVisible);
            Assert.False(priority.IsVisible);
            DesktopFixture.Save(window, $"wifi-folded-{width}-{count}.png");
        });
    }

    internal static ScrollViewer Scroll(DataGrid grid) => DesktopFixture.Descendants(grid).OfType<ScrollViewer>().Single(v => v.Name == "DG_ScrollViewer");
}
