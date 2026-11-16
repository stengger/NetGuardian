using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Runtime;

namespace NetGuardian.App;

public partial class MainWindow
{
    private readonly EventLogStore _eventLogStore;
    private ICollectionView? _logView;
    private PersistedEventLogReader? _persistedLogReader;
    private readonly DispatcherTimer _logRefreshTimer = new();
    private bool _isLoadingPersistedLogs;
    private string _logReadError = "";

    private void InitializePersistedLogs()
    {
        var directories = new[]
        {
            (_fileLog as FileLogSink)?.Directory, _journal.Directory,
            Path.Combine(GuardianPaths.MachineDirectory, "logs"), Path.Combine(GuardianPaths.UserDirectory, "logs")
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!);
        _persistedLogReader = new(directories, MaxLogLines, localizer: _localizer);
        _logRefreshTimer.Interval = TimeSpan.FromSeconds(2);
        _logRefreshTimer.Tick += async (_, _) => await RefreshPersistedLogsAsync();
        _logRefreshTimer.Start();
        _ = RefreshPersistedLogsAsync();
    }

    private async Task RefreshPersistedLogsAsync()
    {
        if (_persistedLogReader is null || _isLoadingPersistedLogs || _resourcesCleaned) return;
        _isLoadingPersistedLogs = true;
        try
        {
            // 文件读取和历史统计均在工作线程执行，不阻塞 WPF 的滚动与输入。
            var result = await Task.Run(() =>
            {
                var batch = _persistedLogReader.Read();
                return (Batch: batch, Summary: batch.Entries.Count > 0 ? _journal.GetSummary() : null);
            });
            if (_resourcesCleaned) return;
            var previousCount = _eventLogStore.Entries.Count;
            // 无新增记录时只清理过期项，不重建视图，保留正在阅读的选择与滚动位置。
            if (result.Batch.Entries.Count > 0)
            {
                using (_logView?.DeferRefresh()) _eventLogStore.Import(result.Batch.Entries, DateTimeOffset.Now);
            }
            else _eventLogStore.Import(result.Batch.Entries, DateTimeOffset.Now);
            _logReadError = string.Join(Environment.NewLine, result.Batch.Errors);
            UpdateLogScope();
            UpdateLogViewInfo();
            if (result.Batch.Entries.Count > 0 || previousCount != _eventLogStore.Entries.Count) RebuildRecentEvents();
            if (result.Summary is not null) ApplyWeeklySummary(result.Summary);
            UpdateEmptyStates();
            if (result.Batch.Entries.Count > 0 && LogFollowCheckBox.IsChecked == true
                && _logView?.Cast<UiLogEntry>().LastOrDefault() is { } last) LogGrid.ScrollIntoView(last);
        }
        catch (Exception ex)
        {
            if (_resourcesCleaned) return;
            // 读取失败只显示一次状态，不调用 Log，避免错误自身被重复持久化与导入。
            _logReadError = ex.Message;
            UpdateLogScope();
        }
        finally { _isLoadingPersistedLogs = false; }
    }

    private void UpdateLogScope()
    {
        LogScopeText.Text = string.Format(_localizer.T("LogSessionScope"), MaxLogLines);
        if (!string.IsNullOrEmpty(_logReadError))
            LogScopeText.Text += Environment.NewLine + string.Format(_localizer.T("LogHistoryReadFailed"), _logReadError);
    }

    private void RebuildRecentEvents()
    {
        _recentEvents.Clear();
        foreach (var entry in _eventLogStore.Entries.OrderByDescending(entry => entry.Time).Take(6))
            _recentEvents.Add($"{entry.TimeText}  {entry.Summary}");
    }

    private void InitializeLogView()
    {
        _logView = CollectionViewSource.GetDefaultView(_eventLogStore.Entries);
        _logView.Filter = item => item is UiLogEntry entry && entry.Matches(LogSearchTextBox.Text,
            SelectedLogLevel(), SelectedLogSource());
        LogGrid.ItemsSource = _logView;
        LogReadingBehavior.Attach(LogGrid, LogFollowCheckBox);
        UpdateLogViewInfo();
    }

