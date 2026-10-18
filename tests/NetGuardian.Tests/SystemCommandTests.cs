using NetGuardian.Core.SystemCommands;
using Xunit;

namespace NetGuardian.Tests;

public sealed class SystemCommandTests
{
    [Fact]
    public async Task 挂起的命令超时后返回明确失败()
    {
        var runner = new ProcessCommandRunner(TimeSpan.FromMilliseconds(300));
        var result = await runner.RunAsync("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"]);
        Assert.Equal(124, result.ExitCode);
        Assert.Contains("超时", result.Output);
    }

    [Fact]
    public async Task 正常命令保留输出和退出码()
    {
        var result = await new ProcessCommandRunner().RunAsync("cmd.exe", ["/c", "echo 守护测试"]);
        Assert.True(result.Ok);
        Assert.NotEmpty(result.Output);
    }

    [Fact]
    public async Task 调用方取消后结束命令并抛出取消()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessCommandRunner().RunAsync(
            "powershell.exe", ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"], cts.Token));
    }
}
