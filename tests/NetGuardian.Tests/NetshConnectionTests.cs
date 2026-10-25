using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class NetshConnectionTests
{
    [Fact]
    public async Task 导入密码不能匹配仅大小写不同的SSID()
    {
        var client = new NetshWifiClient(new PasswordCommandRunner());
        Assert.Null(await client.GetSavedPasswordAsync("office"));
    }

    private sealed class PasswordCommandRunner : ISystemCommandRunner
    {
        public Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            var result = arguments.Contains("name=office") ? new SystemCommandResult(1, "未找到配置")
                : arguments.Contains("profiles") ? new(0, "All User Profile : Office")
                : arguments.Contains("key=clear") ? new(0, "Key Content : 仅供测试的密码")
                : new(0, "SSID name : \"Office\"");
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task 连接失败保留系统原因且成功后清除旧错误()
    {
        var commands = new ProfileCommandRunner { Result = new(1, "指定的网络无法用于连接。") };
        var client = new NetshWifiClient(commands);
        Assert.False(await client.ConnectAsync("备用"));
        Assert.Contains("1", client.LastConnectionFailure);
        Assert.Contains("指定的网络无法用于连接", client.LastConnectionFailure);
        commands.Result = new(0, "已成功完成连接请求。");
        Assert.True(await client.ConnectAsync("备用"));
        Assert.Null(client.LastConnectionFailure);
    }

    [Fact]
    public async Task 中文网络按保存的配置连接而不传易被代码页误转换的SSID()
    {
        var commands = new ProfileCommandRunner();
        var client = new NetshWifiClient(commands);
        Assert.True(await client.ConnectProfileAsync("实验室 配置", "8b304北_5G_Wi-Fi5"));
        Assert.Equal(new[] { "wlan", "connect", "name=实验室 配置" }, commands.Arguments);
    }

    private sealed class ProfileCommandRunner : ISystemCommandRunner
    {
        public IReadOnlyList<string>? Arguments { get; private set; }
        public SystemCommandResult Result { get; set; } = new(0, "");
        public Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Assert.Equal("netsh.exe", fileName);
            Arguments = arguments;
            return Task.FromResult(arguments.Any(value => value.StartsWith("ssid=")) ? new SystemCommandResult(1, "") : Result);
        }
    }
}
