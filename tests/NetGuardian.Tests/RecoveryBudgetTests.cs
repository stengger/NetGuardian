using NetGuardian.Core.Models;
using NetGuardian.Core.Recovery;
using Xunit;

namespace NetGuardian.Tests;

public sealed class RecoveryBudgetTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NetGuardian-budget-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 进程重新创建不能绕过持久化动作冷却()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(new RecoveryBudget(_directory).TryReserve("Adapter", now, TimeSpan.FromMinutes(30)));
        Assert.False(new RecoveryBudget(_directory).TryReserve("Adapter", now.AddMinutes(1), TimeSpan.FromMinutes(30)));
        Assert.True(new RecoveryBudget(_directory).TryReserve("Adapter", now.AddMinutes(31), TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void 记录损坏时禁止破坏性动作()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "recovery-budget.json"), "{坏数据");
        Assert.False(new RecoveryBudget(_directory).TryReserve("ComputerRestart", DateTimeOffset.UtcNow, TimeSpan.FromHours(24)));
    }

    [Theory]
    [InlineData(false, NetworkState.AdapterFault, 60, false)]
    [InlineData(true, NetworkState.InternetIssue, 60, false)]
    [InlineData(true, NetworkState.AdapterFault, 29, false)]
    [InlineData(true, NetworkState.AdapterFault, 30, true)]
    public void 整机重启仅适用于明确开启且持续的设备故障(bool enabled, NetworkState state, int minutes, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, ComputerRestartPlanner.ShouldRestart(enabled, state, now.AddMinutes(-minutes), now, 30));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
