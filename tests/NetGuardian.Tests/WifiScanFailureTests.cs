using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class WifiScanFailureTests
{
    [Fact]
    public async Task 扫描命令失败不能解析成成功的空列表()
    {
        var client = new NetshWifiClient(new ScanRunner(5, "访问被拒绝"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ScanAsync());
        Assert.Contains("5", error.Message);
        Assert.Contains("访问被拒绝", error.Message);
    }

    [Fact]
    public async Task 成功的空扫描仍返回空列表()
    {
        var client = new NetshWifiClient(new ScanRunner(0, "There are 0 networks currently visible."));
        Assert.Empty(await client.ScanAsync());
    }

    private sealed class ScanRunner(int exitCode, string output) : ISystemCommandRunner
    {
        public Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(new SystemCommandResult(exitCode, output));
    }
}
