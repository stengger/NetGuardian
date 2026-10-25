using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class AdaptiveMonitoringTests
{
    [Fact]
    public async Task 单次定时回调提前到达也必须继续安排检测()
    {
        var clock = new DrivenClock();
        var tester = new AdaptiveTester();
        using var engine = Create(clock, tester, new MonitorPolicy());
        engine.Start();
        await engine.CheckNowAsync();
        clock.Advance(TimeSpan.FromSeconds(89));
        clock.FireEarly();
        Assert.Equal(0, tester.LightChecks);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, tester.LightChecks);
    }

    [Fact]
    public async Task 完整失败后恢复总超时本轮不得重复累计()
    {
        var wifi = new FakeWifiClient
        {
            CurrentSsid = "原网络",
            ScanHandler = async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return []; }
        };
        var policy = new MonitorPolicy { ConsecutiveFailuresBeforeRecovery = 1, EnableCurrentWifiReconnect = false };
        using var engine = new MonitoringEngine(wifi, new FakeConnectivityTester { NextState = NetworkState.InternetIssue },
            new FakeCommandRunner(), () => policy, () => []);
        engine.CheckTimeout = TimeSpan.FromMilliseconds(30);
        engine.Start();
        await engine.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, engine.FailureStreak);
        Assert.False(engine.IsChecking);
    }

    [Fact]
    public async Task 活跃检查停止重启后旧结果不得提交且新一轮仍完整诊断()
    {
        var clock = new DrivenClock();
        var tester = new AdaptiveTester();
        var completion = new TaskCompletionSource<ConnectivityReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        tester.PendingFullCheck = completion.Task;
        using var engine = Create(clock, tester, new MonitorPolicy());
        var reports = new List<ConnectivityReport>();
        engine.CheckCompleted += (_, e) => reports.Add(e.Report);
        engine.Start();
        var checking = engine.CheckNowAsync();
        engine.Stop();
        engine.Start();
        completion.SetResult(new ConnectivityReport { State = NetworkState.Healthy });
        await checking;
        Assert.Empty(reports);
        tester.PendingFullCheck = null;
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Single(reports);
        Assert.False(reports[0].IsLightweight);
        Assert.Equal(0, tester.LightChecks);
    }

    [Fact]
    public async Task 健康定时走轻量检查而手动仍走完整诊断()
    {
        var clock = new DrivenClock();
        var tester = new AdaptiveTester();
        var policy = new MonitorPolicy { CheckIntervalSeconds = 90 };
        using var engine = Create(clock, tester, policy);
        var reports = new List<ConnectivityReport>();
        engine.CheckCompleted += (_, e) => reports.Add(e.Report);
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal(clock.GetLocalNow().AddSeconds(90), engine.NextCheckAt);
        clock.Advance(TimeSpan.FromSeconds(90));
        Assert.Equal(2, reports.Count);
        Assert.True(reports[1].IsLightweight);
        Assert.Equal(1, tester.FullChecks);
        await engine.CheckNowAsync();
        Assert.False(reports[2].IsLightweight);
        Assert.Equal(2, tester.FullChecks);
    }

    [Fact]
    public async Task 轻量失败升级诊断只计一次失败并加速随后恢复正常周期()
    {
        var clock = new DrivenClock();
        var tester = new AdaptiveTester();
        var policy = new MonitorPolicy { CheckIntervalSeconds = 90, ConsecutiveFailuresBeforeRecovery = 100 };
        using var engine = Create(clock, tester, policy);
        engine.Start();
        await engine.CheckNowAsync();
        tester.Healthy = false;
        clock.Advance(TimeSpan.FromSeconds(90));
        Assert.Equal(1, engine.FailureStreak);
        Assert.Equal(2, tester.FullChecks);
        Assert.Equal(clock.GetLocalNow().AddSeconds(10), engine.NextCheckAt);
        tester.Healthy = true;
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, engine.FailureStreak);
        Assert.Equal(3, tester.FullChecks);
        Assert.Equal(clock.GetLocalNow().AddSeconds(90), engine.NextCheckAt);
        policy.CheckIntervalSeconds = 5;
        engine.UpdateSchedule();
        Assert.Equal(clock.GetLocalNow().AddSeconds(5), engine.NextCheckAt);
    }

    [Fact]
    public async Task 事件从首次通知合并且强制完整诊断停止后不再触发()
    {
        var clock = new DrivenClock();
        var tester = new AdaptiveTester();
        var source = new TestNetworkChanges();
        using var engine = Create(clock, tester, new MonitorPolicy(), source);
        engine.Start();
        await engine.CheckNowAsync();
        source.Notify();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        source.Notify();
        engine.UpdateSchedule();
        Assert.Equal(clock.GetLocalNow().AddMilliseconds(500), engine.NextCheckAt);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(2, tester.FullChecks);
        Assert.Equal(0, tester.LightChecks);
        source.Notify();
        engine.Stop();
        source.Notify();
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(2, tester.FullChecks);
        Assert.Equal(0, source.Subscribers);
        Assert.Null(engine.NextCheckAt);
    }

    [Fact]
    public async Task 检测中事件仅追加一次且旧订阅不能影响重启()
    {
        var clock = new DrivenClock();
        var tester = new AdaptiveTester();
        var source = new TestNetworkChanges();
        using var engine = Create(clock, tester, new MonitorPolicy(), source);
        engine.Start();
        var oldHandler = source.Handler!;
        var completion = new TaskCompletionSource<ConnectivityReport>();
        tester.PendingFullCheck = completion.Task;
        var running = engine.CheckNowAsync();
        source.Notify();
        source.Notify();
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, tester.FullChecks);
        tester.PendingFullCheck = null;
        completion.SetResult(new ConnectivityReport { State = NetworkState.Healthy });
        await running;
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, tester.FullChecks);
        engine.Stop();
        engine.Start();
        var due = engine.NextCheckAt;
        oldHandler();
        Assert.Equal(due, engine.NextCheckAt);
        Assert.Equal(1, source.Subscribers);
    }

    [Fact]
    public async Task 事件源订阅失败仍能定时检测并报告原因()
    {
        var clock = new DrivenClock();
        var tester = new AdaptiveTester();
        using var engine = Create(clock, tester, new MonitorPolicy(), new TestNetworkChanges { Fail = true });
        var errors = new List<EngineEventArgs>();
        engine.EngineEvent += (_, e) => errors.Add(e);
        engine.Start();
        await engine.CheckNowAsync();
        clock.Advance(TimeSpan.FromSeconds(90));
        Assert.Equal(1, tester.LightChecks);
        Assert.Contains(errors, e => e.Kind == EngineEventKind.CheckError && e.Args.Any(value => value?.ToString()?.Contains("模拟订阅失败") == true));
    }

    [Fact]
    public async Task 轻量超时仍执行完整诊断而不直接累计失败()
    {
        var clock = new DrivenClock();
        var tester = new AdaptiveTester();
        using var engine = Create(clock, tester, new MonitorPolicy { ConsecutiveFailuresBeforeRecovery = 100 });
        engine.Start();
        await engine.CheckNowAsync();
        tester.PendingLightCheck = new TaskCompletionSource<ConnectivityReport>().Task;
        engine.FullProbeTimeout = TimeSpan.FromMilliseconds(20);
        var completed = new TaskCompletionSource<ConnectivityReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.CheckCompleted += (_, e) => completed.TrySetResult(e.Report);
        clock.Advance(TimeSpan.FromSeconds(90));
        var report = await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(report.Ok);
        Assert.False(report.IsLightweight);
        Assert.Equal(2, tester.FullChecks);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public async Task 切换网络验证成功后返回正常周期而非保留故障前周期()
    {
        var clock = new DrivenClock();
        var wifi = new FakeWifiClient { CurrentSsid = "原网络" };
        wifi.ConnectSucceedsFor.Add("备用网络");
        var tester = new FakeConnectivityTester { NextState = NetworkState.InternetIssue };
        tester.HealthySsids.Add("备用网络");
        var policy = new MonitorPolicy { CheckIntervalSeconds = 90, ConsecutiveFailuresBeforeRecovery = 1, EnableCurrentWifiReconnect = false };
        using var engine = new MonitoringEngine(wifi, tester, new FakeCommandRunner(), () => policy,
            () => [new WifiProfile { Ssid = "备用网络", CredentialSaved = true }], clock);
        engine.Start();
        await engine.CheckNowAsync();
        Assert.Equal("备用网络", wifi.CurrentSsid);
        Assert.Equal(0, engine.FailureStreak);
        Assert.Equal(clock.GetLocalNow().AddSeconds(90), engine.NextCheckAt);
    }

    [Fact]
    public void 新默认周期与已有显式配置兼容()
    {
        Assert.Equal(90, new MonitorPolicy().CheckIntervalSeconds);
        var report = new ConnectivityReport { IsLightweight = true, State = NetworkState.Healthy };
        var json = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<ConnectivityReport>(json)!.IsLightweight);
        var policy = System.Text.Json.JsonSerializer.Deserialize<MonitorPolicy>("{\"CheckIntervalSeconds\":30}")!;
        Assert.Equal(30, policy.CheckIntervalSeconds);
    }

    private static MonitoringEngine Create(DrivenClock clock, AdaptiveTester tester, MonitorPolicy policy, INetworkChangeSource? source = null)
        => new(new FakeWifiClient(), tester, new FakeCommandRunner(), () => policy, () => [], clock, networkChanges: source);

    private sealed class AdaptiveTester : IConnectivityTester
    {
        public bool Healthy { get; set; } = true;
        public int FullChecks { get; private set; }
        public int LightChecks { get; private set; }
        public Task<ConnectivityReport>? PendingFullCheck { get; set; }
        public Task<ConnectivityReport>? PendingLightCheck { get; set; }
        public Task<ConnectivityReport> RunFullCheckAsync(MonitorPolicy policy, string? currentSsid,
            WifiAdapterDiagnostics? wifiDiagnostics = null, CancellationToken cancellationToken = default)
        {
            FullChecks++;
            return PendingFullCheck ?? Task.FromResult(new ConnectivityReport { State = Healthy ? NetworkState.Healthy : NetworkState.InternetIssue });
        }
        public Task<ConnectivityReport> RunLightweightCheckAsync(MonitorPolicy policy, CancellationToken cancellationToken = default)
        {
            LightChecks++;
            return PendingLightCheck ?? Task.FromResult(new ConnectivityReport { IsLightweight = true, State = Healthy ? NetworkState.Healthy : NetworkState.InternetIssue });
        }
    }

    private sealed class TestNetworkChanges : INetworkChangeSource
    {
        public Action? Handler { get; private set; }
        public int Subscribers { get; private set; }
        public bool Fail { get; init; }
        public IDisposable Subscribe(Action changed)
        {
            if (Fail) throw new InvalidOperationException("模拟订阅失败");
            Subscribers++;
            Handler = changed;
            return new Subscription(() => { Subscribers--; Handler = null; });
        }
        public void Notify() => Handler?.Invoke();
        private sealed class Subscription(Action release) : IDisposable { public void Dispose() => release(); }
    }

    private sealed class DrivenClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        private readonly List<DrivenTimer> _timers = [];
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new DrivenTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            _now += elapsed;
            foreach (var timer in _timers.ToArray()) timer.Fire();
        }
        public void FireEarly()
        {
            foreach (var timer in _timers.ToArray()) timer.Fire(early: true);
        }
        private sealed class DrivenTimer(DrivenClock clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? _due;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                return true;
            }
            public void Fire(bool early = false)
            {
                if (_due is null || !early && _due > clock._now) return;
                _due = null;
                callback(state);
            }
            public void Dispose() => _due = null;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
