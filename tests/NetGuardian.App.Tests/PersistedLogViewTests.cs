using System.Windows.Controls;
using System.Windows.Data;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class PersistedLogViewTests
{
    [Fact]
    public void 真实日志表格显示历史后台与恢复来源并支持筛选和原文阅读()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "LogPanel");
            var now = DateTimeOffset.Now;
            var localizer = new Localizer();
            var store = new EventLogStore(localizer);
            var raw = "{\"type\":\"recovery\",\"action\":\"failed\"}";
            var recovery = new PersistedLogEntry("journal", now.AddHours(-1), raw, UiLogLevel.Error,
                UiLogSource.Recovery, "自动恢复未成功（恢复事件记录）。", true);
            store.Import(new[]
            {
                recovery,
                new PersistedLogEntry("background", now.AddMinutes(-1), "RecoveryDnsFlush：", UiLogLevel.Information,
                    UiLogSource.Background, "恢复策略：刷新 DNS 缓存。")
            }, now);
            var grid = (DataGrid)window.FindName("LogGrid");
            var view = CollectionViewSource.GetDefaultView(store.Entries);
            grid.ItemsSource = view;
            var sources = (ComboBox)window.FindName("LogSourceComboBox");
            foreach (ComboBoxItem source in sources.Items)
                source.Content = localizer.T(source.Tag?.ToString() == "all" ? "LogAllSources" : "LogSource" + source.Tag);
            Assert.Contains(sources.Items.Cast<ComboBoxItem>(), item => Equals(item.Tag, "Background"));
            Assert.Contains(sources.Items.Cast<ComboBoxItem>(), item => Equals(item.Tag, "Recovery"));
            var item = store.Entries[0];
            grid.SelectedItem = item;
            DesktopFixture.Layout(window, 1180, 760);
            Assert.Equal(raw, ((TextBox)window.FindName("LogDetailsTextBox")).Text);
            Assert.Equal("恢复事件", item.SourceText);
            Assert.Equal("后台守护", store.Entries[1].SourceText);
            using (view.DeferRefresh()) store.Import(new[] { recovery with { Message = raw + "\n完整原文" } }, now);
            DesktopFixture.Layout(window, 1180, 760);
            Assert.Same(item, grid.SelectedItem);
            Assert.Contains("完整原文", ((TextBox)window.FindName("LogDetailsTextBox")).Text);
            view.Filter = row => ((UiLogEntry)row).Matches("自动恢复", UiLogLevel.Error, UiLogSource.Recovery);
            Assert.Single(view.Cast<UiLogEntry>());
            view.Filter = null;
            Assert.Equal(2, view.Cast<UiLogEntry>().Count());
            foreach (var (name, key) in new[]
            {
                ("LogSearchLabel", "LogSearch"), ("LogLevelLabel", "LogLevel"), ("LogSourceLabel", "LogSource")
            }) ((TextBlock)window.FindName(name)).Text = localizer.T(key);
            foreach (var (name, key) in new[]
            {
                ("LogTimeColumn", "LogTime"), ("LogLevelColumn", "LogLevel"),
                ("LogSourceColumn", "LogSource"), ("LogMessageColumn", "LogMessage")
            }) ((DataGridColumn)window.FindName(name)).Header = localizer.T(key);
            ((Button)window.FindName("ExportLogsButton")).Content = localizer.T("ExportLogs");
            foreach (ComboBoxItem level in ((ComboBox)window.FindName("LogLevelComboBox")).Items)
                level.Content = localizer.T(level.Tag?.ToString() == "all" ? "LogAllLevels" : "LogLevel" + level.Tag);
            ((CheckBox)window.FindName("LogFollowCheckBox")).Content = localizer.T("LogFollowLatest");
            ((Expander)window.FindName("LogDetailsExpander")).Header = localizer.T("LogSelectedDetails");
            ((TextBlock)window.FindName("LogCountText")).Text = "显示 2/2 条";
            ((TextBlock)window.FindName("LogScopeText")).Text = string.Format(localizer.T("LogSessionScope"), 500);
            EventLogPresentation.UpdateEmptyActions(window, 2, 2);
            DesktopFixture.Layout(window, 1180, 760);
            Assert.Equal(System.Windows.Visibility.Collapsed, ((TextBlock)window.FindName("LogEmptyText")).Visibility);
            DesktopFixture.Save(window, "历史及后台日志-1180x760.png");
        });
    }
}
