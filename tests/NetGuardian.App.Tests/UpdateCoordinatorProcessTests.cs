using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using Xunit;
using NetGuardian.App.Updates;

namespace NetGuardian.App.Tests;

public sealed class UpdateCoordinatorProcessTests
{
    [Fact]
    public async Task 协调脚本提前退出时保存并展示中文原始错误()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NetGuardian 更新测试 ' " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var script = Path.Combine(directory, "失败脚本.ps1");
            await File.WriteAllTextAsync(script, "param([string]$RequestFile)\nthrow '模拟中文准备失败'", new System.Text.UTF8Encoding(true));
            using var process = Process.Start(UpdateCoordinatorProcess.CreateStartInfo(script, Path.Combine(directory, "request.json"), directory))!;
            var errorOutput = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            var error = await UpdateCoordinatorProcess.ReadFailureAsync(process, errorOutput, directory);
            Assert.Equal(1, process.ExitCode);
            Assert.Contains("模拟中文准备失败", error.Message);
            Assert.Contains("coordinator-stderr.log", error.Message);
            Assert.DoesNotContain("CLIXML", error.Message);
            Assert.Contains("模拟中文准备失败", await File.ReadAllTextAsync(Path.Combine(directory, "coordinator-stderr.log")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void 协调进程隐藏运行且路径通过环境变量传入()
    {
        var info = UpdateCoordinatorProcess.CreateStartInfo("脚本 ' 路径", "请求路径", "作业目录");
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardError);
        Assert.Equal("脚本 ' 路径", info.Environment["NETGUARDIAN_UPDATE_SCRIPT"]);
        Assert.Equal("请求路径", info.Environment["NETGUARDIAN_UPDATE_REQUEST"]);
    }
}