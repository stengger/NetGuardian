using System.Net;
using NetGuardian.Core.Network;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class NetworkEnvironmentInspectorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ng_env_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void NetshWifiClient_ParseConnectionInfo_ExtractsRadioBandChannelRatesAndBssid()
    {
        const string chineseOutput = """
            系统上有 1 个接口:

                名称                   : WLAN
                状态                   : 已连接
                SSID                   : Campus-5G
                AP BSSID               : aa:bb:cc:11:22:33
                网络类型               : 结构
                无线电类型             : 802.11ax
                身份验证               : WPA2-个人
                密码                   : CCMP
                连接模式               : 自动连接
                频段                   : 5 GHz
                信道                   : 149
                接收速率(Mbps)         : 866.7
                传输速率 (Mbps)        : 780
                信号                   : 92%
            """;

        var info = NetshWifiClient.ParseConnectionInfo(chineseOutput);
        Assert.True(info.ReadSucceeded);
        Assert.True(info.Connected);
        Assert.Equal("Campus-5G", info.Ssid);
        Assert.Equal("aa:bb:cc:11:22:33", info.Bssid);
        Assert.Equal("802.11ax", info.RadioType);
        Assert.Equal("5 GHz", info.Band);
        Assert.Equal(149, info.Channel);
        Assert.Equal(866.7, info.ReceiveRateMbps);
        Assert.Equal(780, info.TransmitRateMbps);
        Assert.Equal(92, info.SignalPercent);

        const string englishOutputWithoutBand = """
            There is 1 interface on the system:

                Name                   : Wi-Fi
                State                  : connected
                SSID                   : Home-Net
                BSSID                  : 10:20:30:40:50:60
                Radio type             : 802.11ac
                Authentication         : WPA2-Personal
                Cipher                 : CCMP
                Channel                : 6
                Receive rate (Mbps)    : 144.4
                Transmit rate (Mbps)   : 144.4
                Signal                 : 85%
            """;

        var enInfo = NetshWifiClient.ParseConnectionInfo(englishOutputWithoutBand);
        Assert.True(enInfo.Connected);
        Assert.Equal("10:20:30:40:50:60", enInfo.Bssid);
        Assert.Equal("802.11ac", enInfo.RadioType);
        Assert.Equal("2.4 GHz", enInfo.Band);
        Assert.Equal(6, enInfo.Channel);
    }

    [Fact]
    public void InspectHostsFile_IgnoresCommentsAndLocalhost_CountsCustomMappings()
    {
        Directory.CreateDirectory(_tempDir);
        var hostsPath = Path.Combine(_tempDir, "hosts");
        File.WriteAllText(hostsPath, """
            # Copyright comment
            127.0.0.1 localhost
            ::1 localhost
            127.0.0.1 api.dev.local auth.dev.local # inline comment
            192.168.10.20 nas.home
            """);

        var summary = NetworkEnvironmentInspector.InspectHostsFile(hostsPath);
        Assert.True(summary.ReadSucceeded);
        Assert.Equal(3, summary.CustomEntryCount);
        Assert.Equal(["api.dev.local", "auth.dev.local", "nas.home"], summary.SampleHosts);
    }

    [Fact]
    public async Task ProbeIpv6StatusAsync_HandlesNoGlobalIpv6AndDualStackReachable()
    {
        var linkLocalOnly = new[] { IPAddress.Parse("fe80::1234"), IPAddress.Parse("fd00::1") };
        var noGlobal = await NetworkEnvironmentInspector.ProbeIpv6StatusAsync(
            localAddressesOverride: linkLocalOnly,
            publicIpv6Lookup: _ => throw new InvalidOperationException("should not probe"));
        Assert.False(noGlobal.HasLocalGlobalIpv6);
        Assert.False(noGlobal.PublicIpv6Reachable);

        var withGlobal = new[] { IPAddress.Parse("240e:1234:5678::1") };
        var reachable = await NetworkEnvironmentInspector.ProbeIpv6StatusAsync(
            localAddressesOverride: withGlobal,
            publicIpv6Lookup: _ => Task.FromResult<string?>("240e:1234:5678::1"));
        Assert.True(reachable.HasLocalGlobalIpv6);
        Assert.Equal("240e:1234:5678::1", reachable.LocalGlobalIpv6);
        Assert.True(reachable.PublicIpv6Reachable);
        Assert.Equal("240e:1234:5678::1", reachable.PublicIpv6);
    }
}
