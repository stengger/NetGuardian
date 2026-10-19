using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class QualityCheckSessionTests
{
    [Fact]
    public void 新检查清除旧结果且取消后的迟到成功不能覆盖取消()
    {
        var state = new QualityCheckSession();
        var first = state.Begin();
        state.Complete(first, new ConnectivityReport { State = NetworkState.Healthy });
        Assert.NotNull(state.Report);
        var second = state.Begin();
        Assert.Null(state.Report);
        state.RequestCancel();
        state.Complete(second, new ConnectivityReport { State = NetworkState.Healthy });
        state.Cancel(second);
        Assert.Equal(QualityCheckPhase.Cancelled, state.Phase);
        Assert.Null(state.Report);
        Assert.False(state.IsBusy);
    }

    [Fact]
    public void 旧轮次和重复开始不能覆盖当前检查状态()
    {
        var state = new QualityCheckSession();
        var first = state.Begin();
        Assert.Throws<InvalidOperationException>(() => state.Begin());
        state.Fail(first, "读取接口失败");
        var second = state.Begin();
        state.Complete(first, new ConnectivityReport());
        Assert.Equal(QualityCheckPhase.Running, state.Phase);
        state.Fail(second, "访问被拒绝");
        Assert.Equal(QualityCheckPhase.Failed, state.Phase);
        Assert.Equal("访问被拒绝", state.Error);
    }
}
