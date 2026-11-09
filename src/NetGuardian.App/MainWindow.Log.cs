using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using NetGuardian.App.Presentation;

namespace NetGuardian.App;

public partial class MainWindow
{
    private readonly EventLogStore _eventLogStore;
    private ICollectionView? _logView;

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
        var line = $"{entry.TimeText}  {message}";
        _recentEvents.Insert(0, line);
        while (_recentEvents.Count > 6) _recentEvents.RemoveAt(_recentEvents.Count - 1);
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
        LogScopeText.Text = string.Format(_localizer.T("LogSessionScope"), MaxLogLines);
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
