using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class ConnectivityReportSelectionTests
{
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
