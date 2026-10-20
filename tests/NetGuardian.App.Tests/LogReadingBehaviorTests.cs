using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class LogReadingBehaviorTests
{
    [Theory]
    [InlineData(120, false)]
    [InlineData(-120, true)]
    public void 父级已处理滚轮时仍按阅读方向维护跟随(int delta, bool expectedFollow)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "LogPanel");
            var grid = (DataGrid)window.FindName("LogGrid");
            var follow = (CheckBox)window.FindName("LogFollowCheckBox");
            LogReadingBehavior.Attach(grid, follow);
            follow.IsChecked = true;
            // 外层正文的滚轮交接先处理预览事件，日志仍须观察用户向上阅读。
            window.PreviewMouseWheel += (_, args) => args.Handled = true;
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = Mouse.PreviewMouseWheelEvent
            };
            grid.RaiseEvent(wheel);
            Assert.True(wheel.Handled);
            Assert.Equal(expectedFollow, follow.IsChecked);
        });
    }

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
