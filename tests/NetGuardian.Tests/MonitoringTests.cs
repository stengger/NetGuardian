using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public class RecoveryPlannerTests
{
    private static MonitorPolicy Policy(int failures = 3, int cooldownSeconds = 300) => new()
    {
        ConsecutiveFailuresBeforeRecovery = failures,
        RecoveryCooldownSeconds = cooldownSeconds
    };

    [Fact]
    public void Evaluate_HealthyOrNotMonitoring_IsNotNeeded()
    {
        var now = DateTimeOffset.Now;
        Assert.Equal(RecoveryDecisionKind.NotNeeded,
            RecoveryPlanner.Evaluate(Policy(), reportOk: true, isMonitoring: true, 10, null, now).Kind);
        Assert.Equal(RecoveryDecisionKind.NotNeeded,
            RecoveryPlanner.Evaluate(Policy(), reportOk: false, isMonitoring: false, 10, null, now).Kind);
    }

    [Theory]
    [InlineData(1, RecoveryDecisionKind.BelowThreshold)]
    [InlineData(2, RecoveryDecisionKind.BelowThreshold)]
    [InlineData(3, RecoveryDecisionKind.Proceed)]
    [InlineData(7, RecoveryDecisionKind.Proceed)]
    public void Evaluate_RespectsFailureThreshold(int streak, RecoveryDecisionKind expected)
    {
        var decision = RecoveryPlanner.Evaluate(Policy(failures: 3), false, true, streak, null, DateTimeOffset.Now);
        Assert.Equal(expected, decision.Kind);
    }

    [Fact]
    public void Evaluate_WithinCooldown_ReportsRemainingTime()
    {
        var now = DateTimeOffset.Now;
        var decision = RecoveryPlanner.Evaluate(Policy(cooldownSeconds: 300), false, true, 5, now.AddSeconds(-100), now);
        Assert.Equal(RecoveryDecisionKind.CooldownActive, decision.Kind);
        Assert.InRange(decision.CooldownRemaining.TotalSeconds, 199, 201);
    }

    [Fact]
    public void Evaluate_AfterCooldown_Proceeds()
    {
        var now = DateTimeOffset.Now;
        var decision = RecoveryPlanner.Evaluate(Policy(cooldownSeconds: 300), false, true, 5, now.AddSeconds(-301), now);
        Assert.Equal(RecoveryDecisionKind.Proceed, decision.Kind);
    }

    [Fact]
    public void OrderSwitchCandidates_FiltersAndSortsByPriority()
    {
        var profiles = new[]
        {
            new WifiProfile { Ssid = "Low", Priority = 9, CredentialSaved = true },
            new WifiProfile { Ssid = "Disabled", Priority = 1, Enabled = false, CredentialSaved = true },
            new WifiProfile { Ssid = "NoAuto", Priority = 1, AutoConnect = false, CredentialSaved = true },
            new WifiProfile { Ssid = "NoCred", Priority = 1, CredentialSaved = false },
            new WifiProfile { Ssid = "Current", Priority = 1, CredentialSaved = true },
            new WifiProfile { Ssid = "High", Priority = 2, CredentialSaved = true }
        };

        var candidates = RecoveryPlanner.OrderSwitchCandidates(profiles, "Current");
        Assert.Equal(new[] { "High", "Low" }, candidates.Select(profile => profile.Ssid));
    }

    [Theory]
    [InlineData(true, true, true, true, false)]   // adapter up, all radios on -> no
    [InlineData(false, true, true, true, true)]   // adapter down -> yes
    [InlineData(true, false, true, true, false)]  // policy disabled -> no
    [InlineData(false, true, false, true, false)] // hardware radio off -> no
    [InlineData(true, true, true, false, true)]   // software radio off -> yes
    public void ShouldTryEnableWifi_FollowsPolicyAndRadioState(
        bool adapterUp, bool policyEnabled, bool hardwareOn, bool softwareOn, bool expected)
    {
        var policy = new MonitorPolicy { EnableWifiAdapterAutoEnable = policyEnabled };
        var diagnostics = new WifiAdapterDiagnostics(
            "Wi-Fi", AdapterPresent: true, AdapterUp: adapterUp,
            HardwareRadioOn: hardwareOn, SoftwareRadioOn: softwareOn,
            State: "connected", Detail: "");

        Assert.Equal(expected, RecoveryPlanner.ShouldTryEnableWifi(policy, diagnostics));
    }

    [Theory]
    [InlineData("Office", "Office", true)]
    [InlineData("Office", "office", false)]
    [InlineData("Office", " Office ", false)]
    [InlineData(" Office ", " Office ", true)]
    [InlineData("Office", "Backup", false)]
    [InlineData(null, "Office", false)]
    [InlineData("Office", "", false)]
    public void 网络名称按大小写和空格精确比较(string? left, string? right, bool expected)
    {
        Assert.Equal(expected, RecoveryPlanner.SsidEquals(left, right));
    }

    [Theory]
    [InlineData("office")]
    [InlineData(" Office ")]
    public void 不同大小写或空格的网络仍可作为备用候选(string backup)
    {
        var profiles = new[]
        {
            new WifiProfile { Ssid = "Office", CredentialSaved = true },
            new WifiProfile { Ssid = backup, CredentialSaved = true }
        };
        Assert.Equal(backup, RecoveryPlanner.OrderSwitchCandidates(profiles, "Office").Single().Ssid);
    }
}

