using System.ComponentModel;
using System.Windows;
using NetGuardian.Core.Localization;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class EventLogPresentationTests
{
    [Fact]
    public void 历史与新增日志默认按真实时间倒序且不改变存储顺序()
    {
        DesktopFixture.Run(window =>
        {
            var grid = (DataGrid)window.FindName("LogGrid");
            var store = new EventLogStore(new Localizer());
            var now = DateTimeOffset.Now;
            var records = new[]
            {
                new PersistedLogEntry("中间", now.AddMinutes(-2), "中间记录", UiLogLevel.Information, UiLogSource.Interface),
                new PersistedLogEntry("最新", now.AddMinutes(-1), "最新记录", UiLogLevel.Information, UiLogSource.Interface),
                new PersistedLogEntry("最旧", now.AddMinutes(-3), "最旧记录", UiLogLevel.Information, UiLogSource.Interface)
            };
            var view = EventLogPresentation.CreateView(grid, store);
            EventLogPresentation.ImportHistory(grid, store, records, now);
            Assert.Equal(new[] { "最新记录", "中间记录", "最旧记录" }, grid.Items.Cast<UiLogEntry>().Select(entry => entry.Message));
            Assert.Equal(new[] { "最旧记录", "中间记录", "最新记录" }, store.Entries.Select(entry => entry.Message));
            Assert.Equal(nameof(UiLogEntry.Time), grid.Columns[0].SortMemberPath);
            Assert.Equal(ListSortDirection.Descending, grid.Columns[0].SortDirection);
            var added = store.Add("实时新增", UiLogLevel.Information, UiLogSource.Interface);
            Assert.Same(added, view.Cast<UiLogEntry>().First());
            view.Filter = item => ((UiLogEntry)item).Message.Contains("记录");
            view.Refresh();
            Assert.Equal(new[] { "最新记录", "中间记录", "最旧记录" }, view.Cast<UiLogEntry>().Select(entry => entry.Message));
        });
    }

    [Fact]
    public void 跟随最新使用筛选后的真实时间且不受手动排序影响()
    {
        DesktopFixture.Run(window =>
        {
            var grid = (DataGrid)window.FindName("LogGrid");
            var store = new EventLogStore(new Localizer());
            var view = EventLogPresentation.CreateView(grid, store);
            Assert.Null(EventLogPresentation.LatestVisibleEntry(view));
            var now = DateTimeOffset.Now;
            store.Import(new[]
            {
                new PersistedLogEntry("旧", now.AddMinutes(-3), "旧警告", UiLogLevel.Warning, UiLogSource.Network),
                new PersistedLogEntry("新", now.AddMinutes(-2), "新警告", UiLogLevel.Warning, UiLogSource.Network),
                new PersistedLogEntry("信息", now.AddMinutes(-1), "最新信息", UiLogLevel.Information, UiLogSource.Interface)
            }, now);
            view.Filter = item => ((UiLogEntry)item).Level == UiLogLevel.Warning;
            view.Refresh();
            Assert.Equal("新警告", EventLogPresentation.LatestVisibleEntry(view)!.Message);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new(nameof(UiLogEntry.Time), ListSortDirection.Ascending));
            Assert.Equal("新警告", EventLogPresentation.LatestVisibleEntry(view)!.Message);
        });
    }
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