    private UiLogLevel? SelectedLogLevel() => LogLevelComboBox.SelectedItem is ComboBoxItem { Tag: string tag }
        && System.Enum.TryParse<UiLogLevel>(tag, out var level) ? level : null;

    private UiLogSource? SelectedLogSource() => LogSourceComboBox.SelectedItem is ComboBoxItem { Tag: string tag }
        && System.Enum.TryParse<UiLogSource>(tag, out var source) ? source : null;

    private void Log(string message, UiLogLevel level = UiLogLevel.Information, UiLogSource source = UiLogSource.Interface)
    {
        var entry = _eventLogStore.Add(message, level, source);
        _fileLog.Write($"[{level}/{source}] {message}");
        RebuildRecentEvents();
        UpdateLogViewInfo();
        if (LogFollowCheckBox.IsChecked == true && _logView?.Contains(entry) == true) LogGrid.ScrollIntoView(entry);
        UpdateEmptyStates();
    }

    private void EngineLog(string message, UiLogLevel level = UiLogLevel.Information) => Log(message, level, UiLogSource.Monitor);
    private void NetworkLog(string message, UiLogLevel level = UiLogLevel.Information) => Log(message, level, UiLogSource.Network);
    private void DiagnosticLog(string message, UiLogLevel level = UiLogLevel.Information) => Log(message, level, UiLogSource.Diagnostic);

    private void LogFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_logView is null) return;
        _logView.Refresh();
        UpdateLogViewInfo();
    }

    private void LogResetFilter_Click(object sender, RoutedEventArgs e) => EventLogPresentation.ResetFilters(this);

    private void LogFollow_Changed(object sender, RoutedEventArgs e)
    {
        if (_logView is null || LogFollowCheckBox.IsChecked != true) return;
        var last = _logView.Cast<UiLogEntry>().LastOrDefault();
        if (last is not null) LogGrid.ScrollIntoView(last);
    }

    private void LogReading_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.PageUp or Key.Home) LogFollowCheckBox.IsChecked = false;
    }

    private void UpdateLogViewInfo()
    {
        if (_logView is null) return;
        var count = _logView.Cast<object>().Count();
        LogCountText.Text = string.Format(_localizer.T("LogVisibleCount"), count, _eventLogStore.Entries.Count);
        EventLogPresentation.UpdateEmptyActions(this, _eventLogStore.Entries.Count, count);
        LogEmptyText.Text = _localizer.T(_eventLogStore.Entries.Count == 0 ? "LogNoEntries" : "LogNoMatches");
    }

    private void RefreshLogLanguage()
    {
        LogResetFilterButton.Content = _localizer.T("UiResetFilters");
        LogSearchLabel.Text = _localizer.T("LogSearch");
        LogLevelLabel.Text = _localizer.T("LogLevel");
        LogSourceLabel.Text = _localizer.T("LogSource");
        UpdateLogScope();
        LogFollowCheckBox.Content = _localizer.T("LogFollowLatest");
        LogDetailsExpander.Header = _localizer.T("LogSelectedDetails");
        LogTimeColumn.Header = _localizer.T("LogTime");
        LogLevelColumn.Header = _localizer.T("LogLevel");
        LogSourceColumn.Header = _localizer.T("LogSource");
        LogMessageColumn.Header = _localizer.T("LogMessage");
        foreach (ComboBoxItem item in LogLevelComboBox.Items)
            item.Content = _localizer.T(item.Tag?.ToString() == "all" ? "LogAllLevels" : $"LogLevel{item.Tag}");
        foreach (ComboBoxItem item in LogSourceComboBox.Items)
            item.Content = _localizer.T(item.Tag?.ToString() == "all" ? "LogAllSources" : $"LogSource{item.Tag}");
        _eventLogStore.RefreshLanguage();
        _logView?.Refresh();
        UpdateLogViewInfo();
    }
}
