using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NetGuardian.App.Presentation;
using NetGuardian.App.Views;
using NetGuardian.Core.Models;
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

    private IncidentHistoryReader? _incidentReader;
    private IncidentTimelineView? _incidentView;
    private IReadOnlyList<IncidentSummary> IncidentHistory { get; set; } = [];
    private bool IncidentEnglish => _localizer.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    private void RefreshIncidentLanguage()
    {
        if (_incidentView is null)
        {
            _incidentView = new();
            IncidentViewHost.Content = _incidentView;
            _incidentView.OpenAllLogsRequested += OpenIncidentAllLogs_Click;
        }
        _incidentView.SetLanguage(IncidentEnglish);
        UpdateIncidentViews();
        RebuildRecentEvents();
    }

    private void UpdateIncidentViews()
    {
        HomeIncidentButton.Content = IncidentPresentation.HomeText(IncidentHistory.FirstOrDefault(), IncidentEnglish);
        _incidentView?.ShowIncidents(IncidentHistory);
        RefreshObservabilitySummary();
    }

    private string? _incidentParent;
    private FrameworkElement? _incidentOrigin;

    private void CloseIncidents_Click(object sender, RoutedEventArgs e)
    {
        var parent = _incidentParent ?? "StatusPanel";
        _incidentParent = null;
        ShowPanelByTag(parent);
        _incidentOrigin?.Focus();
        _incidentOrigin = null;
    }

    private void OpenIncidents_Click(object sender, RoutedEventArgs e)
    {
        var id = IncidentPresentation.LinkedIncident((sender as FrameworkElement)?.DataContext as UiLogEntry);
        _logParent = null;
        _incidentParent = PolicyPanel.IsVisible ? "PolicyPanel" : "StatusPanel";
        _incidentOrigin = sender as FrameworkElement;
        ShowPanelByTag("IncidentPanel");
        _incidentView?.ShowIncidents(IncidentHistory, id ?? IncidentHistory.FirstOrDefault()?.Id);
        IncidentBackButton.Focus();
    }

    private void InitializePersistedLogs()
    {
        var directories = new[]
        {
            (_fileLog as FileLogSink)?.Directory, _journal.Directory,
            Path.Combine(GuardianPaths.MachineDirectory, "logs"), Path.Combine(GuardianPaths.UserDirectory, "logs")
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!);
        _incidentReader = new(directories);
        _persistedLogReader = new(directories, MaxLogLines, localizer: _localizer);
        _logRefreshTimer.Interval = TimeSpan.FromSeconds(2);
        _logRefreshTimer.Tick += async (_, _) => await RefreshPersistedLogsAsync();
        _logRefreshTimer.Start();
        _ = RefreshPersistedLogsAsync();
    }

    private async Task RefreshPersistedLogsAsync()
    {
        if (_incidentReader is null || _persistedLogReader is null || _isLoadingPersistedLogs || _resourcesCleaned) return;
        _isLoadingPersistedLogs = true;
        try
        {
            // 文件读取和历史统计均在工作线程执行，不阻塞 WPF 的滚动与输入。
            var result = await Task.Run(() =>
            {
                var batch = _persistedLogReader.Read();
                return (Batch: batch, Incidents: _incidentReader.Read(), Summary: batch.Entries.Count > 0 ? _journal.GetSummary() : null);
            });
            if (_resourcesCleaned) return;
            var previousCount = _eventLogStore.Entries.Count;
            // 无新增记录时只清理过期项，不重建视图，保留正在阅读的选择与滚动位置。
            EventLogPresentation.ImportHistory(LogGrid, _eventLogStore, result.Batch.Entries, DateTimeOffset.Now);
            if (!ReferenceEquals(IncidentHistory, result.Incidents))
            {
                IncidentHistory = result.Incidents;
                UpdateIncidentViews();
                RebuildRecentEvents();
            }
            _logReadError = string.Join(Environment.NewLine, result.Batch.Errors.Concat(_incidentReader.Errors));
            UpdateLogScope();
            UpdateLogViewInfo();
            if (result.Batch.Entries.Count > 0 || previousCount != _eventLogStore.Entries.Count) RebuildRecentEvents();
            if (result.Summary is not null) ApplyWeeklySummary(result.Summary);
            UpdateEmptyStates();
            if (result.Batch.Entries.Count > 0 && LogFollowCheckBox.IsChecked == true
                && _logView is not null && EventLogPresentation.LatestVisibleEntry(_logView) is { } latest) LogGrid.ScrollIntoView(latest);
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

    private string? _logParent;

    private void OpenIncidentAllLogs_Click(object sender, RoutedEventArgs e) => OpenEventLog_Click(IncidentRawLogButton, e);

    private void OpenEventLog_Click(object sender, RoutedEventArgs e)
    {
        if (IncidentPresentation.LinkedIncident((sender as FrameworkElement)?.DataContext as UiLogEntry) != null)
        {
            OpenIncidents_Click(sender, e);
            return;
        }
        _logParent = sender == IncidentRawLogButton ? "IncidentPanel" : PolicyPanel.IsVisible ? "PolicyPanel" : "StatusPanel";
        var recovery = _logParent == "PolicyPanel";
        var entry = (sender as FrameworkElement)?.DataContext as UiLogEntry;
        EventLogPresentation.ResetFilters(this, recovery);
        if (_logParent == "IncidentPanel") LogLevelComboBox.SelectedItem = LogLevelComboBox.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, "all"));
        LogGrid.SelectedItem = null;
        LogDetailsExpander.IsExpanded = false;
        ShowPanelByTag("LogPanel");
        if (entry is not null && _logView?.Contains(entry) == true)
        {
            LogFollowCheckBox.IsChecked = false;
            LogGrid.SelectedItem = entry;
            LogDetailsExpander.IsExpanded = true;
            LogGrid.ScrollIntoView(entry);
        }
        LogBackButton.Focus();
    }

    private void RenderLogContext()
    {
        var recovery = _logParent == "PolicyPanel";
        PageTitleText.Text = _localizer.T(recovery ? "RecoveryHistory" : "Log");
        LogBackButton.Content = _logParent == "IncidentPanel" ? (IncidentEnglish ? "Back to incidents" : "返回故障记录")
            : _localizer.T(recovery ? "BackToRecovery" : "BackToHome");
        UpdateLogViewInfo();
    }

    private void CloseEventLog_Click(object sender, RoutedEventArgs e)
    {
        var parent = _logParent ?? "StatusPanel";
        _logParent = null;
        ShowPanelByTag(parent);
        (parent == "IncidentPanel" ? IncidentRawLogButton : parent == "PolicyPanel" ? RecoveryLogsButton : HomeLogsButton).Focus();
    }

    private void ParentWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_logParent != null) CloseEventLog_Click(sender, e);
        if (_incidentParent != null) CloseIncidents_Click(sender, e);
    }

    private void RebuildRecentEvents()
    {
        var cutoff = DateTimeOffset.Now.AddDays(-7);
        var incidents = IncidentHistory.Where(i => i.EndedAt is null || i.EndedAt >= cutoff)
            .Take(6).Select(i => IncidentPresentation.HistoryEntry(i, _localizer)).ToList();
        var otherEvents = _eventLogStore.Entries.Where(entry => entry.IsKeyEvent && !entry.IsRecoveryEvent
            && entry.Source is not (UiLogSource.Monitor or UiLogSource.Background));
        _recentEvents.Clear();
        foreach (var entry in incidents.Concat(otherEvents).OrderByDescending(e => e.Time).Take(6)) _recentEvents.Add(entry);
        RecoveryEventsList.ItemsSource = incidents.Take(2).ToArray();
    }

    private void InitializeLogView()
    {
        _logView = EventLogPresentation.CreateView(LogGrid, _eventLogStore);
        _logView.Filter = item => item is UiLogEntry entry && entry.Matches(LogSearchTextBox.Text,
            SelectedLogLevel(), SelectedLogSource(), KeyEventsOnly);
        LogReadingBehavior.Attach(LogGrid, LogFollowCheckBox);
        UpdateLogViewInfo();
    }

    private bool KeyEventsOnly => LogLevelComboBox.SelectedItem is ComboBoxItem { Tag: "Key" };

    private UiLogLevel? SelectedLogLevel() => LogLevelComboBox.SelectedItem is ComboBoxItem { Tag: string tag }
        && System.Enum.TryParse<UiLogLevel>(tag, out var level) ? level : null;

    private UiLogSource? SelectedLogSource() => LogSourceComboBox.SelectedItem is ComboBoxItem { Tag: string tag }
        && System.Enum.TryParse<UiLogSource>(tag, out var source) ? source : null;

    private void Log(string message, UiLogLevel level = UiLogLevel.Information, UiLogSource source = UiLogSource.Interface)
    {
        _eventLogStore.Add(message, level, source);
        _fileLog.Write($"[{level}/{source}] {message}");
        RebuildRecentEvents();
        UpdateLogViewInfo();
        if (LogFollowCheckBox.IsChecked == true && _logView is not null
            && EventLogPresentation.LatestVisibleEntry(_logView) is { } latest) LogGrid.ScrollIntoView(latest);
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

    private void LogResetFilter_Click(object sender, RoutedEventArgs e)
    {
        EventLogPresentation.ResetFilters(this, _logParent == "PolicyPanel");
        if (_logParent != "PolicyPanel") LogLevelComboBox.SelectedItem = LogLevelComboBox.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, "all"));
    }

    private void LogFollow_Changed(object sender, RoutedEventArgs e)
    {
        if (_logView is null || LogFollowCheckBox.IsChecked != true) return;
        var latest = EventLogPresentation.LatestVisibleEntry(_logView);
        if (latest is not null) LogGrid.ScrollIntoView(latest);
    }

    private void UpdateLogViewInfo()
    {
        if (_logView is null) return;
        var count = _logView.Cast<object>().Count();
        var recovery = _logParent == "PolicyPanel";
        var total = recovery ? _eventLogStore.Entries.Count(entry => entry.IsRecoveryEvent) : _eventLogStore.Entries.Count;
        LogResetFilterButton.Content = _localizer.T(recovery ? "UiResetFilters" : "LogShowAll");
        LogCountText.Text = string.Format(_localizer.T("LogVisibleCount"), count, total);
        EventLogPresentation.UpdateEmptyActions(this, total, count);
        if (recovery) LogEmptyAction.Visibility = Visibility.Collapsed;
        LogEmptyText.Text = _localizer.T(total == 0 ? (recovery ? "NoRecoveryHistory" : "LogNoEntries") : "LogNoMatches");
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
