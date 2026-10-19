using NetGuardian.Core.Models;
using NetGuardian.Core.Recovery;
using Xunit;

namespace NetGuardian.Tests;

public sealed class RecoveryBudgetTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NetGuardian-budget-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 未确认两种设备恢复均执行过不得整机重启()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(ComputerRestartPlanner.ShouldRestart(true, NetworkState.AdapterFault, now.AddMinutes(-60), now, 30));
    }

    [Theory]
    [InlineData(NetworkState.Healthy)]
    [InlineData(NetworkState.InternetIssue)]
    public void 恢复或上游故障会清除已完成设备动作证据(NetworkState state)
    {
        var tracker = new DeviceFailureTracker();
        var now = DateTimeOffset.UtcNow;
        tracker.Record(NetworkState.AdapterFault, now);
        tracker.RecordRecoveryAttempt("WlanService");
        Assert.False(tracker.RecoveryAttemptsCompleted);
        tracker.RecordRecoveryAttempt("Adapter");
        Assert.True(tracker.RecoveryAttemptsCompleted);
        tracker.Record(state, now);
        Assert.False(tracker.RecoveryAttemptsCompleted);
        tracker.Record(NetworkState.AdapterFault, now);
        tracker.RecordRecoveryAttempt("Adapter");
        Assert.False(tracker.RecoveryAttemptsCompleted);
    }

    [Fact]
    public async Task 冷却跳过的动作不能计入整机重启前置条件()
    {
        var tracker = new DeviceFailureTracker();
        tracker.Record(NetworkState.AdapterFault, DateTimeOffset.UtcNow);
        var budget = new RecoveryBudget(_directory);
        Assert.True(budget.TryReserve("WlanService", DateTimeOffset.UtcNow, TimeSpan.FromHours(1)));
        var service = new BudgetedRecoveryService(new CompletedRecovery(), budget, () => TimeSpan.FromHours(1), tracker.RecordRecoveryAttempt);
        Assert.Equal(75, (await service.RestartWlanServiceAsync()).ExitCode);
        await service.RestartAdapterAsync("WLAN");
        Assert.False(tracker.RecoveryAttemptsCompleted);
    }

    private sealed class CompletedRecovery : IHighRiskRecoveryService
    {
        public Task<NetGuardian.Core.SystemCommands.SystemCommandResult> RestartWlanServiceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NetGuardian.Core.SystemCommands.SystemCommandResult(0, "已执行"));
        public Task<NetGuardian.Core.SystemCommands.SystemCommandResult> RestartAdapterAsync(string adapterName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NetGuardian.Core.SystemCommands.SystemCommandResult(0, "已执行"));
    }

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
    [InlineData(NetworkState.Healthy)]
    [InlineData(NetworkState.InternetIssue)]
    [InlineData(NetworkState.DnsIssue)]
    public void 设备恢复后立即清除重启计时而不等待下一轮(NetworkState finalState)
    {
        var tracker = new DeviceFailureTracker();
        var now = DateTimeOffset.UtcNow;
        tracker.Record(NetworkState.AdapterFault, now.AddMinutes(-31));
        Assert.NotNull(tracker.FailedSince);
        tracker.Record(finalState, now);
        Assert.Null(tracker.FailedSince);
        Assert.False(ComputerRestartPlanner.ShouldRestart(true, NetworkState.AdapterFault, tracker.FailedSince, now, 30));
    }

    [Theory]
    [InlineData(false, NetworkState.AdapterFault, 60, false)]
    [InlineData(true, NetworkState.InternetIssue, 60, false)]
    [InlineData(true, NetworkState.AdapterFault, 29, false)]
    [InlineData(true, NetworkState.AdapterFault, 30, true)]
    public void 整机重启仅适用于明确开启且持续的设备故障(bool enabled, NetworkState state, int minutes, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, ComputerRestartPlanner.ShouldRestart(enabled, state, now.AddMinutes(-minutes), now, 30, recoveryAttemptsCompleted: true));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
