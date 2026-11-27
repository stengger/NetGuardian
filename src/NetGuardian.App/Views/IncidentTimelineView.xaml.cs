using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Views;

public partial class IncidentTimelineView : UserControl
{
    private sealed record Row(IncidentSummary Incident, string Title);
    private IReadOnlyList<IncidentSummary>? _history;
    private QualityTrendSummary? _trend;
    private NetworkSlaSummary? _sla7;
    private NetworkSlaSummary? _sla30;
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
        HistoryHeading.Text = english ? "Incident history" : "历史故障";
        TimelineHeading.Text = english ? "Incident timeline" : "本次故障经过";
        RawDetails.Header = english ? "Raw records and probe evidence" : "原始记录与探针证据";
        ObservabilityHeading.Text = english ? "Availability & quality baseline" : "可用性与质量基线概览";
        Obs7dLabel.Text = english ? "7-day availability" : "近 7 天可用率";
        Obs30dLabel.Text = english ? "30-day availability" : "近 30 天可用率";
        ObsMttrLabel.Text = english ? "Mean recovery (MTTR)" : "平均恢复 (MTTR)";
        ObsLongestLabel.Text = english ? "Longest / recovery rate" : "最长中断 / 恢复率";
        ObsTrendHeading.Text = english ? "Recent HTTPS latency trend" : "近期网络延迟趋势 (HTTPS)";
        ObsWifiRankingHeading.Text = english ? "Wi-Fi stability ranking" : "Wi-Fi 稳定性排行";
        AutomationProperties.SetName(HistoryList, english ? "Incident time and outcome" : "故障时间与恢复结果");
        AutomationProperties.SetName(StepsList, english ? "Incident steps" : "故障步骤");
        AutomationProperties.SetName(RawText, english ? "Original records" : "原始故障记录");
        var history = _history;
        _history = null;
        if (history != null) ShowIncidents(history);
        RenderObservability();
    }

    public void ShowObservability(NetworkSlaSummary sla7, NetworkSlaSummary sla30, QualityTrendSummary trend)
    {
        _sla7 = sla7;
        _sla30 = sla30;
        _trend = trend;
        RenderObservability();
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

    private void RenderObservability()
    {
        var sla7 = _sla7 ?? NetworkObservabilityAnalyzer.ComputeSla(_history ?? [], 7);
        var sla30 = _sla30 ?? NetworkObservabilityAnalyzer.ComputeSla(_history ?? [], 30);
        Obs7dValue.Text = $"{sla7.AvailabilityPercent:F2}%";
        Obs30dValue.Text = $"{sla30.AvailabilityPercent:F2}%";
        ObsMttrValue.Text = sla7.MttrMinutes is { } mttr ? $"{mttr:F1} min" : "—";
        var longest = sla7.LongestOutageMinutes is { } lo ? $"{lo:F1} min" : "—";
        ObsLongestValue.Text = $"{longest} / {sla7.RecoveryRatePercent:F0}%";

        if (sla7.WifiStabilityRanking.Count == 0)
        {
            ObsWifiRankingText.Text = _english ? "No Wi-Fi outage records in the last 7 days" : "近 7 天暂无断网 Wi-Fi 统计";
        }
        else
        {
            ObsWifiRankingText.Text = string.Join(Environment.NewLine, sla7.WifiStabilityRanking.Take(3).Select((stat, idx) =>
            {
                var unit = _english ? "outages" : "次";
                var avgText = stat.AverageRecoveryMinutes is { } m ? $" · MTTR {m:F1}m" : "";
                return $"{idx + 1}. {stat.Ssid}: {stat.OutageCount} {unit} ({stat.RecoveredCount}/{stat.OutageCount}){avgText}";
            }));
        }

        if (_trend is null || _trend.SampleCount == 0)
        {
            ObsTrendMetricsText.Text = _english
                ? "No baseline samples yet; checks will populate the 7-day trend automatically"
                : "暂无质量基线样本，运行检测后将按节流自动记录 7 天趋势";
            LatencyTrendLine.Points = new PointCollection();
        }
        else
        {
            var p50 = _trend.P50HttpsMs is { } p50v ? $"{p50v:F0} ms" : "—";
            var p95 = _trend.P95HttpsMs is { } p95v ? $"{p95v:F0} ms" : "—";
            var sig = _trend.AverageSignalPercent is { } sv ? $"{sv:F0}%" : "—";
            ObsTrendMetricsText.Text = _english
                ? $"P50 {p50} · P95 {p95} · Avg Signal {sig} · {_trend.SampleCount} samples"
                : $"P50 {p50} · P95 {p95} · 平均信号 {sig} · 样本 {_trend.SampleCount}";
            PlotLatencyPolyline();
        }
    }

    private void LatencyTrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => PlotLatencyPolyline();

    private void PlotLatencyPolyline()
    {
        if (_trend is null || _trend.RecentPoints.Count == 0)
        {
            LatencyTrendLine.Points = new PointCollection();
            return;
        }

        var valid = _trend.RecentPoints.Where(p => p.HttpsAvgMs is { } ms && double.IsFinite(ms) && ms >= 0).ToList();
        if (valid.Count == 0)
        {
            LatencyTrendLine.Points = new PointCollection();
            return;
        }

        var width = LatencyTrendCanvas.ActualWidth > 20 ? LatencyTrendCanvas.ActualWidth : 280;
        var height = LatencyTrendCanvas.ActualHeight > 20 ? LatencyTrendCanvas.ActualHeight : 56;
        var maxMs = Math.Max(20.0, valid.Max(p => p.HttpsAvgMs!.Value));
        var pts = new PointCollection(valid.Count);
        for (var i = 0; i < valid.Count; i++)
        {
            var x = valid.Count == 1 ? width / 2.0 : (width - 8) * i / (valid.Count - 1) + 4;
            var ratio = valid[i].HttpsAvgMs!.Value / maxMs;
            var y = height - 4 - ratio * (height - 10);
            pts.Add(new Point(x, y));
        }
        LatencyTrendLine.Points = pts;
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
