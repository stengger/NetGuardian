using System.IO;
using NetGuardian.App.Updates;
using NetGuardian.Core.Runtime;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class UpdateExitTests
{
    [Fact]
    public async Task 更新退出取消后保持收尾期间的真实租约且空闲后释放()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NetGuardian-ExitLease-" + Guid.NewGuid());
        using var lease = MonitoringLease.TryAcquire(directory);
        Assert.NotNull(lease);
        var checking = 1;
        using var cancellation = new CancellationTokenSource();
        var pending = ApplicationExitCoordinator.WaitForIdleAsync(() => Volatile.Read(ref checking) != 0,
            lease.Dispose, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        using (var blocked = MonitoringLease.TryAcquire(directory)) Assert.Null(blocked);
        Volatile.Write(ref checking, 0);
        MonitoringLease? next = null;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (next is null && DateTime.UtcNow < deadline)
            {
                next = MonitoringLease.TryAcquire(directory);
                if (next is null) await Task.Delay(10);
            }
            Assert.NotNull(next);
        }
        finally
        {
            next?.Dispose();
            lease.Dispose();
            File.Delete(Path.Combine(directory, "monitor.lock"));
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task 退出收尾不响应取消时仍可返回且迟到完成不会关闭界面()
    {
        var exit = new ApplicationExitCoordinator();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = false;
        using var cancellation = new CancellationTokenSource();
        var task = exit.ExitAsync(_ => pending.Task, () => closed = true, cancellation.Token);
        Assert.True(exit.IsExiting);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(exit.IsExiting);
        pending.SetResult();
        await Task.Yield();
        Assert.False(closed);
        await exit.ExitAsync(_ => Task.CompletedTask, () => closed = true, CancellationToken.None);
        Assert.True(closed);
    }

    [Fact]
    public async Task 退出异常可被调用者捕获且允许重试()
    {
        var exit = new ApplicationExitCoordinator();
        var closed = false;
        var error = await Assert.ThrowsAsync<IOException>(() => exit.ExitAsync(
            _ => Task.FromException(new IOException("收尾失败")), () => closed = true, CancellationToken.None));
        Assert.Equal("收尾失败", error.Message);
        Assert.False(exit.IsExiting);
        Assert.False(closed);
        await exit.ExitAsync(_ => Task.CompletedTask, () => closed = true, CancellationToken.None);
        Assert.True(closed);
    }

    [Fact]
    public async Task 重复退出被拒绝且不改变正在执行的退出()
    {
        var exit = new ApplicationExitCoordinator();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closes = 0;
        var first = exit.ExitAsync(_ => pending.Task, () => closes++, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => exit.ExitAsync(
            _ => Task.CompletedTask, () => closes++, CancellationToken.None));
        Assert.True(exit.IsExiting);
        pending.SetResult();
        await first;
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task 更新退出超时会取消收尾且迟到完成不能关闭界面()
    {
        var exit = new ApplicationExitCoordinator();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = false;
        var failure = await Assert.ThrowsAsync<TimeoutException>(() => UpdateExitCoordinator.WaitAsync(
            token => exit.ExitAsync(_ => pending.Task, () => closed = true, token),
            _ => Task.CompletedTask, CancellationToken.None, TimeSpan.FromMilliseconds(50)));
        Assert.Contains("退出", failure.Message);
        pending.SetResult();
        await Task.Delay(50);
        Assert.False(closed);
        Assert.False(exit.IsExiting);
    }

    [Fact]
    public async Task 交接期间脚本失败会取消退出并传回原始错误()
    {
        var exit = new ApplicationExitCoordinator();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = false;
        var checks = 0;
        var error = await Assert.ThrowsAsync<IOException>(() => UpdateExitCoordinator.WaitAsync(
            token => exit.ExitAsync(_ => pending.Task, () => closed = true, token),
            _ => ++checks < 2 ? Task.CompletedTask : Task.FromException(new IOException("协调脚本退出失败")),
            CancellationToken.None, TimeSpan.FromSeconds(2)));
        Assert.Equal("协调脚本退出失败", error.Message);
        pending.SetResult();
        await Task.Delay(50);
        Assert.False(closed);
        Assert.False(exit.IsExiting);
    }
}
