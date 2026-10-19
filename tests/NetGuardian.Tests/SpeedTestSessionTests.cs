using NetGuardian.Core.Network;
using Xunit;

namespace NetGuardian.Tests;

public sealed class SpeedTestSessionTests
{
    [Fact]
    public void 新一轮清空数值且忽略旧轮次回调()
    {
        var session = new SpeedTestSession();
        var first = session.Begin();
        session.Complete(first, new(120, 30, 90, 5_000_000, 1_000_000, DateTimeOffset.Now));
        var second = session.Begin();
        session.Apply(first, new("Upload", 999, null, true));
        session.Complete(first, new(999, 999, 999, 0, 0, DateTimeOffset.Now));
        session.Cancel(first, networkChanged: true);
        Assert.True(session.IsRunning);
        Assert.Null(session.Download.Value);
        Assert.Null(session.Upload.Value);
        Assert.Null(session.Latency.Value);
        Assert.NotEqual(first, second);
        Assert.False(session.NetworkChanged);
    }

    [Fact]
    public void 上传失败保留下载但不保留未完成上传数字()
    {
        var session = new SpeedTestSession();
        var id = session.Begin();
        session.Apply(id, new("Download", 80, null, true));
        session.Apply(id, new("Upload", 12, null));
        session.Fail(id, "连接中断");
        Assert.Equal(SpeedTestStatus.Partial, session.Status);
        Assert.Equal(80, session.Download.Value);
        Assert.Equal(SpeedMetricState.Complete, session.Download.State);
        Assert.Null(session.Upload.Value);
        Assert.Equal(SpeedMetricState.Failed, session.Upload.State);
        Assert.Equal("连接中断", session.FailureDetail);
    }

    [Fact]
    public void 取消保留已完成结果并忽略排队的进度()
    {
        var session = new SpeedTestSession();
        var id = session.Begin();
        session.Apply(id, new("Download", 80, null, true));
        session.Apply(id, new("Upload", null, null));
        session.Cancel(id);
        session.Apply(id, new("Upload", 20, null, true));
        Assert.Equal(SpeedTestStatus.Cancelled, session.Status);
        Assert.Equal(80, session.Download.Value);
        Assert.Null(session.Upload.Value);
        Assert.Equal(SpeedMetricState.Cancelled, session.Upload.State);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void 无响应样本的完成结果标记为部分完成()
    {
        var session = new SpeedTestSession();
        var id = session.Begin();
        session.Complete(id, new(100, 25, null, 5_000_000, 1_000_000, DateTimeOffset.Now));
        Assert.Equal(SpeedTestStatus.Partial, session.Status);
        Assert.Equal(SpeedMetricState.Unavailable, session.Latency.State);
        Assert.Equal(100, session.Download.Value);
        Assert.Equal(25, session.Upload.Value);
    }

    [Fact]
    public void 网络变化取消与手动取消可以区分()
    {
        var session = new SpeedTestSession();
        var id = session.Begin();
        session.Cancel(id, networkChanged: true);
        Assert.True(session.NetworkChanged);
        Assert.Equal(SpeedTestStatus.Cancelled, session.Status);
        Assert.False(session.CanCopy);
    }
}
