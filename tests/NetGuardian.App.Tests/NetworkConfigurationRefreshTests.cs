using System.IO;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class NetworkConfigurationRefreshTests
{
    [Fact]
    public void 读取失败返回失败状态并通知调用页而不冒充成功()
    {
        Exception? reported = null;
        var failure = new IOException("接口已移除");
        var result = NetworkConfigurationRefresh.TryRead(() => throw failure, ex => reported = ex);
        Assert.False(result);
        Assert.Same(failure, reported);
    }

    [Fact]
    public void 成功读取不触发失败回调且不吞掉程序逻辑错误()
    {
        var read = false;
        Assert.True(NetworkConfigurationRefresh.TryRead(() => read = true, _ => Assert.Fail("不应报告失败")));
        Assert.True(read);
        Assert.Throws<ArithmeticException>(() => NetworkConfigurationRefresh.TryRead(() => throw new ArithmeticException(), _ => { }));
    }
}
