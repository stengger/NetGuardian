using System.Reflection;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class NetshOutputEncodingTests
{
    [Theory]
    [InlineData(936)]
    [InlineData(65001)]
    public async Task 实际命令读取器兼容中文代码页与UTF8(int codePage)
    {
        var client = new NetshWifiClient();
        var runner = (ISystemCommandRunner)typeof(NetshWifiClient)
            .GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        var result = await runner.RunAsync("powershell.exe", ["-NoProfile", "-Command",
            $"[Console]::OutputEncoding = [Text.Encoding]::GetEncoding({codePage}); [Console]::Write(\"名称 : WLAN`n状态 : 已连接`nSSID : 8b304北`n所有用户配置文件 : 8b304北\")"]);
        Assert.Equal(0, result.ExitCode);
        var info = NetshWifiClient.ParseConnectionInfo(result.Output);
        Assert.True(info.Connected, result.Output);
        Assert.Equal("8b304北", info.Ssid);
        Assert.Equal("8b304北", Assert.Single(NetshWifiClient.ParseProfileNames(result.Output)));
    }
}
