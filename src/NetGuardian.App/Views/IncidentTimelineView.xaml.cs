using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Views;

public partial class IncidentTimelineView : UserControl
{
    private sealed record Row(IncidentSummary Incident, string Title);
    private IReadOnlyList<IncidentSummary>? _history;
    private bool _refreshing;
    public string? SelectedIncidentId => (HistoryList.SelectedItem as Row)?.Incident.Id;
    public event RoutedEventHandler? OpenAllLogsRequested;

    public IncidentTimelineView()
    {
        InitializeComponent();
        // 与页外的“全部日志”按钮共享操作；保留键盘直接打开日志的入口。
        var command = new RoutedUICommand("全部日志", "AllLogs", typeof(IncidentTimelineView));
        InputBindings.Add(new KeyBinding(command, Key.L, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(command, (_, e) => OpenAllLogsRequested?.Invoke(this, e)));
    }

    private bool _english;
    public void SetLanguage(bool english)
    {
        if (english == _english) return;
        _english = english;
        EmptyText.Text = english ? "No incidents recorded" : "暂无故障记录";
        RawDetails.Header = english ? "Raw records and probe evidence" : "原始记录与探针证据";
        AutomationProperties.SetName(HistoryList, english ? "Incident time and outcome" : "故障时间与恢复结果");
        AutomationProperties.SetName(StepsList, english ? "Incident steps" : "故障步骤");
        AutomationProperties.SetName(RawText, english ? "Original records" : "原始故障记录");
        var history = _history;
        _history = null;
        if (history != null) ShowIncidents(history);
    }

    public void ShowIncidents(IReadOnlyList<IncidentSummary> incidents, string? selectId = null)
    {
        if (ReferenceEquals(incidents, _history) && (selectId is null || selectId == SelectedIncidentId)) return;
        var previous = SelectedIncidentId;
        var wanted = selectId ?? previous;
        _refreshing = true;
        try
        {
            if (!ReferenceEquals(incidents, _history))
            {
                _history = incidents;
                HistoryList.ItemsSource = incidents.Select(i => new Row(i, IncidentPresentation.HomeText(i, _english))).ToArray();
            }
            var rows = HistoryList.Items.Cast<Row>();
            HistoryList.SelectedItem = rows.FirstOrDefault(r => r.Incident.Id == wanted) ?? rows.FirstOrDefault();
            HistoryDetails.Visibility = incidents.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            EmptyText.Visibility = incidents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (selectId != null && HistoryList.SelectedItem != null) HistoryList.ScrollIntoView(HistoryList.SelectedItem);
        }
        finally { _refreshing = false; }
        Render(previous != SelectedIncidentId);
    }

    private void HistorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) Render(true);
    }

    private void Render(bool resetRaw)
    {
        if (resetRaw) { RawDetails.IsExpanded = false; RawText.Clear(); }
        var incident = (HistoryList.SelectedItem as Row)?.Incident;
        CountsText.Text = incident is null ? "" : IncidentPresentation.Counts(incident, _english);
        StepsList.ItemsSource = incident?.Steps.Select(s => IncidentPresentation.Describe(s, _english));
        if (RawDetails.IsExpanded) ShowRaw();
    }

    private void RawExpanded(object sender, RoutedEventArgs e) => ShowRaw();
    private void ShowRaw()
    {
        var steps = (HistoryList.SelectedItem as Row)?.Incident.Steps;
        RawText.Text = steps is null ? "" : string.Join(Environment.NewLine, steps.Select(s => s.RawLog).Where(line => line != null));
    }
}
