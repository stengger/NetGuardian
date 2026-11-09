using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class CurrentWifiConnectionTests
{
    [Theory]
    [InlineData("名称 : WLAN\n状态 : 已连接\nSSID :  中文网络 \n信号 : 89%\n身份验证 : WPA3-Personal\n密码 : GCMP-256", "WLAN", " 中文网络 ", 89, "WPA3-Personal")]
    [InlineData("Name : Wi-Fi\nState : connected\nSSID : Home\nSignal : 75%\nAuthentication : WPA2-Personal\nCipher : CCMP", "Wi-Fi", "Home", 75, "WPA2-Personal")]
    public void 当前连接读取中英文信号与安全并保留SSID空格(string output, string adapter, string ssid, int signal, string security)
    {
        var info = NetshWifiClient.ParseConnectionInfo(output);
        Assert.True(info.ReadSucceeded);
        Assert.True(info.Connected);
        Assert.Equal(ssid, info.Ssid);
        Assert.Equal(adapter, info.AdapterName);
        Assert.Equal(signal, info.SignalPercent);
        Assert.Equal(security, info.Authentication);
    }

    [Fact]
    public void 断开状态不能显示残留的网络名称或信号()
    {
        var info = NetshWifiClient.ParseConnectionInfo("Name : Wi-Fi\nState : disconnected\nSSID : Old\nSignal : 88%");
        Assert.True(info.ReadSucceeded);
        Assert.False(info.Connected);
        Assert.Null(info.Ssid);
        Assert.Null(info.SignalPercent);
    }

    [Fact]
    public async Task 系统查询失败须与未连接区分并保留错误()
    {
        var client = new NetshWifiClient(new QueryRunner());
        var info = await client.GetConnectionInfoAsync();
        Assert.False(info.ReadSucceeded);
        Assert.False(info.Connected);
        Assert.Contains("5", info.Error);
        Assert.Contains("访问被拒绝", info.Error);
    }

    [Fact]
    public void 多网卡选择实际连接的接口而不是前一个未连接接口()
    {
        var info = NetshWifiClient.ParseConnectionInfo("Name : Wi-Fi 2\nState : disconnected\n\nName : Wi-Fi\nState : connected\nSSID : Active\nSignal : 91%");
        Assert.True(info.Connected);
        Assert.Equal("Active", info.Ssid);
        Assert.Equal("Wi-Fi", info.AdapterName);
    }

    private sealed class QueryRunner : ISystemCommandRunner
    {
        public Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(new SystemCommandResult(5, "访问被拒绝"));
    }
}
