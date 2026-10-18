using NetGuardian.Core.Runtime;
using Xunit;

namespace NetGuardian.Tests;

public sealed class GuardianRuntimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NetGuardian-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 同一路径只允许一个监控控制者且退出后可接管()
    {
        using (var first = MonitoringLease.TryAcquire(_directory))
        {
            Assert.NotNull(first);
            Assert.Null(MonitoringLease.TryAcquire(_directory));
        }
        using var replacement = MonitoringLease.TryAcquire(_directory);
        Assert.NotNull(replacement);
    }

    [Fact]
    public void 心跳原子保存后能恢复进程身份和状态()
    {
        var status = new GuardianStatus(123, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Healthy", false);
        GuardianStatusStore.Write(_directory, status);
        Assert.Equal(status, GuardianStatusStore.Read(_directory));
    }

    [Fact]
    public void 心跳损坏时读取为空()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "guardian-status.json"), "{坏数据");
        Assert.Null(GuardianStatusStore.Read(_directory));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
