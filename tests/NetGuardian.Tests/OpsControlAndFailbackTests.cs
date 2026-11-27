using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class OpsControlAndFailbackTests
{
    [Fact]
    public void IsRecoverySuppressed_HandlesSameDayOvernightAndTemporaryPause()
    {
        var policy = new MonitorPolicy
        {
            EnableMaintenanceWindow = true,
            MaintenanceStartTime = "23:00",
            MaintenanceEndTime = "05:00"
        };
        policy.Normalize();

        var lateNight = new DateTimeOffset(2026, 11, 27, 23, 30, 0, TimeSpan.FromHours(8));
        var earlyMorning = new DateTimeOffset(2026, 11, 28, 4, 15, 0, TimeSpan.FromHours(8));
        var daytime = new DateTimeOffset(2026, 11, 27, 14, 0, 0, TimeSpan.FromHours(8));

        Assert.True(policy.IsRecoverySuppressed(lateNight, out _));
        Assert.True(policy.IsRecoverySuppressed(earlyMorning, out _));
        Assert.False(policy.IsRecoverySuppressed(daytime, out _));

        policy.PauseRecoveryUntilUtc = daytime.AddMinutes(30).ToUniversalTime();
        Assert.True(policy.IsRecoverySuppressed(daytime, out _));
        Assert.False(policy.IsRecoverySuppressed(daytime.AddMinutes(31), out _));
    }

    [Fact]
    public async Task Engine_SuppressesRecoveryDuringMaintenanceAndHighRiskWhenEthernetHealthy()
    {
        var t0 = new DateTimeOffset(2026, 11, 27, 2, 30, 0, TimeSpan.FromHours(8));
        var policy = new MonitorPolicy
        {
            ConsecutiveFailuresBeforeRecovery = 1,
            EnableMaintenanceWindow = true,
            MaintenanceStartTime = "02:00",
            MaintenanceEndTime = "05:00",
            EnableHighRiskRecovery = true,
            HighRiskAfterFailedRounds = 1,
            PreferEthernetWhenHealthy = true
        };
        policy.Normalize();

        var wifi = new StubWifiClient("Backup-WiFi", [new WifiNetwork("Primary-WiFi", 88, "WPA2-Personal", "CCMP", true, true)]);
        var tester = new StubConnectivityTester((_, _) => new ConnectivityReport
        {
            State = NetworkState.WifiDisconnected,
            Summary = "wifi disconnected"
        });
        var commands = new StubCommandRunner();
        var events = new List<EngineEventKind>();

        using var engine = new MonitoringEngine(
            wifi,
            tester,
            commands,
            () => policy,
            () => [new WifiProfile { Ssid = "Primary-WiFi", Priority = 0, Enabled = true, AutoConnect = true, CredentialSaved = true }],
            timeProvider: new StubTimeProvider(t0),
            wiredInternetProbe: (_, _) => Task.FromResult((true, (string?)"Ethernet")));

        engine.EngineEvent += (_, e) => events.Add(e.Kind);
        engine.Start();
        await engine.CheckNowAsync();

        Assert.Contains(EngineEventKind.RecoverySuppressedByMaintenanceOrPause, events);
        Assert.DoesNotContain(EngineEventKind.RecoveryStarted, events);

        // 关闭维护窗口但保持有线健康，应进入轻恢复但跳过高风险设备层动作。
        events.Clear();
        policy.EnableMaintenanceWindow = false;
        policy.EnableAutoSwitch = false;
        policy.EnableCurrentWifiReconnect = false;
        await engine.CheckNowAsync();

        Assert.Contains(EngineEventKind.RecoverySuppressedByHealthyEthernet, events);
        Assert.DoesNotContain(EngineEventKind.HighRiskActionStarted, events);
    }

    [Fact]
    public async Task Engine_AttemptsPrimaryFailbackAndRollsBackWhenPrimaryFailsValidation()
    {
        var t0 = new DateTimeOffset(2026, 11, 27, 15, 0, 0, TimeSpan.FromHours(8));
        var time = new StubTimeProvider(t0);
        var policy = new MonitorPolicy
        {
            EnablePrimaryFailback = true,
            PrimaryFailbackIntervalMinutes = 10
        };
        policy.Normalize();

        var profiles = new[]
        {
            new WifiProfile { Ssid = "Primary-WiFi", Priority = 0, Enabled = true, AutoConnect = true, CredentialSaved = true },
            new WifiProfile { Ssid = "Backup-WiFi", Priority = 1, Enabled = true, AutoConnect = true, CredentialSaved = true }
        };

        var wifi = new StubWifiClient(
            "Backup-WiFi",
            [
                new WifiNetwork("Primary-WiFi", 90, "WPA2-Personal", "CCMP", true, true),
                new WifiNetwork("Backup-WiFi", 75, "WPA2-Personal", "CCMP", true, true)
            ]);

        var tester = new StubConnectivityTester((ssid, _) =>
        {
            // 当切到 Primary-WiFi 时故意判定公网不可达，迫使回滚到 Backup-WiFi。
            if (ssid == "Primary-WiFi")
            {
                return new ConnectivityReport { State = NetworkState.InternetIssue, CurrentSsid = ssid, Summary = "primary no internet" };
            }
            return new ConnectivityReport { State = NetworkState.Healthy, CurrentSsid = ssid, Summary = "ok" };
        });

        var events = new List<EngineEventArgs>();
        using var engine = new MonitoringEngine(
            wifi,
            tester,
            new StubCommandRunner(),
            () => policy,
            () => profiles,
            timeProvider: time)
        {
            WifiValidationTimeout = TimeSpan.FromSeconds(2),
            SsidPollInterval = TimeSpan.FromMilliseconds(10)
        };
        engine.EngineEvent += (_, e) => events.Add(e);
        engine.Start();

        await engine.CheckNowAsync();

        Assert.Contains(events, e => e.Kind == EngineEventKind.PrimaryFailbackStarted);
        Assert.Contains(events, e => e.Kind == EngineEventKind.PrimaryFailbackRolledBack);
        Assert.Equal("Backup-WiFi", wifi.CurrentSsid);
    }

    private sealed class StubWifiClient : IWifiClient
    {
        private readonly IReadOnlyList<WifiNetwork> _scanList;
        public string? CurrentSsid { get; private set; }
        public string? LastConnectionFailure => null;
        public string? LastAdapterEnableFailure => null;

        public StubWifiClient(string? initialSsid, IReadOnlyList<WifiNetwork> scanList)
        {
            CurrentSsid = initialSsid;
            _scanList = scanList;
        }

        public Task<WifiConnectionInfo> GetConnectionInfoAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WifiConnectionInfo(true, CurrentSsid is not null, CurrentSsid, "WLAN", 80));

        public Task<string?> GetCurrentSsidAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CurrentSsid);

        public Task<WifiAdapterDiagnostics> GetAdapterDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WifiAdapterDiagnostics(
                AdapterName: "WLAN",
                AdapterPresent: true,
                AdapterUp: true,
                HardwareRadioOn: true,
                SoftwareRadioOn: true,
                State: "connected",
                Detail: "ok"));

        public Task<bool> TryEnableWifiAdapterAsync(string? adapterName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<WifiRecoveryScan> ScanForRecoveryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WifiRecoveryScan(WifiScanStatus.Completed, _scanList));

        public Task<WifiProfileInventory> GetProfileInventoryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WifiProfileInventory(true, []));

        public Task<IReadOnlyList<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(["Primary-WiFi", "Backup-WiFi"]);

        public Task<string?> GetSavedPasswordAsync(string ssid, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<bool> SaveProfileAsync(string ssid, string? password, string authentication, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> SaveProfileAsync(string profileName, string ssid, string? password, string authentication, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> DeleteProfileAsync(string profileName, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ConnectAsync(string ssid, CancellationToken cancellationToken = default)
        {
            CurrentSsid = ssid;
            return Task.FromResult(true);
        }

        public Task<bool> ConnectProfileAsync(string profileName, string ssid, CancellationToken cancellationToken = default)
        {
            CurrentSsid = ssid;
            return Task.FromResult(true);
        }

        public Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            CurrentSsid = null;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_scanList);
    }

    private sealed class StubConnectivityTester : IConnectivityTester
    {
        private readonly Func<string?, bool, ConnectivityReport> _factory;
        public StubConnectivityTester(Func<string?, bool, ConnectivityReport> factory) => _factory = factory;

        public Task<ConnectivityReport> RunFullCheckAsync(MonitorPolicy policy, string? currentSsid, WifiAdapterDiagnostics? wifiDiagnostics = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_factory(currentSsid, false));

        public Task<ConnectivityReport> RunLightweightCheckAsync(MonitorPolicy policy, CancellationToken cancellationToken = default)
            => Task.FromResult(_factory(null, true));
    }

    private sealed class StubCommandRunner : ISystemCommandRunner
    {
        public Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(new SystemCommandResult(0, ""));
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public StubTimeProvider(DateTimeOffset initial) => _now = initial;
        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
    }
}
