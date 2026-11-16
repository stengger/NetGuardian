using System.Reflection;
using System.Text;
using NetGuardian.Core.Settings;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class NetshMixedEncodingTests
{
    [Theory]
    [InlineData("8b304北_5G_Wi-Fi5")]
    [InlineData(" 前缀:北_5G ")]
    public async Task 中文命令字段与UTF8无线名称混合时保持当前SSID与导入SSID一致(string ssid)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(936);
        byte[] Mixed(params (string Label, string Value, bool Utf8)[] lines) => lines.SelectMany(line =>
            oem.GetBytes(line.Label + " : ").Concat((line.Utf8 ? Encoding.UTF8 : oem).GetBytes(line.Value))
                .Concat(new byte[] { 13, 10 })).ToArray();
        var runner = GetRunner();
        var connection = await ReadAsync(runner, Mixed(("名称", "WLAN", false), ("状态", "已连接", false),
            ("SSID", ssid, true), ("配置文件", "真实配置北", false)));
        var info = NetshWifiClient.ParseConnectionInfo(connection.Output);
        Assert.True(info.Connected, connection.Output);
        Assert.Equal(ssid, info.Ssid);
        var names = await ReadAsync(runner, Mixed(("所有用户配置文件", "真实配置北", false)));
        var name = Assert.Single(NetshWifiClient.ParseProfileNames(names.Output));
        Assert.Equal("真实配置北", name);
        var detail = await ReadAsync(runner, Mixed(("SSID 名称", "\"" + ssid + "\"", true),
            ("身份验证", "WPA2-Personal", false)));
        var importedSsid = NetshWifiClient.ParseProfileSsid(detail.Output);
        Assert.Equal(ssid, importedSsid);
        var settings = new AppSettings();
        SavedWifiProfileImporter.Apply(settings, new(true, [new(name, importedSsid!)]), info.Ssid);
        Assert.Equal(ssid, Assert.Single(settings.Profiles).Ssid);
    }

    [Theory]
    [InlineData(936)]
    [InlineData(65001)]
    public async Task 纯中文代码页与纯UTF8不改变配置名及SSID空格(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = "所有用户配置文件 :  配置北 \r\nSSID 名称 : \" 北:5G \"\r\n";
        var result = await ReadAsync(GetRunner(), Encoding.GetEncoding(codePage).GetBytes(text));
        Assert.Equal(" 配置北 ", Assert.Single(NetshWifiClient.ParseProfileNames(result.Output)));
        Assert.Equal(" 北:5G ", NetshWifiClient.ParseProfileSsid(result.Output));
    }

    [Fact]
    public async Task 中文配置详情的系统代码页引号不能破坏UTF8的SSID()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(936);
        var bytes = oem.GetBytes("SSID 名称 : “").Concat(Encoding.UTF8.GetBytes(" 北_5G "))
            .Concat(oem.GetBytes("”\r\n")).ToArray();
        var result = await ReadAsync(GetRunner(), bytes);
        Assert.Equal(" 北_5G ", NetshWifiClient.ParseProfileSsid(result.Output));
    }

    [Fact]
    public async Task 混合编码扫描只修复SSID而不改配置名称和密钥文本()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(936);
        var bytes = oem.GetBytes("SSID 1 : ").Concat(Encoding.UTF8.GetBytes("8b304北_5G_Wi-Fi5"))
            .Concat(oem.GetBytes("\r\n信号 : 100%\r\n所有用户配置文件 : 真实配置北\r\n关键内容 : 密钥北\r\n")).ToArray();
        var result = await ReadAsync(GetRunner(), bytes);
        Assert.Contains("SSID 1 : 8b304北_5G_Wi-Fi5", result.Output);
        Assert.Contains("所有用户配置文件 : 真实配置北", result.Output);
        Assert.Contains("关键内容 : 密钥北", result.Output);
    }
    private static ISystemCommandRunner GetRunner() => (ISystemCommandRunner)typeof(NetshWifiClient)
        .GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(new NetshWifiClient())!;

    private static async Task<SystemCommandResult> ReadAsync(ISystemCommandRunner runner, byte[] bytes)
    {
        var result = await runner.RunAsync("powershell.exe", ["-NoProfile", "-Command",
            "$bytes = [Convert]::FromBase64String('" + Convert.ToBase64String(bytes) + "'); [Console]::OpenStandardOutput().Write($bytes, 0, $bytes.Length)"]);
        Assert.True(result.Ok, $"退出码 {result.ExitCode}：{result.Output}");
        return result;
    }
}
