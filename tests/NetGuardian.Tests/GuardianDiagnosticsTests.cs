using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Runtime;
using NetGuardian.Core.Settings;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class GuardianDiagnosticsTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void 联网健康但缺少备用候选时不能通过无人值守诊断(int available, bool expected)
    {
        var profiles = Enumerable.Range(0, 2).Select(index => new DiagnosticProfile($"网络{index}", true, true, index < available)).ToArray();
        var result = new GuardianDiagnosticReport(true, null, profiles,
            new WifiAdapterDiagnostics("测试", true, true, true, true, "connected", ""),
            new ConnectivityReport { State = NetworkState.Healthy });
        Assert.Equal(expected, result.ReadyForUnattendedRecovery);
    }

    [Fact]
    public async Task 部署诊断只读取配置与联网状态且不执行恢复()
    {
        var settings = new AppSettings { Profiles = [new WifiProfile { Ssid = "独立备用", CredentialSaved = false }] };
        var result = await GuardianDiagnostics.RunAsync(settings, new ReadOnlyWifi(), new FakeConnectivityTester { NextState = NetworkState.WifiDisabled });
        Assert.True(result.InventorySuccess);
        Assert.True(result.Profiles.Single().Available);
        Assert.Equal(NetworkState.WifiDisabled, result.Connectivity.State);
    }

    private sealed class ReadOnlyWifi : IWifiClient
    {
        public Task<string?> GetCurrentSsidAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<WifiAdapterDiagnostics> GetAdapterDiagnosticsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new WifiAdapterDiagnostics("测试", true, false, true, true, "disconnected", ""));
        public Task<IReadOnlyList<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(["独立备用"]);
        public Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("诊断不应扫描或修改网络");
        public Task<bool> TryEnableWifiAdapterAsync(string? adapterName, CancellationToken cancellationToken = default) => throw new InvalidOperationException("不应启用网卡");
        public Task<string?> GetSavedPasswordAsync(string ssid, CancellationToken cancellationToken = default) => throw new InvalidOperationException("不应读取密码");
        public Task<bool> SaveProfileAsync(string ssid, string? password, string authentication, CancellationToken cancellationToken = default) => throw new InvalidOperationException("不应保存凭据");
        public Task<bool> SaveProfileAsync(string profileName, string ssid, string? password, string authentication, CancellationToken cancellationToken = default) => throw new InvalidOperationException("不应保存凭据");
        public Task<bool> ConnectAsync(string ssid, CancellationToken cancellationToken = default) => throw new InvalidOperationException("不应连接网络");
        public Task<bool> ConnectProfileAsync(string profileName, string ssid, CancellationToken cancellationToken = default) => throw new InvalidOperationException("不应连接网络");
        public Task<bool> DeleteProfileAsync(string profileName, CancellationToken cancellationToken = default) => throw new InvalidOperationException("不应删除配置");
        public Task<bool> DisconnectAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("不应断开网络");
    }
}
