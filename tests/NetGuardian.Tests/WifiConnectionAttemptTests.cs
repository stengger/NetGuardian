using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class WifiConnectionAttemptTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 超时或取消后必须等待系统收尾确认才能结束(bool cancel)
    {
        var attempt = new WifiConnectionAttempt(Guid.NewGuid(), "目标");
        var settling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var pending = attempt.WaitAsync(cancel ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(10), async () =>
        {
            settling.TrySetResult();
            await settled.Task;
        }, cancellation.Token);
        if (cancel) cancellation.Cancel();
        await settling.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(pending.IsCompleted);
        settled.SetResult();
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        else Assert.Null(await pending);
    }

    [Fact]
    public async Task 只接受当前接口与配置的连接完成通知()
    {
        var id = Guid.NewGuid();
        var attempt = new WifiConnectionAttempt(id, "中文 配置");
        attempt.Observe(8, 10, Guid.NewGuid(), "中文 配置", 0);
        attempt.Observe(8, 10, id, "其他配置", 0);
        attempt.Observe(4, 10, id, "中文 配置", 0);
        attempt.Observe(8, 7, id, "中文 配置", 0);
        Assert.False(attempt.Completion.IsCompleted);
        attempt.Observe(8, 10, id, "中文 配置", 0);
        Assert.True((await attempt.Completion).Connected);
    }

    [Theory]
    [InlineData(10u)]
    [InlineData(11u)]
    public async Task 系统最终失败保留原因且迟到成功不能覆盖(uint notification)
    {
        var id = Guid.NewGuid();
        var attempt = new WifiConnectionAttempt(id, "目标");
        attempt.Observe(8, notification, id, "目标", 163851);
        attempt.Observe(8, 10, id, "目标", 0);
        var result = await attempt.Completion;
        Assert.False(result.Connected);
        Assert.Equal(163851u, result.ReasonCode);
    }
}
