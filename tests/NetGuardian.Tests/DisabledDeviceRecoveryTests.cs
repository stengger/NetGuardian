using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.Wifi;
using NetGuardian.Core.SystemCommands;
using Xunit;

namespace NetGuardian.Tests;

public sealed class DisabledDeviceRecoveryTests
{
    [Fact]
    public async Task 无WLAN接口但PnP设备仍在时应保留存在状态供自动启用()
    {
        var fallback = new FakeWifiClient { Diagnostics = new(null, false, false, null, null, "", "") };
        var inspector = new WifiDeviceInspector(new DisabledDeviceCommands());
        var client = new WifiClient(fallback: fallback, deviceInspector: inspector);
        var diagnostics = await client.GetAdapterDiagnosticsAsync();
        Assert.True(diagnostics.AdapterPresent);
        Assert.Equal(22, diagnostics.ProblemCode);
        Assert.Equal(@"PCI\VEN_14C3&DEV_7925\TEST", diagnostics.AdapterName);
        Assert.True(RecoveryPlanner.ShouldTryEnableWifi(new MonitorPolicy(), diagnostics));
    }

    private sealed class DisabledDeviceCommands : ISystemCommandRunner
    {
        public Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SystemCommandResult(0, """{"InstanceId":"PCI\\VEN_14C3&DEV_7925\\TEST","Name":"MediaTek Wi-Fi 7 MT7925","ProblemCode":22}"""));
    }

    [Fact]
    public async Task 禁用的PnP无线设备按实例启用而不是当成接口名称()
    {
        var commands = new FakeCommandRunner();
        var client = new NetshWifiClient(commands);
        const string instance = @"PCI\VEN_14C3&DEV_7925\TEST";
        Assert.True(await client.TryEnableWifiAdapterAsync(instance));
        var command = Assert.Single(commands.Invocations);
        Assert.Equal("pnputil.exe", command.FileName);
        Assert.Equal("/enable-device " + instance, command.Args);
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(22, true)]
    public void 只有被禁用的设备错误允许直接启用(int problemCode, bool expected)
    {
        var diagnostics = new WifiAdapterDiagnostics("设备", true, false, null, null, "", "", ProblemCode: problemCode);
        Assert.Equal(expected, RecoveryPlanner.ShouldTryEnableWifi(new MonitorPolicy(), diagnostics));
    }
}
