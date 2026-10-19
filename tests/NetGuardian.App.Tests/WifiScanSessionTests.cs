using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class WifiScanSessionTests
{
    [Fact]
    public async Task 两个入口不能并发扫描且取消后丢弃迟到结果()
    {
        var session = new WifiScanSession();
        var pending = new TaskCompletionSource<IReadOnlyList<WifiNetwork>>();
        var calls = 0;
        var first = session.RunAsync(_ => { calls++; return pending.Task; }, () => { });
        await session.RunAsync(_ => { calls++; return Task.FromResult<IReadOnlyList<WifiNetwork>>([]); }, () => { });
        Assert.Equal(1, calls);
        session.Cancel();
        pending.SetResult([]);
        await first;
        Assert.Equal(WifiScanPhase.Cancelled, session.Phase);
        Assert.Null(session.Networks);
    }

    [Fact]
    public async Task 失败清空旧结果并允许重试且开始通知先于服务调用()
    {
        var session = new WifiScanSession();
        var notified = false;
        await session.RunAsync(_ =>
        {
            Assert.True(notified);
            return Task.FromResult<IReadOnlyList<WifiNetwork>>([]);
        }, () => notified = true);
        Assert.Equal(WifiScanPhase.Complete, session.Phase);
        await session.RunAsync(_ => throw new InvalidOperationException("扫描服务失败"), () => { });
        Assert.Equal(WifiScanPhase.Failed, session.Phase);
        Assert.Null(session.Networks);
        Assert.Equal("扫描服务失败", session.Error);
        await session.RunAsync(_ => Task.FromResult<IReadOnlyList<WifiNetwork>>([]), () => { });
        Assert.Equal(WifiScanPhase.Complete, session.Phase);
        Assert.Null(session.Error);
    }
}
