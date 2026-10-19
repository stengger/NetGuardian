using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class LogReadingBehaviorTests
{
    [Fact]
    public void 用户操作垂直滚动条暂停跟随而程序滚动不暂停()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "LogPanel");
            var grid = (DataGrid)window.FindName("LogGrid");
            var follow = (CheckBox)window.FindName("LogFollowCheckBox");
            grid.ItemsSource = Enumerable.Range(0, 100).Select(i => new { Message = $"记录 {i}" }).ToArray();
            LogReadingBehavior.Attach(grid, follow);
            DesktopFixture.Layout(window, 1040, 660);
            var scrollbar = FindScrollBar(grid);
            Assert.NotNull(scrollbar);
            follow.IsChecked = true;
            grid.ScrollIntoView(grid.Items[99]);
            grid.UpdateLayout();
            Assert.True(follow.IsChecked);
            scrollbar.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack, 10));
            Assert.False(follow.IsChecked);
            follow.IsChecked = true;
            scrollbar.RaiseEvent(new ScrollEventArgs(ScrollEventType.LargeDecrement, 0));
            Assert.False(follow.IsChecked);
        });
    }

    private static ScrollBar? FindScrollBar(System.Windows.DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollBar { Orientation: Orientation.Vertical } bar) return bar;
            if (FindScrollBar(child) is { } found) return found;
        }
        return null;
    }
}
