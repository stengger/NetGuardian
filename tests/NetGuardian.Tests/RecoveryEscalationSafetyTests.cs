using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.SystemCommands;
using Xunit;

namespace NetGuardian.Tests;

public sealed class RecoveryEscalationSafetyTests
{
    private static MonitorPolicy Policy(bool autoSwitch = false) => new()
    {
        CheckIntervalSeconds = 3600, ConsecutiveFailuresBeforeRecovery = 1, RecoveryCooldownSeconds = 0,
        EnableWifiAdapterAutoEnable = false, EnableCurrentWifiReconnect = false, EnableAutoSwitch = autoSwitch,
        EnableHighRiskRecovery = true, HighRiskAfterFailedRounds = 1
    };

    private static MonitoringEngine Engine(FakeWifiClient wifi, ScriptedTester tester, ISystemCommandRunner commands,
        bool autoSwitch = false) => new(wifi, tester, commands, () => Policy(autoSwitch),
            () => new[] { new WifiProfile { Ssid = "Backup", CredentialSaved = true } })
    {
        HighRiskSettleDelay = TimeSpan.Zero, SsidPollInterval = TimeSpan.Zero
    };

    [Fact]
    public async Task StopThenStart_HoldsCheckUntilWlanCleanupFinishes()
    {
        var runner = new CleanupRunner();
        var probes = 0;
        using var engine = Engine(new(), new((ssid, _) =>
        {
            probes++;
            return Task.FromResult(Report(ssid, NetworkState.WifiDisconnected));
        }), runner);
        engine.Start();
        var check = engine.CheckNowAsync();
        await runner.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        engine.Stop();
        await runner.CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await Task.WhenAny(check, Task.Delay(100));
            Assert.False(check.IsCompleted);
            Assert.True(engine.IsChecking);
            var probesBeforeRestart = probes;
            engine.Start();
            await engine.CheckNowAsync();
            Assert.Equal(probesBeforeRestart, probes);
            Assert.Equal(1, runner.StopCalls);
        }
        finally
        {
            runner.AllowCleanup.TrySetResult();
            await check.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.False(engine.IsChecking);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WlanVerificationUnknown_DoesNotEscalateToAdapter(bool timeout)
    {
        var runner = new FakeCommandRunner();
        using var engine = Engine(new(), new(async (ssid, token) =>
        {
            if (runner.Invocations.Any(c => c.FileName == "net.exe"))
            {
                if (timeout) await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("WLAN 恢复后的验证未知");
            }
            return Report(ssid, NetworkState.WifiDisconnected);
        }), runner);
        engine.FullProbeTimeout = TimeSpan.FromMilliseconds(30);
        engine.Start();
        await engine.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains(runner.Invocations, c => c.FileName == "net.exe");
        Assert.DoesNotContain(runner.Invocations, c => c.FileName == "netsh.exe");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidateAssociationReplacesInitialDeviceFault_BeforeEscalation(bool probeUnknown)
    {
        var wifi = new FakeWifiClient();
        wifi.ConnectSucceedsFor.Add("Backup");
        var runner = new FakeCommandRunner();
        using var engine = Engine(wifi, new((ssid, _) => ssid is not null && probeUnknown
            ? throw new InvalidOperationException("候选网络验证未知")
            : Task.FromResult(Report(ssid, ssid is null ? NetworkState.WifiDisconnected : NetworkState.InternetIssue))), runner, true);
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal(new[] { "Backup" }, wifi.ConnectAttempts);
        Assert.Empty(runner.Invocations);
    }

    private static ConnectivityReport Report(string? ssid, NetworkState state) => new() { CurrentSsid = ssid, State = state };

    private sealed class CleanupRunner : ISystemCommandRunner
    {
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCalls { get; private set; }

        public async Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            if (fileName == "net.exe" && arguments[0] == "stop")
            {
                StopCalls++;
                StopEntered.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            if (fileName == "net.exe" && arguments[0] == "start")
            {
                CleanupEntered.TrySetResult();
                await AllowCleanup.Task;
            }
            return new SystemCommandResult(0, "模拟必要收尾完成");
        }
    }
}
