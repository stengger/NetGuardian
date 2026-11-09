using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class EventLogPresentationTests
{
    [Theory]
    [InlineData(0, 0, true, false)]
    [InlineData(8, 0, false, true)]
    [InlineData(8, 3, false, false)]
    public void 日志空态按记录和匹配结果提供对应操作(int total, int visible, bool detect, bool reset)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "LogPanel");
            EventLogPresentation.UpdateEmptyActions(window, total, visible);
            DesktopFixture.Layout(window, 1040, 660);
            Assert.Equal(detect, ((Button)window.FindName("LogEmptyAction")).IsVisible);
            Assert.Equal(reset, ((Button)window.FindName("LogResetFilterButton")).IsVisible);
            DesktopFixture.Save(window, $"logs-empty-{total}-{visible}.png");
        });
    }

    [Fact]
    public void 重置筛选保留日志条目与阅读状态()
    {
        DesktopFixture.Run(window =>
        {
            var search = (TextBox)window.FindName("LogSearchTextBox");
            var level = (ComboBox)window.FindName("LogLevelComboBox");
            var source = (ComboBox)window.FindName("LogSourceComboBox");
            var grid = (DataGrid)window.FindName("LogGrid");
            var follow = (CheckBox)window.FindName("LogFollowCheckBox");
            var records = new[] { new { Message = "保留的记录" } };
            grid.ItemsSource = records;
            follow.IsChecked = false;
            search.Text = "不存在";
            level.SelectedIndex = 2;
            source.SelectedIndex = 3;
            EventLogPresentation.ResetFilters(window);
            Assert.Equal("", search.Text);
            Assert.Equal(0, level.SelectedIndex);
            Assert.Equal(0, source.SelectedIndex);
            Assert.Same(records, grid.ItemsSource);
            Assert.False(follow.IsChecked);
        });
    }
}
