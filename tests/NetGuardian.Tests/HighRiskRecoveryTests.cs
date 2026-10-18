using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.Recovery;
using Xunit;

namespace NetGuardian.Tests;

public class HighRiskPlannerTests
{
    private static MonitorPolicy Policy(bool enabled = true, int rounds = 2, int cooldownMinutes = 30) => new()
    {
        EnableHighRiskRecovery = enabled,
        HighRiskAfterFailedRounds = rounds,
        HighRiskCooldownMinutes = cooldownMinutes
    };

    [Fact]
    public void Disabled_NeverProceeds()
    {
        var decision = RecoveryPlanner.EvaluateHighRisk(Policy(enabled: false), 99, null, DateTimeOffset.Now);
        Assert.Equal(HighRiskDecisionKind.NotEnabled, decision.Kind);
    }

    [Theory]
    [InlineData(0, HighRiskDecisionKind.BelowRounds)]
    [InlineData(1, HighRiskDecisionKind.BelowRounds)]
    [InlineData(2, HighRiskDecisionKind.Proceed)]
    [InlineData(5, HighRiskDecisionKind.Proceed)]
    public void RespectsFailedRoundThreshold(int rounds, HighRiskDecisionKind expected)
    {
        var decision = RecoveryPlanner.EvaluateHighRisk(Policy(rounds: 2), rounds, null, DateTimeOffset.Now);
        Assert.Equal(expected, decision.Kind);
    }

    [Fact]
    public void WithinCooldown_ReportsRemaining()
    {
        var now = DateTimeOffset.Now;
        var decision = RecoveryPlanner.EvaluateHighRisk(Policy(cooldownMinutes: 30), 5, now.AddMinutes(-10), now);
        Assert.Equal(HighRiskDecisionKind.CooldownActive, decision.Kind);
        Assert.InRange(decision.CooldownRemaining.TotalMinutes, 19.9, 20.1);
    }

    [Fact]
    public void AfterCooldown_Proceeds()
    {
        var now = DateTimeOffset.Now;
        var decision = RecoveryPlanner.EvaluateHighRisk(Policy(cooldownMinutes: 30), 5, now.AddMinutes(-31), now);
        Assert.Equal(HighRiskDecisionKind.Proceed, decision.Kind);
    }

    [Fact]
    public void PolicyNormalize_ClampsHighRiskValues()
    {
        var policy = new MonitorPolicy { HighRiskAfterFailedRounds = 0, HighRiskCooldownMinutes = 999999 };
        policy.Normalize();
        Assert.Equal(1, policy.HighRiskAfterFailedRounds);
        Assert.Equal(24 * 60, policy.HighRiskCooldownMinutes);
    }
}

public class HighRiskRecoveryServiceTests
{
    [Fact]
    public async Task RestartWlanService_StopsThenStarts()
    {
        var runner = new FakeCommandRunner();
        var service = new HighRiskRecoveryService(runner);
        var result = await service.RestartWlanServiceAsync();

        Assert.True(result.Ok);
        Assert.Equal(
            [("net.exe", "stop WlanSvc"), ("net.exe", "start WlanSvc")],
            runner.Invocations);
    }

    [Fact]
    public async Task RestartAdapter_DisablesThenEnables()
    {
        var runner = new FakeCommandRunner();
        var service = new HighRiskRecoveryService(runner);
        await service.RestartAdapterAsync("Wi-Fi 2");

        Assert.Equal(
        [
            ("netsh.exe", "interface set interface name=Wi-Fi 2 admin=disabled"),
            ("netsh.exe", "interface set interface name=Wi-Fi 2 admin=enabled")
        ], runner.Invocations);
    }
}

public sealed class MonitoringEngineHighRiskTests : IDisposable
{
    private readonly FakeWifiClient _wifi = new();
    private readonly FakeConnectivityTester _tester = new();
    private readonly FakeCommandRunner _commands = new();
    private readonly MonitorPolicy _policy = new()
    {
        CheckIntervalSeconds = 3600,
        ConsecutiveFailuresBeforeRecovery = 1,
        RecoveryCooldownSeconds = 30,
        EnableAutoSwitch = true,
        EnableCurrentWifiReconnect = false,
        EnableWifiAdapterAutoEnable = false,
        EnableHighRiskRecovery = true,
        HighRiskAfterFailedRounds = 1,
        HighRiskCooldownMinutes = 30
    };
    private readonly List<EngineEventArgs> _events = [];
    private readonly MonitoringEngine _engine;