public sealed class MonitoringEngineTests : IDisposable
{
    private readonly FakeWifiClient _wifi = new();
    private readonly FakeConnectivityTester _tester = new();
    private readonly FakeCommandRunner _commands = new();
    private readonly MonitorPolicy _policy = new()
    {
        CheckIntervalSeconds = 3600,
        ConsecutiveFailuresBeforeRecovery = 2,
        RecoveryCooldownSeconds = 300,
        EnableDnsFlush = false,
        EnableCurrentWifiReconnect = false,
        EnableWifiAdapterAutoEnable = false
    };
    private readonly List<WifiProfile> _profiles = [];
    private readonly List<EngineEventArgs> _events = [];
    private readonly List<CheckCompletedEventArgs> _checks = [];
    private readonly MonitoringEngine _engine;

    public MonitoringEngineTests()
    {
        _engine = new MonitoringEngine(_wifi, _tester, _commands, () => _policy, () => _profiles)
        {
            SsidPollInterval = TimeSpan.Zero,
            AdapterEnableSettleDelay = TimeSpan.Zero,
            WifiValidationTimeout = TimeSpan.FromMilliseconds(100),
            WifiRestoreTimeout = TimeSpan.FromMilliseconds(100)
        };
        _engine.EngineEvent += (_, e) => { lock (_events) { _events.Add(e); } };
        _engine.CheckCompleted += (_, e) => { lock (_checks) { _checks.Add(e); } };
    }

    private IReadOnlyList<EngineEventKind> Kinds
    {
        get { lock (_events) { return _events.Select(e => e.Kind).ToList(); } }
    }

    [Fact]
    public async Task HealthyCheck_ResetsStreak_NoRecovery()
    {
        _tester.NextState = NetworkState.Healthy;
        _engine.Start();
        await _engine.CheckNowAsync();

        Assert.Equal(0, _engine.FailureStreak);
        Assert.DoesNotContain(EngineEventKind.RecoveryStarted, Kinds);
        Assert.Single(_checks);
    }

    [Fact]
    public async Task FailuresBelowThreshold_DoNotTriggerRecovery()
    {
        _tester.NextState = NetworkState.InternetIssue;
        _engine.Start();
        await _engine.CheckNowAsync();

        Assert.Equal(1, _engine.FailureStreak);
        Assert.DoesNotContain(EngineEventKind.RecoveryStarted, Kinds);
    }

    [Fact]
    public async Task NotMonitoring_NeverTriggersRecovery()
    {
        _tester.NextState = NetworkState.InternetIssue;
        await _engine.CheckNowAsync();
        await _engine.CheckNowAsync();
        await _engine.CheckNowAsync();

        Assert.Equal(3, _engine.FailureStreak);
        Assert.DoesNotContain(EngineEventKind.RecoveryStarted, Kinds);
    }

    [Fact]
    public async Task ThresholdReached_SwitchesToPriorityProfile_AndStopsOnSuccess()
    {
        _profiles.Add(new WifiProfile { Ssid = "Backup2", Priority = 2, CredentialSaved = true });
        _profiles.Add(new WifiProfile { Ssid = "Backup1", Priority = 1, CredentialSaved = true });
        _policy.EnableAutoSwitch = true;
        _tester.NextState = NetworkState.InternetIssue;
        _wifi.ConnectSucceedsFor.Add("Backup1");
        _tester.HealthySsids.Add("Backup1");

        _engine.Start();
        await _engine.CheckNowAsync();
        await _engine.CheckNowAsync();

        Assert.Contains(EngineEventKind.RecoveryStarted, Kinds);
        Assert.Equal(new[] { "Backup1" }, _wifi.ConnectAttempts);
        Assert.Contains(EngineEventKind.RecoveryWifiConnected, Kinds);
        Assert.DoesNotContain(EngineEventKind.RecoveryFailed, Kinds);
    }

    [Fact]
    public async Task AllSwitchCandidatesFail_RaisesRecoveryFailed_AndTriesInPriorityOrder()
    {
        _profiles.Add(new WifiProfile { Ssid = "B", Priority = 2, CredentialSaved = true });
        _profiles.Add(new WifiProfile { Ssid = "A", Priority = 1, CredentialSaved = true });
        _policy.EnableAutoSwitch = true;
        _tester.NextState = NetworkState.InternetIssue;

        _engine.Start();
        await _engine.CheckNowAsync();
        await _engine.CheckNowAsync();

        Assert.Equal(new[] { "A", "B" }, _wifi.ConnectAttempts);
        Assert.Contains(EngineEventKind.RecoveryFailed, Kinds);
    }

