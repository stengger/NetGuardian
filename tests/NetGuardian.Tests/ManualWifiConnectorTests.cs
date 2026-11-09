using NetGuardian.Core.Models;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class ManualWifiConnectorTests
{
    private static readonly WifiProfile Target = new() { Ssid = "目标网络", WindowsProfileName = "Windows配置", CredentialSaved = true };
    private static ManualWifiConnector Connector(FakeWifiClient wifi, ScriptedTester tester) => new(wifi, tester)
    { ConnectionTimeout = TimeSpan.FromMilliseconds(200), PollInterval = TimeSpan.FromMilliseconds(1) };
    private static ConnectivityReport Report(string? ssid, NetworkState state) => new() { CurrentSsid = ssid, State = state };

    [Fact]
    public async Task 已连接目标时不重复连接并实际确认公网()
    {
        var wifi = new FakeWifiClient { CurrentSsid = Target.Ssid };
        var probes = 0;
        var result = await Connector(wifi, new((ssid, _) => { probes++; return Task.FromResult(Report(ssid, NetworkState.Healthy)); }))
            .ConnectAsync(Target, new());
        Assert.True(result.Connected);
        Assert.True(result.Report?.Ok);
        Assert.Empty(wifi.ConnectAttempts);
        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task 使用Windows配置名且等待地址就绪后报告联网成功()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "原网络" };
        wifi.ConnectSucceedsFor.Add(Target.Ssid);
        var probes = 0;
        var result = await Connector(wifi, new((ssid, _) => Task.FromResult(Report(ssid,
            ++probes == 1 ? NetworkState.Disconnected : NetworkState.Healthy)))).ConnectAsync(Target, new());
        Assert.True(result.Report?.Ok);
        Assert.Equal(2, probes);
        Assert.Equal((Target.WindowsProfileName, Target.Ssid), Assert.Single(wifi.ProfileConnectAttempts));
    }

    [Fact]
    public async Task 接受请求但未关联目标时不能报告连接成功()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "原网络", ConnectHandler = (_, _) => Task.FromResult(true) };
        var result = await Connector(wifi, new((_, _) => throw new Exception("不能检测错误网络"))).ConnectAsync(Target, new());
        Assert.False(result.Connected);
        Assert.Null(result.Report);
    }

    [Fact]
    public async Task 系统拒绝连接不继续等待或探测()
    {
        var wifi = new FakeWifiClient { CurrentSsid = "原网络" };
        var result = await Connector(wifi, new((_, _) => throw new Exception("不能检测错误网络"))).ConnectAsync(Target, new());
        Assert.False(result.Connected);
        Assert.Single(wifi.ConnectAttempts);
    }

    [Fact]
    public async Task 关联成功但校园网未认证须保留联网失败结果()
    {
        var wifi = new FakeWifiClient { CurrentSsid = Target.Ssid };
        var result = await Connector(wifi, new((ssid, _) => Task.FromResult(Report(ssid, NetworkState.InternetIssue))))
            .ConnectAsync(Target, new());
        Assert.True(result.Connected);
        Assert.False(result.Report?.Ok);
        Assert.Equal(NetworkState.InternetIssue, result.Report?.State);
    }

    [Fact]
    public async Task 联网验证期间目标网络变化不能返回旧成功结果()
    {
        var wifi = new FakeWifiClient { CurrentSsid = Target.Ssid };
        var result = await Connector(wifi, new((ssid, _) =>
        {
            wifi.CurrentSsid = "其他网络";
            return Task.FromResult(Report(ssid, NetworkState.Healthy));
        })).ConnectAsync(Target, new());
        Assert.False(result.Connected);
        Assert.Contains("已变化", result.Error);
    }

    [Fact]
    public async Task 取消不能提前放任连接任务仍在运行或发布迟到成功()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wifi = new FakeWifiClient { CurrentSsid = "原网络", ConnectHandler = (_, _) =>
        { entered.TrySetResult(); return release.Task; } };
        using var cancellation = new CancellationTokenSource();
        var pending = Connector(wifi, new((_, _) => throw new Exception("取消后不能探测"))).ConnectAsync(Target, new(), cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        Assert.False(pending.IsCompleted);
        release.SetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task 联网检查超时不能冒充通过()
    {
        var wifi = new FakeWifiClient { CurrentSsid = Target.Ssid };
        var result = await Connector(wifi, new(async (ssid, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Report(ssid, NetworkState.Healthy);
        })).ConnectAsync(Target, new());
        Assert.False(result.Connected);
        Assert.Null(result.Report);
    }
}
