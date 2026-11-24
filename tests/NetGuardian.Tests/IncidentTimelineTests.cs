using NetGuardian.Core.Wifi;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using Xunit;

namespace NetGuardian.Tests;

public sealed class IncidentTimelineTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory().FullName;
    private readonly Clock _clock = new();
    private readonly FakeWifiClient _wifi = new() { CurrentSsid = "主网" };
    private readonly Tester _tester = new();
    private readonly MonitorPolicy _policy = new() { ConsecutiveFailuresBeforeRecovery = 1,
        EnableCurrentWifiReconnect = false, EnableWifiAdapterAutoEnable = false };

    private MonitoringEngine Engine(string actor, params string[] candidates)
    {
        _wifi.ScanHandler = _ => Task.FromResult(FakeWifiClient.Visible(candidates));
        var engine = new MonitoringEngine(_wifi, _tester, new FakeCommandRunner(), () => _policy,
            () => candidates.Select((s, i) => new WifiProfile { Ssid = s, Priority = i, CredentialSaved = true }), _clock);
        var journal = new EventJournal(_directory, _clock);
        journal.ObserveIncidents(engine, actor);
        engine.CheckCompleted += (_, e) => journal.RecordCheck(e.Report.State, e.Report.Ok);
        engine.Start();
        return engine;
    }

    [Fact]
    public async Task 三次尝试两次实际关联一次公网恢复且重读标识稳定()
    {
        _wifi.ConnectSucceedsFor.UnionWith(["候选乙", "候选丙"]);
        _tester.OnCheck = ssid => { _clock.Advance(12); return ssid == "候选丙"; };
        using var engine = Engine("后台", "候选甲", "候选乙", "候选丙");
        await engine.CheckNowAsync();
        var reader = new IncidentHistoryReader([_directory]);
        var first = Assert.Single(reader.Read());
        Assert.Equal("主网", first.OriginalNetwork);
        Assert.Equal(3, first.SwitchAttempts);
        Assert.Equal(2, first.WifiAssociations);
        Assert.Equal(1, first.VerifiedRecoveries);
        Assert.Equal(IncidentOutcome.RecoveredAfterAction, first.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(24), first.ObservedDuration);
        Assert.Contains(first.Steps, s => s.Kind == IncidentStepKind.PublicVerified && s.Target == "候选丙" && s.Evidence!.Any(p => p.Name == "HTTPS one" && p.Ok));
        // 模拟一次批次重复投递，重读仍只认同一个事件标识。
        var path = Directory.GetFiles(_directory, "events-*.jsonl").Single();
        File.AppendAllText(path, File.ReadAllText(path));
        var reopened = Assert.Single(new IncidentHistoryReader([_directory]).Read());
        Assert.Equal(first.Id, reopened.Id);
        Assert.Equal(first.Steps.Count, reopened.Steps.Count);
        Assert.Equal(new WeeklySummary(1, 1, 1), new EventJournal(_directory, _clock).GetSummary());
    }

    [Fact]
    public async Task 同一次中断跨月跨恢复轮次仍共用首次失败标识()
    {
        _policy.RecoveryCooldownSeconds = 30;
        _clock.Set(new DateTimeOffset(2026, 11, 30, 23, 59, 48, _clock.LocalTimeZone.GetUtcOffset(new DateTime(2026, 11, 30))));
        using var engine = Engine("后台", "备用网");
        await engine.CheckNowAsync();
        var reader = new IncidentHistoryReader([_directory]);
        var initial = Assert.Single(reader.Read());
        Assert.Equal(IncidentOutcome.Open, initial.Outcome);
        _clock.Advance(35);
        _wifi.ConnectSucceedsFor.Add("备用网");
        _tester.OnCheck = ssid => ssid == "备用网";
        await engine.CheckNowAsync();
        Assert.Equal(2, Directory.GetFiles(_directory, "events-*.jsonl").Length);
        var final = Assert.Single(reader.Read());
        Assert.Equal(initial.Id, final.Id);
        Assert.Equal(2, final.SwitchAttempts);
        Assert.Equal(2, final.Steps.Count(s => s.Kind == IncidentStepKind.RoundStarted));
        Assert.Equal(1, final.VerifiedRecoveries);
        Assert.Equal(TimeSpan.FromSeconds(35), final.ObservedDuration);
    }

    [Theory]
    [InlineData(false, false, IncidentOutcome.NaturalRecovery)]
    [InlineData(true, false, IncidentOutcome.CauseUnknown)]
    [InlineData(false, true, IncidentOutcome.CauseUnknown)]
    public async Task 无动作恢复与交接或长时间缺测分别归类(bool handoff, bool sleep, IncidentOutcome outcome)
    {
        _policy.EnableAutoSwitch = false;
        using var first = Engine("前台");
        await first.CheckNowAsync();
        var reader = new IncidentHistoryReader([_directory]);
        var id = Assert.Single(reader.Read()).Id;
        _clock.Advance(sleep ? 7200 : 30);
        _tester.OnCheck = _ => true;
        if (handoff)
        {
            first.Stop();
            using var second = Engine("后台");
            await second.CheckNowAsync();
        }
        else await first.CheckNowAsync();
        var incident = Assert.Single(reader.Read());
        Assert.Equal(id, incident.Id);
        Assert.Equal(outcome, incident.Outcome);
        Assert.Equal(handoff || sleep, incident.HasObservationGap);
        Assert.Equal(sleep || handoff ? TimeSpan.Zero : TimeSpan.FromSeconds(30), incident.ObservedDuration);
    }

    [Fact]
    public void 旧版边界只读并忽略无法归属动作及损坏行()
    {
        var path = Path.Combine(_directory, "events-202610.jsonl");
        File.WriteAllLines(path, ["{\"ts\":\"2026-10-01T09:00:00Z\",\"type\":\"recovery\",\"action\":\"connected\",\"ssid\":\"孤立动作\"}",
            "{\"ts\":\"2026-10-01T09:01:00Z\",\"type\":\"check\",\"state\":\"InternetIssue\",\"ok\":false}", "{损坏}",
            "{\"ts\":\"2026-10-01T09:02:00Z\",\"type\":\"check\",\"state\":\"Healthy\",\"ok\":true}"]);
        var original = File.ReadAllBytes(path);
        var legacy = Assert.Single(new IncidentHistoryReader([_directory]).Read());
        Assert.Equal(IncidentOutcome.CauseUnknown, legacy.Outcome);
        Assert.True(legacy.IsLegacy);
        Assert.Null(legacy.OriginalNetwork);
        Assert.Equal(0, legacy.VerifiedRecoveries);
        Assert.Equal(2, legacy.Steps.Count);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task 未取得控制权的前台退出不应给后台故障插入观测空档()
    {
        _policy.EnableAutoSwitch = false;
        using var owner = Engine("后台");
        await owner.CheckNowAsync();
        using var idle = new MonitoringEngine(_wifi, _tester, new FakeCommandRunner(), () => _policy, () => [], _clock);
        new EventJournal(_directory, _clock).ObserveIncidents(idle, "前台");
        idle.Stop();
        _clock.Advance(30);
        _tester.OnCheck = _ => true;
        await owner.CheckNowAsync();
        var incident = Assert.Single(new IncidentHistoryReader([_directory]).Read());
        Assert.False(incident.HasObservationGap);
        Assert.Equal(IncidentOutcome.NaturalRecovery, incident.Outcome);
    }

    [Fact]
    public void 旧版动作恢复边界可结案且不会伪造公网验证()
    {
        var path = Path.Combine(_directory, "events-202610.jsonl");
        File.WriteAllLines(path, ["""{"ts":"2026-10-01T09:01:00Z","type":"check","state":"InternetIssue","ok":false}""",
            """{"ts":"2026-10-01T09:02:00Z","type":"recovery","action":"connected","ssid":"备用网"}"""]);
        var incident = Assert.Single(new IncidentHistoryReader([_directory]).Read());
        Assert.Equal(IncidentOutcome.CauseUnknown, incident.Outcome);
        Assert.Equal("备用网", incident.Steps[^1].Target);
        Assert.Equal(0, incident.VerifiedRecoveries);
    }

    [Fact]
    public async Task 旧版成功通知不能替正在跟踪的故障补出公网复测()
    {
        _policy.EnableAutoSwitch = false;
        using var engine = Engine("后台");
        await engine.CheckNowAsync();
        _clock.Advance(2);
        new EventJournal(_directory, _clock).RecordRecoveryResult(true, "旧版报告的网络");
        var incident = Assert.Single(new IncidentHistoryReader([_directory]).Read());
        Assert.Equal(IncidentOutcome.Open, incident.Outcome);
        Assert.Null(incident.EndedAt);
        Assert.Equal(0, incident.VerifiedRecoveries);
    }

    [Fact]
    public async Task 检测器异常不能当作首次确认的公网故障()
    {
        _policy.EnableAutoSwitch = false;
        _tester.OnCheck = _ => throw new InvalidOperationException("探测器异常");
        using var engine = Engine("后台");
        await engine.CheckNowAsync();
        Assert.Empty(new IncidentHistoryReader([_directory]).Read());
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 22, 1, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Set(DateTimeOffset at) => _now = at.ToUniversalTime();
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }

    private sealed class Tester : IConnectivityTester
    {
        public Func<string?, bool> OnCheck { get; set; } = _ => false;
        public Task<ConnectivityReport> RunFullCheckAsync(MonitorPolicy policy, string? currentSsid,
            WifiAdapterDiagnostics? wifiDiagnostics = null, CancellationToken cancellationToken = default)
        {
            var ok = OnCheck(currentSsid);
            return Task.FromResult(new ConnectivityReport { State = ok ? NetworkState.Healthy : NetworkState.InternetIssue,
                CurrentSsid = currentSsid, Probes = [new("HTTPS one", ok), new("HTTPS two", ok)] });
        }
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
