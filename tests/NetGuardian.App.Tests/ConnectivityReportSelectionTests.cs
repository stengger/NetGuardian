using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class ConnectivityReportSelectionTests
{
    [Theory]
    [InlineData(true, "轻量检测")]
    [InlineData(false, "完整诊断")]
    public void 报告上下文明确检测类型及前后台来源(bool lightweight, string expected)
    {
        var report = new ConnectivityReport { IsLightweight = lightweight };
        var text = ConnectivityReportPresentation.Context(report, ConnectivityReportOrigin.Background, new Localizer());
        Assert.Contains(expected, text);
        Assert.Contains("后台", text);
        Assert.Contains(report.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), text);
    }

    [Fact]
    public void 新轻量报告不混入上次完整诊断的探针()
    {
        var selection = new ConnectivityReportSelection();
        var now = DateTimeOffset.Now;
        selection.TryAccept(new ConnectivityReport { CheckedAt = now, Probes = [new("Gateway ping", true, 1, "旧诊断")] }, ConnectivityReportOrigin.Foreground);
        selection.TryAccept(new ConnectivityReport { CheckedAt = now.AddSeconds(90), IsLightweight = true, Probes = [new("HTTPS example.com", true, 2, "HTTP 200")] }, ConnectivityReportOrigin.Background);
        Assert.True(selection.Report!.IsLightweight);
        Assert.Single(selection.Report.Probes);
        Assert.Equal("HTTPS example.com", selection.Report.Probes[0].Name);
    }

    [Fact]
    public void 旧后台心跳不能覆盖刚完成的手动检查但更新的报告可以()
    {
        var now = DateTimeOffset.Now;
        var state = new ConnectivityReportSelection();
        var old = new ConnectivityReport { CheckedAt = now.AddMinutes(-1), State = NetworkState.Healthy };
        var manual = new ConnectivityReport { CheckedAt = now, State = NetworkState.InternetIssue };
        Assert.True(state.TryAccept(old, ConnectivityReportOrigin.Background));
        Assert.True(state.TryAccept(manual, ConnectivityReportOrigin.Manual));
        Assert.False(state.TryAccept(old, ConnectivityReportOrigin.Background));
        Assert.Same(manual, state.Report);
        Assert.Equal(ConnectivityReportOrigin.Manual, state.Origin);
        Assert.False(state.TryAccept(new() { CheckedAt = now, State = NetworkState.Healthy }, ConnectivityReportOrigin.Background));
        Assert.True(state.TryAccept(new() { CheckedAt = now.AddSeconds(1) }, ConnectivityReportOrigin.Background));
        Assert.Equal(ConnectivityReportOrigin.Background, state.Origin);
    }
}
