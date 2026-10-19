using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.Tests;

public sealed class ConnectivityQualitySummaryTests
{
    [Fact]
    public void 不同协议分别统计而不产生混合平均值()
    {
        var report = new ConnectivityReport { Probes = [new("Gateway ping", true, 2), new("DNS a", true, 10), new("DNS b", true, 30), new("HTTPS a", true, 200), new("HTTPS b", true, 400)] };
        var summary = ConnectivityQualitySummary.From(report);
        Assert.Equal(2d, summary.Gateway.AverageMs);
        Assert.Equal(20d, summary.Dns.AverageMs);
        Assert.Equal(300d, summary.Https.AverageMs);
        Assert.Equal(5, summary.PassedChecks);
        Assert.Equal(5, summary.TotalChecks);
    }

    [Fact]
    public void 超时失败和无效计时不能被平均成成功响应()
    {
        var summary = ConnectivityQualitySummary.From(new() { Probes = [new("HTTPS a", true, 200), new("HTTPS b", false, 3000), new("HTTPS c", true, double.NaN), new("HTTPS d", true, -1)] });
        Assert.Equal(200d, summary.Https.AverageMs);
        Assert.Equal(4, summary.Https.Attempted);
        Assert.Equal(3, summary.Https.Succeeded);
        Assert.Equal(1, summary.Https.Measured);
        Assert.Null(summary.Dns.AverageMs);
        Assert.Null(summary.Gateway.AverageMs);
    }

    [Fact]
    public void 未执行的探针与成功零毫秒必须区分()
    {
        var empty = ConnectivityQualitySummary.From(new());
        Assert.Null(empty.Dns.AverageMs);
        Assert.Equal(0, empty.Dns.Attempted);
        var zero = ConnectivityQualitySummary.From(new() { Probes = [new("DNS a", true, 0)] });
        Assert.Equal(0d, zero.Dns.AverageMs);
        Assert.Equal(1, zero.Dns.Measured);
    }
}
