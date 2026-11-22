using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.App.Views;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class IncidentPresentationTests
{
    private static IncidentSummary Incident(string id, string ssid, IncidentOutcome outcome)
    {
        var at = new DateTimeOffset(2026, 11, 22, 14, 05, 00, TimeSpan.FromHours(8));
        return new(id, at, outcome == IncidentOutcome.Open ? null : at.AddSeconds(36), ssid, outcome,
            TimeSpan.FromSeconds(36), false, false, [new(id, at, IncidentStepKind.Failure, "前台", "session", ssid)]);
    }

    [Fact]
    public void 首页能读到原网络结果及可复算的时长()
    {
        var text = IncidentPresentation.HomeText(Incident("a", "实验室主网", IncidentOutcome.NaturalRecovery));
        Assert.Contains("实验室主网", text);
        Assert.Contains("自然恢复", text);
        Assert.Contains("36 秒", text);
        Assert.Contains("暂无", IncidentPresentation.HomeText(null));
    }

    [Fact]
    public void 最近事件与恢复历史使用故障标识定位且语言切换保留标识()
    {
        var incident = Incident("同一故障", "实验室", IncidentOutcome.CauseUnknown);
        var localizer = new NetGuardian.Core.Localization.Localizer();
        var zh = IncidentPresentation.HistoryEntry(incident, localizer);
        localizer.SetLanguage("en-US");
        var en = IncidentPresentation.HistoryEntry(incident, localizer);
        Assert.Equal(incident.Id, IncidentPresentation.LinkedIncident(zh));
        Assert.Equal(incident.Id, IncidentPresentation.LinkedIncident(en));
        Assert.Contains("原因未确认", zh.Summary);
        Assert.Contains("cause unknown", en.Summary);
    }

    [Fact]
    public void 首页钻取指定记录刷新仍保留选择并可展开原始证据()
    {
        DesktopFixture.Run(_ =>
        {
            var view = new IncidentTimelineView();
            var a = Incident("第一条", "甲", IncidentOutcome.Open);
            var b = Incident("第二条", "乙", IncidentOutcome.CauseUnknown);
            view.ShowIncidents([a, b], b.Id);
            Assert.Equal(b.Id, view.SelectedIncidentId);
            var raw = (Expander)view.FindName("RawDetails");
            Assert.False(raw.IsExpanded);
            raw.IsExpanded = true;
            view.ShowIncidents([a with { OriginalNetwork = "新网络" }, b]);
            Assert.Equal(b.Id, view.SelectedIncidentId);
            Assert.True(raw.IsExpanded);
            view.ShowIncidents([a, b], a.Id);
            Assert.Equal(a.Id, view.SelectedIncidentId);
            Assert.False(raw.IsExpanded);
        });
    }
}
