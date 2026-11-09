using NetGuardian.Core.Runtime;
using Xunit;

namespace NetGuardian.Tests;

public sealed class ManualNetworkAccessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NetGuardian-control-" + Guid.NewGuid().ToString("N"));

    private void WriteStatus(int version = ManualControlRequestStore.ProtocolVersion)
        => GuardianStatusStore.Write(_directory, new(1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Running", false,
            ControlProtocolVersion: version));

    [Fact]
    public async Task 无后台时直接获得独占且另一窗口不能并发修改网络()
    {
        using (await ManualNetworkAccess.AcquireAsync(_directory))
        {
            Assert.Null(MonitoringLease.TryAcquire(_directory));
            var error = await Assert.ThrowsAsync<ManualControlException>(() => ManualNetworkAccess.AcquireAsync(_directory));
            Assert.Equal(ManualControlFailure.Busy, error.Reason);
            Assert.Null(ManualControlRequestStore.Read(_directory, DateTimeOffset.UtcNow));
        }
        using var replacement = MonitoringLease.TryAcquire(_directory);
        Assert.NotNull(replacement);
    }

    [Fact]
    public async Task 后台等待恢复收尾再交权且手动操作结束后恢复守护()
    {
        var checking = true;
        var stops = 0;
        var resumes = 0;
        using var guardian = new GuardianControlSession(_directory, MonitoringLease.TryAcquire(_directory)!,
            () => stops++, () => checking, () => resumes++);
        WriteStatus();
        var pending = ManualNetworkAccess.AcquireAsync(_directory);
        guardian.Poll(DateTimeOffset.UtcNow);
        Assert.True(guardian.IsManualControl);
        Assert.Equal(1, stops);
        Assert.False(pending.IsCompleted);
        Assert.Null(MonitoringLease.TryAcquire(_directory));
        checking = false;
        guardian.Poll(DateTimeOffset.UtcNow);
        using (await pending.WaitAsync(TimeSpan.FromSeconds(3)))
        {
            Assert.Null(MonitoringLease.TryAcquire(_directory));
            guardian.Poll(DateTimeOffset.UtcNow);
            Assert.Equal(0, resumes);
        }
        guardian.Poll(DateTimeOffset.UtcNow);
        Assert.False(guardian.IsManualControl);
        Assert.Equal(1, resumes);
        Assert.Null(MonitoringLease.TryAcquire(_directory));
        guardian.Poll(DateTimeOffset.UtcNow);
        Assert.Equal(1, resumes);
    }

    [Fact]
    public async Task 取消等待会撤销请求且恢复后台而不释放仍在恢复的锁()
    {
        var checking = true;
        var resumes = 0;
        using var guardian = new GuardianControlSession(_directory, MonitoringLease.TryAcquire(_directory)!,
            () => { }, () => checking, () => resumes++);
        WriteStatus();
        using var cancellation = new CancellationTokenSource();
        var pending = ManualNetworkAccess.AcquireAsync(_directory, cancellation.Token);
        guardian.Poll(DateTimeOffset.UtcNow);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(ManualControlRequestStore.Read(_directory, DateTimeOffset.UtcNow));
        guardian.Poll(DateTimeOffset.UtcNow);
        Assert.True(guardian.IsManualControl);
        Assert.Equal(0, resumes);
        checking = false;
        guardian.Poll(DateTimeOffset.UtcNow);
        Assert.Equal(1, resumes);
    }

    [Fact]
    public async Task 后台不响应时超时撤销请求且下一次操作仍可重试()
    {
        using (MonitoringLease.TryAcquire(_directory))
        {
            WriteStatus();
            var error = await Assert.ThrowsAsync<ManualControlException>(() =>
                ManualNetworkAccess.AcquireAsync(_directory, timeout: TimeSpan.FromMilliseconds(30)));
            Assert.Equal(ManualControlFailure.TimedOut, error.Reason);
            Assert.Null(ManualControlRequestStore.Read(_directory, DateTimeOffset.UtcNow));
        }
        using var retry = await ManualNetworkAccess.AcquireAsync(_directory);
    }

    [Theory]
    [InlineData(true, ManualControlFailure.UpgradeRequired)]
    [InlineData(false, ManualControlFailure.Unavailable)]
    public async Task 旧后台或未知控制者返回可解释原因并保留原控制锁(bool oldBackground, ManualControlFailure expected)
    {
        using var owner = MonitoringLease.TryAcquire(_directory);
        if (oldBackground) WriteStatus(0);
        var failure = await Assert.ThrowsAsync<ManualControlException>(() => ManualNetworkAccess.AcquireAsync(_directory));
        Assert.Equal(expected, failure.Reason);
        Assert.Null(MonitoringLease.TryAcquire(_directory));
        Assert.Null(ManualControlRequestStore.Read(_directory, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void 请求过期后自动恢复但仍被手动锁占用时不得抢占()
    {
        var now = DateTimeOffset.UtcNow;
        var resumes = 0;
        using var guardian = new GuardianControlSession(_directory, MonitoringLease.TryAcquire(_directory)!,
            () => { }, () => false, () => resumes++);
        ManualControlRequestStore.Write(_directory, new(Guid.NewGuid(), now.AddSeconds(20)));
        guardian.Poll(now);
        Assert.True(guardian.IsManualControl);
        using (var manual = MonitoringLease.TryAcquire(_directory))
        {
            Assert.NotNull(manual);
            guardian.Poll(now.AddSeconds(21));
            Assert.True(guardian.IsManualControl);
            Assert.Equal(0, resumes);
        }
        guardian.Poll(now.AddSeconds(21));
        Assert.False(guardian.IsManualControl);
        Assert.Equal(1, resumes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public void 过期或异常长效请求不能暂停后台(int seconds)
    {
        var now = DateTimeOffset.UtcNow;
        using var guardian = new GuardianControlSession(_directory, MonitoringLease.TryAcquire(_directory)!,
            () => Assert.Fail("不应暂停"), () => false, () => Assert.Fail("不应重启监控"));
        ManualControlRequestStore.Write(_directory, new(Guid.NewGuid(), now.AddSeconds(seconds)));
        guardian.Poll(now);
        Assert.False(guardian.IsManualControl);
    }

    [Fact]
    public async Task 已取消请求不创建任何控制文件()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ManualNetworkAccess.AcquireAsync(_directory, cancellation.Token));
        Assert.False(Directory.Exists(_directory));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
