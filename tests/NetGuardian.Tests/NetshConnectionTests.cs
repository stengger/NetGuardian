using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class NetshConnectionTests
{
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
        public Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Assert.Equal("netsh.exe", fileName);
            Arguments = arguments;
            return Task.FromResult(new SystemCommandResult(arguments.Any(value => value.StartsWith("ssid=")) ? 1 : 0, ""));
        }
    }
}
