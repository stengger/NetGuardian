using System.Net;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.Portal;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class PortalRecoveryEngineTests
{
    private static WifiProfile CreatePortalProfile(string ssid, int priority, string credentialId, bool paused = false) => new()
    {
        Ssid = ssid,
        Priority = priority,
        CredentialSaved = true,
        PortalAuth = new PortalAuthConfig
        {
            Enabled = true,
            Ready = true,
            Paused = paused,
            CredentialId = credentialId,
            AllowedOrigin = "https://portal.campus.invalid",
            AllowedLoginPath = "/login",
            AllowedSubmitPath = "/sign-in",
            UsernameSelector = "#user",
            PasswordSelector = "#pass",
            SubmitSelector = "#submit"
        }
    };

    private static MonitorPolicy Policy(int failures = 1, bool autoSwitch = true, bool enableHighRisk = true) => new()
    {
        ConsecutiveFailuresBeforeRecovery = failures,
        RecoveryCooldownSeconds = 30,
        EnableAutoSwitch = autoSwitch,
        EnableCurrentWifiReconnect = true,
        EnableHighRiskRecovery = enableHighRisk,
        HighRiskAfterFailedRounds = 1,
        HighRiskCooldownMinutes = 5
    };

    [Fact]
    public async Task 当前网门户原地认证成功并经公网复测确认时不触发切网()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "CampusNet" };
        var tester = new PortalAwareTester
        {
            NextStateBySsid = { ["CampusNet"] = (NetworkState.InternetIssue, PortalEvidence.Redirect, new Uri("https://portal.campus.invalid/login?s=1")) }
        };
        var credId = Guid.NewGuid().ToString("N");
        var creds = new InMemoryPortalCredentialStore();
        creds.Save(credId, new PortalCredential("student", "secret"));
        var auth = new FakePortalAuthenticator
        {
            OnAuthenticate = req =>
            {
                tester.NextStateBySsid["CampusNet"] = (NetworkState.Healthy, PortalEvidence.None, null);
                return Task.FromResult(new PortalAuthResult(PortalAuthOutcome.Submitted, "已提交", "https://portal.campus.invalid/login"));
            }
        };

        var profiles = new[]
        {
            CreatePortalProfile("CampusNet", 1, credId),
            new WifiProfile { Ssid = "BackupNet", Priority = 2, CredentialSaved = true }
        };

        using var engine = new MonitoringEngine(wifi, tester, new FakeCommandRunner(), () => Policy(), () => profiles, portalAuthenticator: auth, portalCredentials: creds)
        {
            WifiValidationTimeout = TimeSpan.FromMilliseconds(200),
            WifiRestoreTimeout = TimeSpan.FromMilliseconds(200),
            SsidPollInterval = TimeSpan.FromMilliseconds(10)
        };
        var events = new List<EngineEventKind>();
        engine.EngineEvent += (_, e) => events.Add(e.Kind);

        engine.Start();
        await engine.CheckNowAsync();

        Assert.Equal(1, auth.AuthCalls);
        Assert.Empty(wifi.ConnectAttempts);
        Assert.Contains(EngineEventKind.RecoveryPortalAuthStarted, events);
        Assert.Contains(EngineEventKind.RecoveryWifiConnected, events);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public async Task 认证声称成功但公网仍不通时立即切换备用WiFi()
    {
        var wifi = new FakeWifiClient
        {
            CurrentSsid = "CampusNet",
            ConnectSucceedsFor = { "BackupNet" },
            ScanHandler = _ => Task.FromResult<IReadOnlyList<WifiNetwork>>([new WifiNetwork("BackupNet", 80, "WPA2-Personal", "AES", true, true)])
        };
        var tester = new PortalAwareTester
        {
            NextStateBySsid =
            {
                ["CampusNet"] = (NetworkState.InternetIssue, PortalEvidence.Redirect, new Uri("https://portal.campus.invalid/login?s=1")),
                ["BackupNet"] = (NetworkState.Healthy, PortalEvidence.None, null)
            }
        };
        var credId = Guid.NewGuid().ToString("N");
        var creds = new InMemoryPortalCredentialStore();
        creds.Save(credId, new PortalCredential("student", "secret"));
        var auth = new FakePortalAuthenticator
        {
            OnAuthenticate = _ => Task.FromResult(new PortalAuthResult(PortalAuthOutcome.Submitted, "已提交", "https://portal.campus.invalid/login"))
        };
        var profiles = new[]
        {
            CreatePortalProfile("CampusNet", 1, credId),
            new WifiProfile { Ssid = "BackupNet", Priority = 2, CredentialSaved = true }
        };

        using var engine = new MonitoringEngine(wifi, tester, new FakeCommandRunner(), () => Policy(), () => profiles, portalAuthenticator: auth, portalCredentials: creds)
        {
            WifiValidationTimeout = TimeSpan.FromMilliseconds(500),
            SsidPollInterval = TimeSpan.FromMilliseconds(10)
        };

        engine.Start();
        await engine.CheckNowAsync();

        Assert.Equal("BackupNet", wifi.CurrentSsid);
        Assert.Equal(["BackupNet"], wifi.ConnectAttempts);
    }

    [Fact]
    public async Task 密码错误或欠费时暂停当前配置跳过重连并切到备用网且下轮不重复提交()
    {
        var wifi = new FakeWifiClient
        {
            CurrentSsid = "CampusNet",
            ConnectSucceedsFor = { "BackupNet" },
            ScanHandler = _ => Task.FromResult<IReadOnlyList<WifiNetwork>>([new WifiNetwork("BackupNet", 80, "WPA2-Personal", "AES", true, true)])
        };
        var tester = new PortalAwareTester
        {
            NextStateBySsid =
            {
                ["CampusNet"] = (NetworkState.InternetIssue, PortalEvidence.Redirect, new Uri("https://portal.campus.invalid/login?s=1")),
                ["BackupNet"] = (NetworkState.Healthy, PortalEvidence.None, null)
            }
        };
        var credId = Guid.NewGuid().ToString("N");
        var creds = new InMemoryPortalCredentialStore();
        creds.Save(credId, new PortalCredential("student", "wrong"));
        var auth = new FakePortalAuthenticator
        {
            OnAuthenticate = _ => Task.FromResult(new PortalAuthResult(PortalAuthOutcome.CredentialsRejected, "密码错误", "https://portal.campus.invalid/login", ShouldPause: true))
        };
        var profiles = new[]
        {
            CreatePortalProfile("CampusNet", 1, credId),
            new WifiProfile { Ssid = "BackupNet", Priority = 2, CredentialSaved = true }
        };

        using var engine = new MonitoringEngine(wifi, tester, new FakeCommandRunner(), () => Policy(autoSwitch: true), () => profiles, portalAuthenticator: auth, portalCredentials: creds)
        {
            WifiValidationTimeout = TimeSpan.FromMilliseconds(500),
            SsidPollInterval = TimeSpan.FromMilliseconds(10)
        };
        var paused = new List<string?>();
        engine.EngineEvent += (_, e) =>
        {
            if (e.Kind == EngineEventKind.RecoveryPortalAuthPaused) paused.Add(e.Args[0]?.ToString());
        };

        engine.Start();
        await engine.CheckNowAsync();

        Assert.Single(paused, "CampusNet");
        Assert.Equal(["BackupNet"], wifi.ConnectAttempts);

        // 模拟再次切回 CampusNet 且失败，引擎内存暂停标记应阻止重复提交错误密码。
        wifi.CurrentSsid = "CampusNet";
        wifi.ConnectSucceedsFor.Clear();
        await Task.Delay(50);
        await engine.CheckNowAsync();
        Assert.Equal(1, auth.AuthCalls);
    }

    [Fact]
    public async Task 认证剩余预算不足以预留备用切换时跳过原地认证直接切网()
    {
        var wifi = new FakeWifiClient
        {
            CurrentSsid = "CampusNet",
            ConnectSucceedsFor = { "BackupNet" },
            ScanHandler = _ => Task.FromResult<IReadOnlyList<WifiNetwork>>([new WifiNetwork("BackupNet", 80, "WPA2-Personal", "AES", true, true)])
        };
        var tester = new PortalAwareTester
        {
            NextStateBySsid =
            {
                ["CampusNet"] = (NetworkState.InternetIssue, PortalEvidence.Redirect, new Uri("https://portal.campus.invalid/login?s=1")),
                ["BackupNet"] = (NetworkState.Healthy, PortalEvidence.None, null)
            }
        };
        var credId = Guid.NewGuid().ToString("N");
        var creds = new InMemoryPortalCredentialStore();
        creds.Save(credId, new PortalCredential("student", "secret"));
        var auth = new FakePortalAuthenticator();
        var profiles = new[]
        {
            CreatePortalProfile("CampusNet", 1, credId),
            new WifiProfile { Ssid = "BackupNet", Priority = 2, CredentialSaved = true }
        };

        using var engine = new MonitoringEngine(wifi, tester, new FakeCommandRunner(), () => Policy(), () => profiles, portalAuthenticator: auth, portalCredentials: creds)
        {
            CheckTimeout = TimeSpan.FromSeconds(5),
            BackupSwitchReserveBudget = TimeSpan.FromSeconds(10),
            WifiValidationTimeout = TimeSpan.FromMilliseconds(500),
            SsidPollInterval = TimeSpan.FromMilliseconds(10)
        };

        engine.Start();
        await engine.CheckNowAsync();

        Assert.Equal(0, auth.AuthCalls);
        Assert.Equal("BackupNet", wifi.CurrentSsid);
    }

    [Fact]
    public async Task 备用网连接后也需要认证时自动完成认证与公网验证()
    {
        var wifi = new FakeWifiClient
        {
            CurrentSsid = "DeadNet",
            ConnectSucceedsFor = { "CampusBackup" },
            ScanHandler = _ => Task.FromResult<IReadOnlyList<WifiNetwork>>([new WifiNetwork("CampusBackup", 80, "WPA2-Personal", "AES", true, true)])
        };
        var credId = Guid.NewGuid().ToString("N");
        var creds = new InMemoryPortalCredentialStore();
        creds.Save(credId, new PortalCredential("student", "secret"));
        var tester = new PortalAwareTester
        {
            NextStateBySsid =
            {
                ["DeadNet"] = (NetworkState.InternetIssue, PortalEvidence.None, null),
                ["CampusBackup"] = (NetworkState.InternetIssue, PortalEvidence.Redirect, new Uri("https://portal.campus.invalid/login?s=2"))
            }
        };
        var auth = new FakePortalAuthenticator
        {
            OnAuthenticate = req =>
            {
                Assert.Equal("CampusBackup", req.Ssid);
                tester.NextStateBySsid["CampusBackup"] = (NetworkState.Healthy, PortalEvidence.None, null);
                return Task.FromResult(new PortalAuthResult(PortalAuthOutcome.Submitted, "已提交", "https://portal.campus.invalid/login"));
            }
        };
        var profiles = new[]
        {
            new WifiProfile { Ssid = "DeadNet", Priority = 1, CredentialSaved = true },
            CreatePortalProfile("CampusBackup", 2, credId)
        };

        using var engine = new MonitoringEngine(wifi, tester, new FakeCommandRunner(), () => Policy(), () => profiles, portalAuthenticator: auth, portalCredentials: creds)
        {
            WifiValidationTimeout = TimeSpan.FromMilliseconds(500),
            WifiRestoreTimeout = TimeSpan.FromMilliseconds(200),
            SsidPollInterval = TimeSpan.FromMilliseconds(10)
        };

        engine.Start();
        await engine.CheckNowAsync();

        Assert.Equal(1, auth.AuthCalls);
        Assert.Equal("CampusBackup", wifi.CurrentSsid);
        Assert.Equal(0, engine.FailureStreak);
    }

    [Fact]
    public async Task 复测期间无线连接切换时作废认证结果并避免升级设备重启()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "CampusNet" };
        var credId = Guid.NewGuid().ToString("N");
        var creds = new InMemoryPortalCredentialStore();
        creds.Save(credId, new PortalCredential("student", "secret"));
        var tester = new PortalAwareTester
        {
            NextStateBySsid =
            {
                ["CampusNet"] = (NetworkState.InternetIssue, PortalEvidence.Redirect, new Uri("https://portal.campus.invalid/login?s=1")),
                ["OtherNet"] = (NetworkState.Healthy, PortalEvidence.None, null)
            }
        };
        var auth = new FakePortalAuthenticator
        {
            OnAuthenticate = _ =>
            {
                wifi.CurrentSsid = "OtherNet";
                return Task.FromResult(new PortalAuthResult(PortalAuthOutcome.Submitted, "已提交"));
            }
        };
        var commands = new FakeCommandRunner();
        var profiles = new[] { CreatePortalProfile("CampusNet", 1, credId) };

        using var engine = new MonitoringEngine(wifi, tester, commands, () => Policy(autoSwitch: false, enableHighRisk: true), () => profiles, portalAuthenticator: auth, portalCredentials: creds)
        {
            WifiRestoreTimeout = TimeSpan.FromMilliseconds(200),
            SsidPollInterval = TimeSpan.FromMilliseconds(10)
        };
        var events = new List<EngineEventKind>();
        engine.EngineEvent += (_, e) => events.Add(e.Kind);

        engine.Start();
        await engine.CheckNowAsync();

        Assert.DoesNotContain(EngineEventKind.RecoveryWifiConnected, events);
        Assert.DoesNotContain(EngineEventKind.HighRiskActionStarted, events);
    }

    [Fact]
    public async Task 认证事件正确记录到故障时间线且无明文凭据()
    {
        var logDir = Path.Combine(Path.GetTempPath(), "NetGuardian-PortalJournal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new EventJournal(logDir);
            var wifi = new FakeWifiClient { CurrentSsid = "CampusNet" };
            var tester = new PortalAwareTester
            {
                NextStateBySsid = { ["CampusNet"] = (NetworkState.InternetIssue, PortalEvidence.Redirect, new Uri("https://portal.campus.invalid/login?secret=querytoken")) }
            };
            var credId = Guid.NewGuid().ToString("N");
            var creds = new InMemoryPortalCredentialStore();
            creds.Save(credId, new PortalCredential("secret-student", "super-secret-password"));
            var auth = new FakePortalAuthenticator
            {
                OnAuthenticate = _ =>
                {
                    tester.NextStateBySsid["CampusNet"] = (NetworkState.Healthy, PortalEvidence.None, null);
                    return Task.FromResult(new PortalAuthResult(PortalAuthOutcome.Submitted, "已提交", "https://portal.campus.invalid/login"));
                }
            };
            var profiles = new[] { CreatePortalProfile("CampusNet", 1, credId) };

            using var engine = new MonitoringEngine(wifi, tester, new FakeCommandRunner(), () => Policy(), () => profiles, portalAuthenticator: auth, portalCredentials: creds);
            journal.ObserveIncidents(engine, "后台守护");
            engine.Start();
            await engine.CheckNowAsync();

            var files = Directory.GetFiles(logDir, "events-*.jsonl");
            Assert.Single(files);
            var text = await File.ReadAllTextAsync(files[0]);
            Assert.DoesNotContain("super-secret-password", text, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-student", text, StringComparison.Ordinal);
            Assert.DoesNotContain("querytoken", text, StringComparison.Ordinal);

            var incidents = new IncidentHistoryReader([logDir]).Read();
            var incident = Assert.Single(incidents);
            Assert.Contains(incident.Steps, step => step.Detail is not null && step.Detail.Contains("尝试 Wi-Fi 网页认证", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(logDir)) Directory.Delete(logDir, recursive: true);
        }
    }

    private sealed class PortalAwareTester : IConnectivityTester
    {
        public Dictionary<string, (NetworkState State, PortalEvidence Evidence, Uri? EntryUri)> NextStateBySsid { get; } = new(StringComparer.Ordinal);

        public Task<ConnectivityReport> RunFullCheckAsync(
            MonitorPolicy policy, string? currentSsid, WifiAdapterDiagnostics? wifiDiagnostics = null,
            CancellationToken cancellationToken = default)
        {
            var info = currentSsid is not null && NextStateBySsid.TryGetValue(currentSsid, out var entry)
                ? entry
                : (NetworkState.InternetIssue, PortalEvidence.None, (Uri?)null);

            var probes = info.Item1 == NetworkState.Healthy
                ? new List<ConnectivityProbe> { new("HTTPS www.microsoft.com", true, 12, "HTTP 200") }
                : new List<ConnectivityProbe> { new("HTTPS www.microsoft.com", false, 12, "HTTP 503") };

            return Task.FromResult(new ConnectivityReport
            {
                State = info.Item1,
                PortalEvidence = info.Item2,
                PortalEntryUri = info.Item3,
                PortalSanitizedOrigin = PortalDiscoveryResult.SanitizeUri(info.Item3),
                CurrentSsid = currentSsid,
                Ipv4 = "192.0.2.10",
                WifiInterfaceIndex = 12,
                Probes = probes,
                Summary = info.Item1.ToString()
            });
        }
    }

    private sealed class FakePortalAuthenticator : IPortalAuthenticator
    {
        public int AuthCalls { get; private set; }
        public Func<PortalAuthRequest, Task<PortalAuthResult>> OnAuthenticate { get; set; }
            = _ => Task.FromResult(new PortalAuthResult(PortalAuthOutcome.Failed, "未配置"));

        public Task<PortalAuthResult> AuthenticateAsync(PortalAuthRequest request, PortalCredential? credential, CancellationToken cancellationToken)
        {
            AuthCalls++;
            return OnAuthenticate(request);
        }

        public Task<PortalInspectionResult> InspectEntryAsync(Uri entryUri, IPAddress? wifiAddress, int? interfaceIndex, bool allowInsecureHttp, CancellationToken cancellationToken)
            => Task.FromResult(new PortalInspectionResult(false, "stub", null, null, null, null, true, [], [], [], []));
    }
}
