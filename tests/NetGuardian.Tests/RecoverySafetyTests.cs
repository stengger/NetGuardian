using NetGuardian.Core.Recovery;
using NetGuardian.Core.SystemCommands;
using Xunit;

namespace NetGuardian.Tests;

public sealed class RecoverySafetyTests
{
    [Fact]
    public async Task 停止无线服务期间取消仍尝试重新启动()
    {
        var commands = new CancelAfterStop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new HighRiskRecoveryService(commands).RestartWlanServiceAsync());
        Assert.Equal(new[] { "stop", "start" }, commands.Actions);
    }

    [Fact]
    public async Task 网卡代码十使用设备实例重启而非网络接口名称()
    {
        var commands = new FaultyDeviceCommands();
        var result = await new HighRiskRecoveryService(commands).RestartAdapterAsync("PCI\\VEN_14C3&DEV_7925\\TEST");
        Assert.True(result.Ok);
        Assert.Contains(commands.Calls, call => call.File == "pnputil.exe" && call.Args.SequenceEqual(new[] { "/restart-device", "PCI\\VEN_14C3&DEV_7925\\TEST" }));
        Assert.DoesNotContain(commands.Calls, call => call.File == "netsh.exe");
    }

    private sealed class CancelAfterStop : ISystemCommandRunner
    {
        public List<string> Actions { get; } = [];
        public Task<SystemCommandResult> RunAsync(string file, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            Actions.Add(args[0]);
            if (args[0] == "stop") throw new OperationCanceledException();
            return Task.FromResult(new SystemCommandResult(0, ""));
        }
    }

    private sealed class FaultyDeviceCommands : ISystemCommandRunner
    {
        public List<(string File, IReadOnlyList<string> Args)> Calls { get; } = [];
        public Task<SystemCommandResult> RunAsync(string file, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            Calls.Add((file, args));
            return Task.FromResult(new SystemCommandResult(0, file == "powershell.exe"
                ? "{\"InstanceId\":\"PCI\\\\VEN_14C3&DEV_7925\\\\TEST\",\"Name\":\"MediaTek Wi-Fi\",\"ProblemCode\":10}" : ""));
        }
    }
}
