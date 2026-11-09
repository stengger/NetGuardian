using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.Settings;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class SavedWifiProfileImportTests
{
    [Fact]
    public void 自动导入当前网络优先且重复执行不重复添加()
    {
        var settings = new AppSettings();
        var inventory = new SavedWifiProfileInventory(true,
            [new("备用配置", "备用", "WPA2-Personal"), new("主配置", "当前"), new("重复配置", "备用")]);
        Assert.Equal(new WifiProfileImportResult(2, true), SavedWifiProfileImporter.Apply(settings, inventory, "当前"));
        Assert.Equal(new[] { "当前", "备用" }, settings.Profiles.Select(profile => profile.Ssid));
        Assert.Equal(new[] { 1, 2 }, settings.Profiles.Select(profile => profile.Priority));
        Assert.All(settings.Profiles, profile => Assert.True(profile.CredentialSaved && profile.Enabled && profile.AutoConnect));
        Assert.Equal("备用配置", settings.Profiles[1].ConnectionProfileName);
        Assert.Equal("WPA2-Personal", settings.Profiles[1].Security);
        Assert.Equal(new WifiProfileImportResult(0, false), SavedWifiProfileImporter.Apply(settings, inventory, "当前"));
    }

    [Fact]
    public void 已有策略和排序不变且新网络追加至末尾()
    {
        var existing = new WifiProfile { Ssid = "已有", Priority = 7, Enabled = false, AutoConnect = false,
            Notes = "保留备注", FailureCount = 3, CredentialSaved = false };
        var settings = new AppSettings { Profiles = [existing] };
        var result = SavedWifiProfileImporter.Apply(settings,
            new(true, [new("已有配置", "已有"), new("当前配置", "当前")]), "当前");
        Assert.Equal(1, result.AddedCount);
        Assert.Same(existing, settings.Profiles[0]);
        Assert.False(existing.Enabled);
        Assert.False(existing.AutoConnect);
        Assert.Equal(7, existing.Priority);
        Assert.Equal("保留备注", existing.Notes);
        Assert.Equal(3, existing.FailureCount);
        Assert.Equal("已有配置", existing.ConnectionProfileName);
        Assert.Equal(8, settings.Profiles[1].Priority);
    }

    [Fact]
    public void 大小写及首尾空格不同的SSID分别导入()
    {
        var settings = new AppSettings();
        SavedWifiProfileImporter.Apply(settings,
            new(true, [new("A", "Office"), new("B", "office"), new("C", " Office ")]), null);
        Assert.Equal(new[] { "Office", "office", " Office " }, settings.Profiles.Select(profile => profile.Ssid));
    }

    [Fact]
    public void 排除手动移除的网络和临时配置但不影响名称相似的网络()
    {
        var settings = new AppSettings { ExcludedWifiSsids = ["Office"] };
        SavedWifiProfileImporter.Apply(settings, new(true,
            [new("Office", "Office"), new("office", "office"), new("NetGuardian-Verify-test", "临时"), new("", "无效")]), null);
        Assert.Equal("office", Assert.Single(settings.Profiles).Ssid);
    }

    [Fact]
    public void 查询失败不修改已有配置或导入部分结果()
    {
        var profile = new WifiProfile { Ssid = "原网络", CredentialSaved = true };
        var settings = new AppSettings { Profiles = [profile] };
        var result = SavedWifiProfileImporter.Apply(settings, new(false, [new("新网络", "新网络")], "访问被拒绝"), null);
        Assert.Equal(new WifiProfileImportResult(0, false), result);
        Assert.Same(profile, Assert.Single(settings.Profiles));
        Assert.True(profile.CredentialSaved);
    }

    [Fact]
    public void 成功空列表只更新凭据状态且不会移除原网络()
    {
        var settings = new AppSettings { Profiles = [new() { Ssid = "离线", CredentialSaved = true }] };
        Assert.True(SavedWifiProfileImporter.Apply(settings, new(true, []), null).Changed);
        Assert.False(Assert.Single(settings.Profiles).CredentialSaved);
    }

    [Fact]
    public void 同一SSID存在多个配置时保留现有配置映射()
    {
        var settings = new AppSettings { Profiles = [new() { Ssid = "网络", WindowsProfileName = "B", CredentialSaved = true }] };
        var result = SavedWifiProfileImporter.Apply(settings, new(true, [new("A", "网络"), new("B", "网络")]), null);
        Assert.False(result.Changed);
        Assert.Equal("B", settings.Profiles[0].ConnectionProfileName);
        WifiProfileAvailability.Apply(settings.Profiles, new(true, ["B"]));
        Assert.True(settings.Profiles[0].CredentialSaved);
    }

    [Fact]
    public void 配置名与排除记录可持久化且旧配置兼容()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NetGuardian-import-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        try
        {
            var settings = new AppSettings { ExcludedWifiSsids = ["已移除"] };
            SavedWifiProfileImporter.Apply(settings, new(true, [new("配置名", "真实SSID")]), null);
            store.Save(settings);
            var loaded = store.Load();
            Assert.Equal("配置名", Assert.Single(loaded.Profiles).ConnectionProfileName);
            Assert.Equal("已移除", Assert.Single(loaded.ExcludedWifiSsids));
            Assert.DoesNotContain("password", File.ReadAllText(store.Path), StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(store.Path, "{\"profiles\":[{\"ssid\":\"旧网络\"}],\"excludedWifiSsids\":null}");
            loaded = store.Load();
            Assert.Empty(loaded.ExcludedWifiSsids);
            Assert.Equal("旧网络", Assert.Single(loaded.Profiles).ConnectionProfileName);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 恢复切换和当前网络重连使用真实配置名(bool reconnect)
    {
        var wifi = new FakeWifiClient { CurrentSsid = reconnect ? "目标SSID" : "原网络" };
        wifi.ConnectSucceedsFor.Add("目标SSID");
        var probes = 0;
        var tester = new ScriptedTester((ssid, _) => Task.FromResult(new ConnectivityReport
        {
            CurrentSsid = ssid,
            State = ++probes == 1 ? NetworkState.InternetIssue : NetworkState.Healthy
        }));
        var policy = new MonitorPolicy { ConsecutiveFailuresBeforeRecovery = 1,
            EnableCurrentWifiReconnect = reconnect, EnableAutoSwitch = !reconnect };
        using var engine = new MonitoringEngine(wifi, tester, new FakeCommandRunner(), () => policy,
            () => [new WifiProfile { Ssid = "目标SSID", WindowsProfileName = "真实配置名", CredentialSaved = true }]);
        engine.Start();
        await engine.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(("真实配置名", "目标SSID"), Assert.Single(wifi.ProfileConnectAttempts));
        Assert.Equal(0, engine.FailureStreak);
    }

    [Theory]
    [InlineData("SSID name : \" Office \"\nAuthentication : WPA2-Personal", " Office ")]
    [InlineData("SSID 名称 : \"实验室\"\n身份验证 : WPA2-Personal", "实验室")]
    [InlineData("SSID 名称 : “实验室”\n身份验证 : WPA2-Personal", "实验室")]
    [InlineData("SSID 名称 : “ Office ”\n身份验证 : WPA2-Personal", " Office ")]
    [InlineData("SSID 名称 : ““网络””\n身份验证 : WPA2-Personal", "“网络”")]
    public async Task 只读查询真实SSID及认证方式而不读取密钥(string detail, string expected)
    {
        var commands = new InventoryRunner(detail);
        var client = new WifiClient(fallback: new NetshWifiClient(commands));
        var result = await client.GetSavedProfilesAsync();
        Assert.True(result.Success);
        Assert.Equal(new SavedWifiProfile("配置名称", expected, "WPA2-Personal"), Assert.Single(result.Profiles));
        Assert.Equal(2, commands.Calls.Count);
        Assert.Equal(new[] { "wlan", "show", "profile", "name=配置名称" }, commands.Calls[1]);
        Assert.All(commands.Calls, call => Assert.DoesNotContain("key=clear", call));
    }

    [Theory]
    [InlineData(5, "访问被拒绝")]
    [InlineData(0, "缺少SSID字段")]
    public async Task 详情读取失败不能把配置名称误当SSID导入(int exitCode, string output)
    {
        var result = await new NetshWifiClient(new InventoryRunner(output, exitCode)).GetSavedProfilesAsync();
        Assert.False(result.Success);
        Assert.Empty(result.Profiles);
        Assert.Contains(output, result.Error);
    }

    [Fact]
    public async Task 取消查询不再启动系统命令()
    {
        var commands = new InventoryRunner("");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new NetshWifiClient(commands).GetSavedProfilesAsync(cancellation.Token));
        Assert.Empty(commands.Calls);
    }

    private sealed class InventoryRunner(string detail, int detailExitCode = 0) : ISystemCommandRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(arguments.ToArray());
            Assert.Equal("netsh.exe", fileName);
            return Task.FromResult(arguments.Contains("profiles")
                ? new SystemCommandResult(0, "All User Profile : NetGuardian-Verify-test\nAll User Profile : 配置名称")
                : new SystemCommandResult(detailExitCode, detail));
        }
    }
}