    [Fact]
    public async Task SecondRecoveryWithinCooldown_IsSuppressed()
    {
        _policy.EnableAutoSwitch = true;
        _tester.NextState = NetworkState.InternetIssue;

        _engine.Start();
        await _engine.CheckNowAsync();
        await _engine.CheckNowAsync(); // triggers first recovery (no candidates -> NoActionsEnabled? none enabled but AutoSwitch on with 0 profiles => attempted=false)
        await _engine.CheckNowAsync(); // within cooldown

        Assert.Contains(EngineEventKind.RecoveryCooldownActive, Kinds);
        Assert.Equal(1, Kinds.Count(kind => kind == EngineEventKind.RecoveryStarted));
    }

    [Fact]
    public async Task DnsFlushEnabled_RunsCommand()
    {
        _policy.EnableDnsFlush = true;
        _tester.NextState = NetworkState.DnsIssue;

        _engine.Start();
        await _engine.CheckNowAsync();
        await _engine.CheckNowAsync();

        Assert.Contains(("ipconfig.exe", "/flushdns"), _commands.Invocations);
        Assert.Contains(EngineEventKind.RecoveryDnsFlushCompleted, Kinds);
    }

    [Fact]
    public async Task TesterThrows_CountsAsFailure_AndRaisesCheckError()
    {
        _tester.ThrowOnCheck = true;
        await _engine.CheckNowAsync();

        Assert.Equal(1, _engine.FailureStreak);
        Assert.Contains(EngineEventKind.CheckError, Kinds);
    }

    [Fact]
    public void StartStop_TogglesStateAndEvents()
    {
        _engine.Start();
        Assert.True(_engine.IsMonitoring);
        Assert.NotNull(_engine.NextCheckAt);

        _engine.Stop();
        Assert.False(_engine.IsMonitoring);
        Assert.Null(_engine.NextCheckAt);
        Assert.Contains(EngineEventKind.MonitorStarted, Kinds);
        Assert.Contains(EngineEventKind.MonitorStopped, Kinds);
    }

    public void Dispose() => _engine.Dispose();
}

internal sealed class FakeConnectivityTester : IConnectivityTester
{
    public NetworkState NextState { get; set; } = NetworkState.Healthy;
    public bool ThrowOnCheck { get; set; }
    public HashSet<string> HealthySsids { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<ConnectivityReport> RunFullCheckAsync(
        MonitorPolicy policy, string? currentSsid, WifiAdapterDiagnostics? wifiDiagnostics = null,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnCheck)
        {
            throw new InvalidOperationException("probe failure");
        }

        return Task.FromResult(new ConnectivityReport
        {
            State = currentSsid is not null && HealthySsids.Contains(currentSsid) ? NetworkState.Healthy : NextState,
            CurrentSsid = currentSsid,
            Summary = NextState.ToString()
        });
    }
}

internal sealed class FakeCommandRunner : ISystemCommandRunner
{
    public List<(string FileName, string Args)> Invocations { get; } = [];

    public Action<string>? OnRun { get; set; }

    public Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        Invocations.Add((fileName, string.Join(' ', arguments)));
        OnRun?.Invoke(fileName);
        return Task.FromResult(new SystemCommandResult(0, ""));
    }
}

internal sealed class FakeWifiClient : IWifiClient
{
    public string? CurrentSsid { get; set; }
    public HashSet<string> ConnectSucceedsFor { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ConnectAttempts { get; } = [];
    public Func<CancellationToken, Task<IReadOnlyList<WifiNetwork>>>? ScanHandler { get; set; }
    public Func<string?>? ReadCurrentSsid { get; set; }
    public Func<string, CancellationToken, Task<bool>>? ConnectHandler { get; set; }
    public WifiAdapterDiagnostics? Diagnostics { get; set; }

    public Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken = default)
        => ScanHandler?.Invoke(cancellationToken) ?? Task.FromResult<IReadOnlyList<WifiNetwork>>([]);

    public Task<string?> GetCurrentSsidAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ReadCurrentSsid is null ? CurrentSsid : ReadCurrentSsid());

    public Task<WifiAdapterDiagnostics> GetAdapterDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Diagnostics ?? new WifiAdapterDiagnostics(
            "Wi-Fi", AdapterPresent: true, AdapterUp: true,
            HardwareRadioOn: true, SoftwareRadioOn: true,
            State: "connected", Detail: ""));

    public Task<IReadOnlyList<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<bool> TryEnableWifiAdapterAsync(string? adapterName, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<string?> GetSavedPasswordAsync(string ssid, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<bool> SaveProfileAsync(string ssid, string? password, string authentication, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> SaveProfileAsync(string profileName, string ssid, string? password, string authentication, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> ConnectAsync(string ssid, CancellationToken cancellationToken = default)
    {
        ConnectAttempts.Add(ssid);
        if (ConnectHandler is not null) return ConnectHandler(ssid, cancellationToken);
        if (ConnectSucceedsFor.Contains(ssid))
        {
            CurrentSsid = ssid;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> ConnectProfileAsync(string profileName, string ssid, CancellationToken cancellationToken = default)
        => ConnectAsync(ssid, cancellationToken);

    public Task<bool> DeleteProfileAsync(string profileName, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        CurrentSsid = null;
        return Task.FromResult(true);
    }
}
