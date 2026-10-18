using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Wifi;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace NetGuardian.Tests;

public sealed class ConnectivityTests
{
    [Fact]
    public async Task 网关不响应但公网正常时应健康()
    {
        var report = await Create(new FakeProbes { GatewayOk = false }).RunFullCheckAsync("校园网");
        Assert.Equal(NetworkState.Healthy, report.State);
        Assert.Contains(report.Probes, probe => probe.Name == "Gateway ping" && !probe.Ok);
        Assert.Contains(report.Probes, probe => probe.Name.StartsWith("HTTPS ") && probe.Ok);
    }

    [Theory]
    [InlineData(1, 1, NetworkState.Healthy)]
    [InlineData(2, 1, NetworkState.InternetIssue)]
    [InlineData(3, 2, NetworkState.Healthy)]
    [InlineData(3, 1, NetworkState.InternetIssue)]
    public async Task 根据目标数计算公网成功门槛(int count, int successes, NetworkState expected)
    {
        var hosts = new[] { "first.example", "second.example", "third.example" };
        var probes = new FakeProbes { HttpsStatuses = hosts.Take(count).Select((host, index) => (host, index < successes ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)).ToDictionary(item => item.host, item => item.Item2) };
        var policy = new MonitorPolicy { HttpsProbeHosts = hosts.Take(count).ToList() };
        Assert.Equal(expected, (await Create(probes, policy).RunFullCheckAsync("校园网")).State);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task 认证重定向不能算作公网成功(HttpStatusCode status)
    {
        var report = await Create(new FakeProbes { Status = status }).RunFullCheckAsync("校园网");
        Assert.Equal(NetworkState.InternetIssue, report.State);
        Assert.DoesNotContain(report.Probes, probe => probe.Name.StartsWith("HTTPS ") && probe.Ok);
    }

    [Theory]
    [InlineData("网关")]
    [InlineData("DNS")]
    [InlineData("HTTPS")]
    public async Task 探测期间调用方取消必须向上传播(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var tester = Create(new FakeProbes { CancelStage = stage, Cancellation = cancellation });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tester.RunFullCheckAsync("校园网", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task 探针自身超时应报告失败而不冒充调用方取消()
    {
        var report = await Create(new FakeProbes { TimeoutStage = "DNS" }).RunFullCheckAsync("校园网");
        Assert.Equal(NetworkState.DnsIssue, report.State);
    }

    [Fact]
    public async Task 所有DNS解析失败时应报告DNS故障()
    {
        Assert.Equal(NetworkState.DnsIssue, (await Create(new FakeProbes { DnsFails = true }).RunFullCheckAsync("校园网")).State);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example.com:443")]
    [InlineData("example.com/path")]
    [InlineData("bad host")]
    [InlineData("example.com?query")]
    public void 无效主机配置回退安全默认值(string invalid)
    {
        var policy = new MonitorPolicy { DnsProbeHosts = [invalid], HttpsProbeHosts = [invalid] };
        policy.Normalize();
        Assert.Equal(new[] { "www.microsoft.com", "www.baidu.com" }, policy.DnsProbeHosts);
        Assert.Equal(new[] { "www.microsoft.com", "www.baidu.com", "www.cloudflare.com" }, policy.HttpsProbeHosts);
    }

    [Fact]
    public async Task 重复目标不得绕过两个独立目标的门槛()
    {
        var policy = new MonitorPolicy { HttpsProbeHosts = ["first.example", "FIRST.example", "second.example"] };
        var probes = new FakeProbes { HttpsStatuses = new(StringComparer.OrdinalIgnoreCase) { ["first.example"] = HttpStatusCode.OK, ["second.example"] = HttpStatusCode.ServiceUnavailable } };
        Assert.Equal(NetworkState.InternetIssue, (await Create(probes, policy).RunFullCheckAsync("校园网")).State);
    }

    private static ConnectivityTester Create(FakeProbes probes, MonitorPolicy? policy = null) =>
        new(policy ?? new MonitorPolicy(), new FakeEnvironment(), probes);

    private sealed class FakeEnvironment : IConnectivityEnvironment
    {
        public NetworkSnapshot Capture() => new("测试无线网卡", true, true, "192.0.2.2", "192.0.2.1");
    }

    private sealed class FakeProbes : IConnectivityProbes
    {
        public bool GatewayOk { get; init; } = true;
        public bool DnsFails { get; init; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public Dictionary<string, HttpStatusCode>? HttpsStatuses { get; init; }
        public string? CancelStage { get; init; }
        public string? TimeoutStage { get; init; }
        public CancellationTokenSource? Cancellation { get; init; }

        private void Check(string stage, CancellationToken token)
        {
            if (CancelStage == stage) { Cancellation!.Cancel(); token.ThrowIfCancellationRequested(); }
            if (TimeoutStage == stage) throw new OperationCanceledException("模拟探针超时");
        }

        public Task<bool> PingAsync(string host, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Check("网关", cancellationToken);
            return Task.FromResult(GatewayOk);
        }

        public Task ResolveAsync(string host, CancellationToken cancellationToken)
        {
            Check("DNS", cancellationToken);
            if (DnsFails) throw new SocketException((int)SocketError.HostNotFound);
            return Task.CompletedTask;
        }

        public Task<HttpStatusCode> HttpsHeadAsync(string host, CancellationToken cancellationToken)
        {
            Check("HTTPS", cancellationToken);
            return Task.FromResult(HttpsStatuses is null ? Status : HttpsStatuses[host]);
        }
    }

    [Fact]
    public async Task 驱动故障导致接口消失时仍报告设备异常()
    {
        var diagnostics = new WifiAdapterDiagnostics("无线网卡", false, false, null, null, "设备异常", "无线设备代码 10", ProblemCode: 10);
        var report = await new ConnectivityTester(new MonitorPolicy()).RunFullCheckAsync("校园网", diagnostics);
        Assert.Equal(NetworkState.AdapterFault, report.State);
        Assert.Contains("无线设备代码 10", report.Summary);
    }

    [Fact]
    public async Task 调用方预先取消时不得返回网络故障()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var diagnostics = new WifiAdapterDiagnostics("无线网卡", false, false, null, null, "禁用", "");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ConnectivityTester(new MonitorPolicy()).RunFullCheckAsync("校园网", diagnostics, cancellation.Token));
    }
}