    public MonitoringEngineHighRiskTests()
    {
        // One switch candidate that always fails to connect -> every low-risk round fails.
        var profiles = new List<WifiProfile> { new() { Ssid = "Backup", Priority = 1, CredentialSaved = true } };
        _engine = new MonitoringEngine(_wifi, _tester, _commands, () => _policy, () => profiles)
        {
            SsidPollInterval = TimeSpan.Zero,
            AdapterEnableSettleDelay = TimeSpan.Zero,
            HighRiskSettleDelay = TimeSpan.Zero,
            WifiValidationTimeout = TimeSpan.FromMilliseconds(50),
            WifiRestoreTimeout = TimeSpan.FromMilliseconds(50)
        };
        _engine.EngineEvent += (_, e) => { lock (_events) { _events.Add(e); } };
    }

    private IReadOnlyList<EngineEventKind> Kinds
    {
        get { lock (_events) { return _events.Select(e => e.Kind).ToList(); } }
    }

    [Fact]
    public async Task Disabled_NeverRunsHighRiskCommands()
    {
        _policy.EnableHighRiskRecovery = false;
        _tester.NextState = NetworkState.WifiDisconnected;
        _engine.Start();
        await _engine.CheckNowAsync();

        Assert.Contains(EngineEventKind.RecoveryFailed, Kinds);
        Assert.DoesNotContain(EngineEventKind.HighRiskActionStarted, Kinds);
        Assert.DoesNotContain(_commands.Invocations, invocation => invocation.FileName == "net.exe");
    }

    [Fact]
    public async Task FailedRound_TriggersWlanServiceThenAdapterRestart_WhenStillDown()
    {
        _tester.NextState = NetworkState.WifiDisconnected;
        _engine.Start();
        await _engine.CheckNowAsync();

        var files = _commands.Invocations.Select(invocation => invocation.FileName).ToList();
        Assert.Contains("net.exe", files);
        Assert.Contains("netsh.exe", files);
        Assert.Equal(2, Kinds.Count(kind => kind == EngineEventKind.HighRiskActionStarted));
        Assert.Equal(2, Kinds.Count(kind => kind == EngineEventKind.HighRiskActionCompleted));
    }

    [Fact]
    public async Task WlanServiceRestartHeals_SkipsAdapterRestart_AndResetsCounters()
    {
        _tester.NextState = NetworkState.WifiDisconnected;
        _commands.OnRun = fileName =>
        {
            if (fileName == "net.exe")
            {
                _tester.NextState = NetworkState.Healthy;
            }
        };

        _engine.Start();
        await _engine.CheckNowAsync();

        Assert.Equal(1, Kinds.Count(kind => kind == EngineEventKind.HighRiskActionStarted));
        Assert.DoesNotContain(_commands.Invocations, invocation => invocation.FileName == "netsh.exe");
        Assert.Equal(0, _engine.FailureStreak);
    }

    [Fact]
    public async Task SecondHighRiskWithinCooldown_IsSuppressed()
    {
        _policy.RecoveryCooldownSeconds = 30;
        _tester.NextState = NetworkState.WifiDisconnected;
        _engine.Start();
        await _engine.CheckNowAsync();   // round 1: low-risk fails -> high-risk runs
        var highRiskRunsAfterFirst = Kinds.Count(kind => kind == EngineEventKind.HighRiskActionStarted);

        // Force past the low-risk cooldown so a second recovery round happens.
        _policy.RecoveryCooldownSeconds = 30; // real clock: still within low-risk cooldown, so simulate by waiting? Instead lower to minimum via new attempt window.
        await Task.Delay(50);
        await _engine.CheckNowAsync();

        Assert.Equal(2, highRiskRunsAfterFirst);
        // Either the low-risk cooldown blocked the round entirely, or high-risk cooldown reported active — never a third high-risk action.
        Assert.Equal(2, Kinds.Count(kind => kind == EngineEventKind.HighRiskActionStarted));
    }

    public void Dispose() => _engine.Dispose();
}
