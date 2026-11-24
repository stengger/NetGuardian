using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class UnattendedEngineTests
{
    [Theory]
    [InlineData("office")]
    [InlineData(" Office ")]
    public async Task 一个网络失败不会让不同名称的备用网络进入冷却(string backup)
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home", ScanHandler = _ => Task.FromResult(FakeWifiClient.Visible("Office", backup)) };
        wifi.ConnectHandler = (ssid, _) =>
        {
            wifi.CurrentSsid = ssid;
            return Task.FromResult(true);
        };
        var policy = Policy();
        policy.RecoveryCooldownSeconds = 30;
        using var engine = new MonitoringEngine(wifi,
            new ScriptedTester((ssid, _) => Task.FromResult(Report(ssid,
                ssid == backup ? NetworkState.Healthy : NetworkState.InternetIssue))),
            new FakeCommandRunner(), () => policy, () =>
            [
                new WifiProfile { Ssid = "Office", Priority = 1, CredentialSaved = true },
                new WifiProfile { Ssid = backup, Priority = 2, CredentialSaved = true }
            ]);
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal(new[] { "Office", backup }, wifi.ConnectAttempts);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public async Task 停止期间连接请求未收尾不能释放检查控制权()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wifi = new FakeWifiClient { CurrentSsid = "Home", ConnectHandler = (_, _) =>
        {
            entered.TrySetResult();
            return release.Task;
        }};
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(Report(ssid, NetworkState.InternetIssue))), Policy());
        engine.Start();
        var check = engine.CheckNowAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        engine.Stop();
        try
        {
            await Task.Delay(50);
            Assert.True(engine.IsChecking);
            Assert.False(check.IsCompleted);
        }
        finally { release.TrySetResult(true); }
        await check.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(engine.IsChecking);
        Assert.Single(wifi.ConnectAttempts);
    }

    [Fact]
    public async Task 链路过渡期间SSID短暂消失时仍等待目标网络()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home" };
        wifi.ConnectSucceedsFor.UnionWith(["A", "B"]);
        var probes = 0;
        var missingSsidReads = 0;
        wifi.ReadCurrentSsid = () => wifi.CurrentSsid == "A" && probes == 1 && missingSsidReads++ == 0 ? null : wifi.CurrentSsid;
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(Report(ssid,
            ssid == "A" ? ++probes == 1 ? NetworkState.WifiDisconnected : NetworkState.Healthy
            : ssid == "B" ? NetworkState.Healthy : NetworkState.InternetIssue))), Policy());
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal(new[] { "A" }, wifi.ConnectAttempts);
        Assert.Equal(2, probes);
    }

    [Theory]
    [InlineData(false, true, true, null)]
    [InlineData(true, false, true, null)]
    [InlineData(true, true, false, null)]
    [InlineData(true, true, true, 10)]
    public async Task 无线明确关闭或设备故障时不作为链路过渡等待(bool hardwareOn, bool softwareOn, bool present, int? problemCode)
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home", Diagnostics = new("Wi-Fi", present, false, hardwareOn, softwareOn, "disconnected", "", ProblemCode: problemCode) };
        wifi.ConnectSucceedsFor.UnionWith(["A", "B"]);
        var probes = 0;
        using var engine = Engine(wifi, new((ssid, _) =>
        {
            if (ssid == "A") probes++;
            return Task.FromResult(Report(ssid, ssid == "A" ? NetworkState.WifiDisabled : ssid == "B" ? NetworkState.Healthy : NetworkState.InternetIssue));
        }), Policy());
        engine.Start();
        await engine.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, probes);
        Assert.Equal("B", wifi.CurrentSsid);
    }

    [Theory]
    [InlineData(NetworkState.WifiDisabled)]
    [InlineData(NetworkState.WifiDisconnected)]
    public async Task 连接请求接受后的链路过渡状态等待就绪(NetworkState transitionalState)
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home" };
        wifi.ConnectSucceedsFor.UnionWith(["A", "B"]);
        var probes = 0;
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(Report(ssid,
            ssid == "A" ? ++probes == 1 ? transitionalState : NetworkState.Healthy
            : ssid == "B" ? NetworkState.Healthy : NetworkState.InternetIssue))), Policy());
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal(new[] { "A" }, wifi.ConnectAttempts);
        Assert.Equal(2, probes);
    }

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
            return Task.FromResult(FakeWifiClient.Visible("B"));
        };
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(Report(ssid,
            ssid == "B" ? NetworkState.Healthy : NetworkState.InternetIssue))), Policy());
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal("B", wifi.CurrentSsid);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public async Task 扫描失败且没有最近验证成功的网络时不发送连接请求()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home", ScanHandler = _ => throw new InvalidOperationException("扫描暂不可用") };
        wifi.ConnectSucceedsFor.Add("B");
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(Report(ssid,
            ssid == "B" ? NetworkState.Healthy : NetworkState.InternetIssue))), Policy());
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Empty(wifi.ConnectAttempts);
        Assert.Equal("Home", wifi.CurrentSsid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 恢复只尝试当前扫描确认的网络且最多三个(bool hasVisible)
    {
        var profiles = Enumerable.Range(1, 57).Select(i => new WifiProfile
        {
            Ssid = $"WiFi-{i}", Priority = i, Enabled = i > 3, CredentialSaved = true
        }).ToArray();
        var wifi = new FakeWifiClient { CurrentSsid = "Home", ScanHandler = _ =>
            Task.FromResult<IReadOnlyList<WifiNetwork>>(hasVisible
                ? [.. new[] { 2, 4, 53, 54, 55, 56, 57 }.Select(i => new WifiNetwork($"WiFi-{i}", 80, "", "", true, i != 4))]
                : []) };
        using var engine = new MonitoringEngine(wifi,
            new ScriptedTester((ssid, _) => Task.FromResult(Report(ssid, NetworkState.InternetIssue))),
            new FakeCommandRunner(), Policy, () => profiles);
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal(hasVisible ? new[] { "WiFi-53", "WiFi-54", "WiFi-55" } : [], wifi.ConnectAttempts);
        if (hasVisible)
        {
            wifi.ConnectAttempts.Clear();
            await engine.CheckNowAsync();
            Assert.Equal(new[] { "WiFi-56", "WiFi-57" }, wifi.ConnectAttempts);
        }
    }

    [Fact]
    public async Task 两次扫描失败后只尝试近期验证成功的一个备用网络()
    {
        var scans = 0;
        var wifi = new FakeWifiClient { CurrentSsid = "B", ScanHandler = _ =>
        {
            scans++;
            throw new InvalidOperationException("扫描暂不可用");
        } };
        var healthy = true;
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(Report(ssid,
            healthy ? NetworkState.Healthy : NetworkState.InternetIssue))), Policy());
        engine.Start();
        await engine.CheckNowAsync();
        healthy = false;
        wifi.CurrentSsid = "Home";
        await engine.CheckNowAsync();
        Assert.Equal(2, scans);
        Assert.Equal(new[] { "B" }, wifi.ConnectAttempts);
    }

    [Fact]
    public async Task 扫描超时后补扫但无近期记录时零连接请求()
    {
        var scans = 0;
        var wifi = new FakeWifiClient { CurrentSsid = "Home", ScanHandler = async token =>
        {
            scans++;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return FakeWifiClient.Visible("A");
        } };
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(Report(ssid, NetworkState.InternetIssue))), Policy());
        engine.RecoveryScanAttemptTimeout = TimeSpan.FromMilliseconds(25);
        EngineEventArgs? summary = null;
        engine.EngineEvent += (_, e) => { if (e.Kind == EngineEventKind.RecoveryWifiSwitchSummary) summary = e; };
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal(2, scans);
        Assert.Empty(wifi.ConnectAttempts);
        Assert.NotNull(summary);
        Assert.Equal("扫描超时", summary.Args[0]);
        Assert.Equal(0, summary.Args[4]);
    }

    [Fact]
    public async Task 切换预算覆盖原生连接等待且不会下发下一条请求()
    {
        var cancelled = false;
        var wifi = new FakeWifiClient { CurrentSsid = "Home", ConnectHandler = async (_, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cancelled = token.IsCancellationRequested; }
            return false;
        } };
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(Report(ssid, NetworkState.InternetIssue))), Policy());
        engine.WifiSwitchBudget = TimeSpan.FromMilliseconds(50);
        engine.Start();
        await engine.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(cancelled);
        Assert.Equal(new[] { "A" }, wifi.ConnectAttempts);
    }

    [Fact]
    public async Task DNS复测确认恢复时保留当前关联不重连不切网()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "Home" };
        var probes = 0;
        var policy = Policy();
        policy.EnableCurrentWifiReconnect = true;
        using var engine = Engine(wifi, new((ssid, _) => Task.FromResult(++probes == 1
            ? new ConnectivityReport { CurrentSsid = ssid, State = NetworkState.DnsIssue, Gateway = "192.168.1.1",
                Probes = [new ConnectivityProbe("Gateway ping", true, 3, "192.168.1.1")] }
            : Report(ssid, NetworkState.Healthy))), policy);
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal(2, probes);
        Assert.Empty(wifi.ConnectAttempts);
        Assert.Equal("Home", wifi.CurrentSsid);
        Assert.Equal(0, engine.FailureStreak);
    }

    private static MonitorPolicy Policy() => new()
    {
        CheckIntervalSeconds = 3600, ConsecutiveFailuresBeforeRecovery = 1,
        RecoveryCooldownSeconds = 0, EnableCurrentWifiReconnect = false,
        EnableWifiAdapterAutoEnable = false, EnableAutoSwitch = true
    };

    private static MonitoringEngine Engine(FakeWifiClient wifi, ScriptedTester tester, MonitorPolicy policy,
        FakeCommandRunner? commands = null, TimeProvider? time = null)
    {
        wifi.ScanHandler ??= _ => Task.FromResult(FakeWifiClient.Visible("A", "B"));
        return new(wifi, tester, commands ?? new(), () => policy,
            () => new[] { new WifiProfile { Ssid = "A", CredentialSaved = true }, new WifiProfile { Ssid = "B", CredentialSaved = true } }, time)
        {
            SsidPollInterval = TimeSpan.Zero, HighRiskSettleDelay = TimeSpan.Zero
        };
    }

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
