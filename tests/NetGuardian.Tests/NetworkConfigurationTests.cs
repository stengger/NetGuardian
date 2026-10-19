using NetGuardian.Core.Network;
using NetGuardian.Core.SystemCommands;
using Xunit;

namespace NetGuardian.Tests;

public sealed class NetworkConfigurationTests
{
    [Fact]
    public async Task 续租只作用于明确指定的接口且名称作为单个参数传递()
    {
        var runner = new RecordingRunner(new SystemCommandResult(0, "完成"));
        var result = await new NetworkConfigurationService(runner).RenewDhcpAsync("实验室 WLAN 2");
        Assert.True(result.Success);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("ipconfig.exe", call.File);
        Assert.Equal(new[] { "/renew", "实验室 WLAN 2" }, call.Args);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("WLAN*")]
    [InlineData("?")]
    public async Task 不允许省略目标或通过通配符扩大操作范围(string name)
    {
        var runner = new RecordingRunner(new SystemCommandResult(0, "完成"));
        var service = new NetworkConfigurationService(runner);
        await Assert.ThrowsAsync<ArgumentException>(() => service.RenewDhcpAsync(name));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task 无效备用地址在执行主地址修改前就被拒绝()
    {
        var runner = new RecordingRunner(new SystemCommandResult(0, "完成"));
        await Assert.ThrowsAsync<ArgumentException>(() => new NetworkConfigurationService(runner)
            .SetStaticDnsAsync("WLAN", "1.1.1.1", "不是地址"));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task 主地址失败后不继续写备用地址()
    {
        var runner = new RecordingRunner(new SystemCommandResult(5, "拒绝访问"));
        var result = await new NetworkConfigurationService(runner).SetStaticDnsAsync("WLAN", "1.1.1.1", "1.0.0.1");
        Assert.False(result.Success);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task 备用地址失败必须报告配置可能已改变并保留两步输出()
    {
        var runner = new RecordingRunner(new SystemCommandResult(0, "首选已设置"), new(5, "备用失败"));
        var result = await new NetworkConfigurationService(runner).SetStaticDnsAsync("WLAN", "1.1.1.1", "1.0.0.1");
        Assert.False(result.Success);
        Assert.True(result.MayHaveChanged);
        Assert.Contains("首选已设置", result.Output);
        Assert.Contains("备用失败", result.Output);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task 写入主地址后取消不能被报告为完全没有修改()
    {
        var runner = new RecordingRunner(new SystemCommandResult(0, "首选已设置")) { CancelSecond = true };
        var result = await new NetworkConfigurationService(runner).SetStaticDnsAsync("WLAN", "1.1.1.1", "1.0.0.1");
        Assert.False(result.Success);
        Assert.True(result.MayHaveChanged);
        Assert.True(result.WasCanceled);
    }

    [Fact]
    public void 多个可能的出口接口时不任意选中第一个()
    {
        var candidates = new[] { new NetworkAdapterCandidate("a", "vEthernet", true, false), new("b", "WLAN", true, true) };
        Assert.Equal("b", NetworkAdapterSelection.Choose(candidates, null));
        Assert.Equal("a", NetworkAdapterSelection.Choose(candidates, "a"));
        Assert.Null(NetworkAdapterSelection.Choose(candidates.Append(new("c", "以太网", true, true)), null));
        Assert.Null(NetworkAdapterSelection.Choose(candidates.Take(1), null));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, null, false)]
    [InlineData(false, true, false)]
    public void 续租前按最新接口条件验证而不沿用列表中的旧状态(bool supportsIpv4, bool? dhcpEnabled, bool allowed)
    {
        var selected = new NetworkAdapterCandidate("id", "WLAN", true, true);
        var current = new NetworkAdapterCandidate("id", "WLAN", true, true, supportsIpv4, dhcpEnabled);
        Assert.Equal(allowed, NetworkAdapterSelection.CanRenewDhcp(selected, current));
        Assert.False(NetworkAdapterSelection.CanRenewDhcp(selected, current with { Id = "其他接口" }));
        Assert.False(NetworkAdapterSelection.CanRenewDhcp(selected, current with { Name = "已更名" }));
        Assert.False(NetworkAdapterSelection.CanRenewDhcp(selected, current with { IsUp = false }));
    }

    private sealed class RecordingRunner(params SystemCommandResult[] results) : ISystemCommandRunner
    {
        public List<(string File, string[] Args)> Calls { get; } = [];
        public bool CancelSecond { get; init; }
        public Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (CancelSecond && Calls.Count == 2) throw new OperationCanceledException();
            return Task.FromResult(results[Math.Min(Calls.Count - 1, results.Length - 1)]);
        }
    }
}
