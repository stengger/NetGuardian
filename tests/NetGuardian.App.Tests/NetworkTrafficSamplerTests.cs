using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class NetworkTrafficSamplerTests
{
    [Fact]
    public void 折叠侧栏保留实际数值及二进制字节单位()
    {
        var format = typeof(NetGuardian.App.MainWindow).GetMethod("FormatRateCompact", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal("512B", format.Invoke(null, [512d]));
        var kib = Assert.IsType<string>(format.Invoke(null, [1024d]));
        Assert.StartsWith("1", kib);
        Assert.EndsWith("Ki", kib);
        var mib = Assert.IsType<string>(format.Invoke(null, [1048576d]));
        Assert.StartsWith("1", mib);
        Assert.EndsWith("Mi", mib);
    }

    [Fact]
    public void 新接口建立基线且接口消失不吞掉其他接口的真实增量()
    {
        var sampler = new NetworkTrafficSampler();
        var first = sampler.Sample([new("a", 1000, 2000)], TimeSpan.FromSeconds(1));
        Assert.Null(first);
        var second = sampler.Sample([new("a", 1200, 2100), new("b", 900000, 800000)], TimeSpan.FromSeconds(2));
        Assert.Equal(100, second!.DownloadBytesPerSecond);
        Assert.Equal(50, second.UploadBytesPerSecond);
        var third = sampler.Sample([new("a", 1400, 2300)], TimeSpan.FromSeconds(1));
        Assert.Equal(200, third!.DownloadBytesPerSecond);
        var fourth = sampler.Sample([new("b", 990000, 900000)], TimeSpan.FromSeconds(1));
        Assert.Null(fourth);
    }

    [Fact]
    public void 计数重置或读取中断重新建基线而不报告假零或尖峰()
    {
        var sampler = new NetworkTrafficSampler();
        sampler.Sample([new("a", 1000, 2000)], TimeSpan.FromSeconds(1));
        Assert.Null(sampler.Sample([new("a", 10, 20)], TimeSpan.FromSeconds(1)));
        Assert.Equal(0, sampler.Sample([new("a", 10, 20)], TimeSpan.FromSeconds(1))!.DownloadBytesPerSecond);
        Assert.Null(sampler.Sample([], TimeSpan.FromSeconds(1)));
        Assert.Null(sampler.Sample([new("a", 100000, 200000)], TimeSpan.FromSeconds(1)));
    }
}
