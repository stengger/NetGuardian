using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class UnattendedEngineTests
{
    [Fact]
    public async Task 地址就绪后的完整探测保留独立时限()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home" };
        wifi.ConnectSucceedsFor.UnionWith(["A", "B"]);
        using var engine = Engine(wifi, new(async (ssid, token) =>
        {
            if (ssid == "A") await Task.Delay(100, token);
            return Report(ssid, ssid is "A" or "B" ? NetworkState.Healthy : NetworkState.InternetIssue);
        }), Policy());
        engine.WifiValidationTimeout = TimeSpan.FromMilliseconds(30);
        engine.FullProbeTimeout = TimeSpan.FromSeconds(1);
        engine.Start();
        await engine.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { "A" }, wifi.ConnectAttempts);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public async Task 停止监控会立即取消等待地址配置而不继续换网()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wifi = new FakeWifiClient { CurrentSsid = "Home" };
        wifi.ConnectSucceedsFor.UnionWith(["A", "B"]);
        using var engine = Engine(wifi, new((ssid, _) =>
        {
            if (ssid == "A") entered.TrySetResult();
            return Task.FromResult(Report(ssid, ssid == "A" ? NetworkState.Disconnected : NetworkState.InternetIssue));
        }), Policy());
        engine.SsidPollInterval = TimeSpan.FromSeconds(10);
        engine.Start();
        var check = engine.CheckNowAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        engine.Stop();
        await check.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(engine.IsChecking);
        Assert.Equal(new[] { "A" }, wifi.ConnectAttempts);
    }

    [Fact]
    public async Task SSID已关联但IP尚未就绪时等待同一网络而不立即切走()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home" };
        wifi.ConnectSucceedsFor.UnionWith(["A", "B"]);
        var attempts = 0;
        var tester = new ScriptedTester((ssid, _) => Task.FromResult(Report(ssid, ssid switch
        {
            "A" => ++attempts == 1 ? NetworkState.Disconnected : NetworkState.Healthy,
            "B" => NetworkState.Healthy,
            _ => NetworkState.InternetIssue
        })));
        using var engine = Engine(wifi, tester, Policy());
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal(new[] { "A" }, wifi.ConnectAttempts);
        Assert.Equal(2, attempts);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public async Task 地址持续未就绪时单个候选超时后继续备用网络()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home" };
        wifi.ConnectSucceedsFor.UnionWith(["A", "B"]);
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(Report(ssid,
            ssid == "A" ? NetworkState.Disconnected : ssid == "B" ? NetworkState.Healthy : NetworkState.InternetIssue))), Policy());
        engine.WifiValidationTimeout = TimeSpan.FromMilliseconds(40);
        engine.Start();
        await engine.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { "A", "B" }, wifi.ConnectAttempts);
        Assert.Equal("B", wifi.CurrentSsid);
    }

    [Fact]
    public async Task 长期连接后的备用网络需要刷新扫描才能连接()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home" };
        wifi.ScanHandler = _ =>
        {
            wifi.ConnectSucceedsFor.Add("B");
            return Task.FromResult<IReadOnlyList<WifiNetwork>>([]);
        };
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(Report(ssid,
            ssid == "B" ? NetworkState.Healthy : NetworkState.InternetIssue))), Policy());
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal("B", wifi.CurrentSsid);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public async Task 扫描失败仍继续尝试保存的备用配置()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home", ScanHandler = _ => throw new InvalidOperationException("扫描暂不可用") };
        wifi.ConnectSucceedsFor.Add("B");
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(Report(ssid,
            ssid == "B" ? NetworkState.Healthy : NetworkState.InternetIssue))), Policy());
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal("B", wifi.CurrentSsid);
    }

    private static MonitorPolicy Policy() => new()
    {
        CheckIntervalSeconds = 3600, ConsecutiveFailuresBeforeRecovery = 1,
        RecoveryCooldownSeconds = 0, EnableCurrentWifiReconnect = false,
        EnableWifiAdapterAutoEnable = false, EnableAutoSwitch = true
    };

    private static MonitoringEngine Engine(FakeWifiClient wifi, ScriptedTester tester, MonitorPolicy policy,
        FakeCommandRunner? commands = null, TimeProvider? time = null) => new(wifi, tester, commands ?? new(), () => policy,
            () => new[] { new WifiProfile { Ssid = "A", CredentialSaved = true }, new WifiProfile { Ssid = "B", CredentialSaved = true } }, time)
    {
        SsidPollInterval = TimeSpan.Zero, HighRiskSettleDelay = TimeSpan.Zero
    };

    [Fact]
    public async Task AssociatedWithoutInternet_ContinuesUntilHealthyCandidate()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home" };
        wifi.ConnectSucceedsFor.UnionWith(["Home", "A", "B"]);
        var tester = new ScriptedTester((ssid, _) => Task.FromResult(Report(ssid, ssid == "B" ? NetworkState.Healthy : NetworkState.InternetIssue)));
        var policy = Policy();
        policy.EnableCurrentWifiReconnect = true;
        using var engine = Engine(wifi, tester, policy);
        var outcomes = new List<(string, bool)>();
        engine.ProfileRecoveryResult += (_, e) => outcomes.Add((e.Ssid, e.Succeeded));
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal(new[] { "Home", "A", "B" }, wifi.ConnectAttempts);
        Assert.Equal(new[] { ("Home", false), ("A", false), ("B", true) }, outcomes);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Theory]
    [InlineData(NetworkState.InternetIssue)]
    [InlineData(NetworkState.GatewayIssue)]
    [InlineData(NetworkState.DnsIssue)]
    public async Task UpstreamFailure_DoesNotRestartDevices(NetworkState state)
    {
        var commands = new FakeCommandRunner();
        var policy = Policy();
        policy.EnableHighRiskRecovery = true;
        policy.HighRiskAfterFailedRounds = 1;
        using var engine = Engine(new(), new((s, _) => Task.FromResult(Report(s, state))), policy, commands);
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Empty(commands.Invocations);
    }

    [Fact]
    public async Task NoLowRiskAction_StillEscalatesWifiFault()
    {
        var commands = new FakeCommandRunner();
        var policy = Policy();
        policy.EnableAutoSwitch = false;
        policy.EnableHighRiskRecovery = true;
        policy.HighRiskAfterFailedRounds = 1;
        using var engine = Engine(new(), new((s, _) => Task.FromResult(Report(s, NetworkState.WifiDisconnected))), policy, commands);
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Contains(commands.Invocations, c => c.FileName == "net.exe");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopOrDispose_CancelsActiveCheck_AndAllowsRestartWhenStopped(bool dispose)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tester = new ScriptedTester(async (s, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Report(s, NetworkState.InternetIssue);
        });
        using var engine = Engine(new(), tester, Policy());
        engine.Start();
        var check = engine.CheckNowAsync();
        await entered.Task;
        if (dispose) engine.Dispose(); else engine.Stop();
        await check.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(engine.IsChecking);
        if (!dispose)
        {
            tester.Run = (s, _) => Task.FromResult(Report(s, NetworkState.Healthy));
            engine.Start();
            await engine.CheckNowAsync();
            Assert.Equal(0, engine.FailureStreak);
        }
    }

    [Fact]
    public async Task ProbeException_DoesNotPermanentlyBlockRecovery()
    {
        var wifi = new FakeWifiClient();
        wifi.ConnectSucceedsFor.Add("B");
        using var engine = Engine(wifi, new((s, _) => s is null
            ? throw new InvalidOperationException("探针异常")
            : Task.FromResult(Report(s, NetworkState.Healthy))), Policy());
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Contains("B", wifi.ConnectAttempts);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public async Task CheckTimeout_ReleasesCheck_AndNextRoundCanRecover()
    {
        var tester = new ScriptedTester(async (s, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Report(s, NetworkState.Healthy);
        });
        using var engine = Engine(new(), tester, Policy());
        engine.CheckTimeout = TimeSpan.FromMilliseconds(30);
        engine.Start();
        await engine.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(engine.IsChecking);
        tester.Run = (s, _) => Task.FromResult(Report(s, NetworkState.Healthy));
        await engine.CheckNowAsync();
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public void UpdateSchedule_ChangesActualTimerDueTime()
    {
        var clock = new RecordingTimeProvider();
        var policy = Policy();
        using var engine = Engine(new(), new((s, _) => Task.FromResult(Report(s, NetworkState.Healthy))), policy, time: clock);
        engine.Start();
        policy.CheckIntervalSeconds = 7;
        engine.UpdateSchedule();
        Assert.Equal(TimeSpan.FromSeconds(7), clock.Timer.Due);
        Assert.Equal(clock.GetLocalNow().AddSeconds(7), engine.NextCheckAt);
    }

    [Fact]
    public async Task FailedSsid_IsSkippedUntilPerNetworkCooldownExpires()
    {
        var clock = new RecordingTimeProvider();
        var wifi = new FakeWifiClient { CurrentSsid = "Home" };
        wifi.ConnectSucceedsFor.UnionWith(["A", "B"]);
        var policy = Policy();
        policy.RecoveryCooldownSeconds = 30;
        using var engine = Engine(wifi, new((s, _) => Task.FromResult(Report(s, NetworkState.InternetIssue))), policy, time: clock);
        engine.Start();
        await engine.CheckNowAsync();
        clock.Now = clock.Now.AddSeconds(31);
        await engine.CheckNowAsync();
        // A 在第二轮又失败后进入退避；外部改变当前网络不应让引擎立即重试 A。
        wifi.CurrentSsid = "Home";
        policy.RecoveryCooldownSeconds = 0;
        await engine.CheckNowAsync();
        Assert.Equal(new[] { "A", "B", "A", "B" }, wifi.ConnectAttempts);
    }

    [Fact]
    public async Task CandidateProbeException_ContinuesToNextCandidate()
    {
        var wifi = new FakeWifiClient();
        wifi.ConnectSucceedsFor.UnionWith(["A", "B"]);
        using var engine = Engine(wifi, new((s, _) => s == "A"
            ? throw new InvalidOperationException("候选探针异常")
            : Task.FromResult(Report(s, s == "B" ? NetworkState.Healthy : NetworkState.InternetIssue))), Policy());
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal(new[] { "A", "B" }, wifi.ConnectAttempts);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public async Task StopDuringRecovery_PreventsNextCandidate()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wifi = new FakeWifiClient();
        wifi.ConnectSucceedsFor.UnionWith(["A", "B"]);
        using var engine = Engine(wifi, new(async (s, token) =>
        {
            if (s == "A")
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            return Report(s, NetworkState.InternetIssue);
        }), Policy());
        engine.Start();
        var check = engine.CheckNowAsync();
        await entered.Task;
        engine.Stop();
        await check.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { "A" }, wifi.ConnectAttempts);
    }

    [Theory]
    [InlineData(true, false, "net.exe", "netsh.exe")]
    [InlineData(false, true, "netsh.exe", "net.exe")]
    public async Task ExplicitHighRiskActionSwitches_AreRespected(bool wlan, bool adapter, string expected, string forbidden)
    {
        var commands = new FakeCommandRunner();
        var policy = Policy();
        policy.EnableAutoSwitch = false;
        policy.EnableHighRiskRecovery = true;
        policy.EnableWlanServiceRestart = wlan;
        policy.EnableAdapterRestart = adapter;
        policy.HighRiskAfterFailedRounds = 1;
        using var engine = Engine(new(), new((s, _) => Task.FromResult(Report(s, NetworkState.WifiDisconnected))), policy, commands);
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Contains(commands.Invocations, c => c.FileName == expected);
        Assert.DoesNotContain(commands.Invocations, c => c.FileName == forbidden);
    }

    [Fact]
    public async Task WlanRestartLeavesOnlyUpstreamIssue_SkipsAdapterRestart()
    {
        var commands = new FakeCommandRunner();
        var policy = Policy();
        policy.EnableAutoSwitch = false;
        policy.EnableHighRiskRecovery = true;
        policy.HighRiskAfterFailedRounds = 1;
        var probes = 0;
        using var engine = Engine(new(), new((s, _) => Task.FromResult(Report(s,
            probes++ < 2 ? NetworkState.WifiDisconnected : NetworkState.InternetIssue))), policy, commands);
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Contains(commands.Invocations, c => c.FileName == "net.exe");
        Assert.DoesNotContain(commands.Invocations, c => c.FileName == "netsh.exe");
    }

    [Fact]
    public async Task HungInitialProbe_StillAllowsRecoveryWithinRoundDeadline()
    {
        var wifi = new FakeWifiClient();
        wifi.ConnectSucceedsFor.Add("B");
        var pending = new TaskCompletionSource<ConnectivityReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = Engine(wifi, new((s, _) => s is null ? pending.Task
            : Task.FromResult(Report(s, NetworkState.Healthy))), Policy());
        engine.FullProbeTimeout = TimeSpan.FromMilliseconds(30);
        engine.Start();
        await engine.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(2));
        pending.SetResult(Report(null, NetworkState.InternetIssue));
        Assert.Contains("B", wifi.ConnectAttempts);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public async Task HighRiskServiceException_StillTriesAdapter()
    {
        var commands = new FakeCommandRunner { OnRun = file => { if (file == "net.exe") throw new InvalidOperationException("服务异常"); } };
        var policy = Policy();
        policy.EnableAutoSwitch = false;
        policy.EnableHighRiskRecovery = true;
        policy.HighRiskAfterFailedRounds = 1;
        using var engine = Engine(new(), new((s, _) => Task.FromResult(Report(s, NetworkState.WifiDisconnected))), policy, commands);
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Contains(commands.Invocations, c => c.FileName == "netsh.exe");
    }

    private static ConnectivityReport Report(string? ssid, NetworkState state) => new() { CurrentSsid = ssid, State = state };
}

internal sealed class ScriptedTester(Func<string?, CancellationToken, Task<ConnectivityReport>> run) : IConnectivityTester
{
    public Func<string?, CancellationToken, Task<ConnectivityReport>> Run { get; set; } = run;
    public Task<ConnectivityReport> RunFullCheckAsync(MonitorPolicy policy, string? currentSsid,
        WifiAdapterDiagnostics? wifiDiagnostics = null, CancellationToken cancellationToken = default) => Run(currentSsid, cancellationToken);
}

internal sealed class RecordingTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public RecordingTimer Timer { get; } = new();
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Timer.Change(dueTime, period);
        return Timer;
    }
    public sealed class RecordingTimer : ITimer
    {
        public TimeSpan Due { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return true; }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
