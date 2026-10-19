using NetGuardian.Core.SystemCommands;
using System.Net;
using System.Net.Sockets;

namespace NetGuardian.Core.Network;

public sealed class NetworkConfigurationService
{
    private readonly ISystemCommandRunner _runner;

    public NetworkConfigurationService(ISystemCommandRunner? runner = null) => _runner = runner ?? new ProcessCommandRunner();

    public Task<NetworkCommandResult> SetAutomaticDnsAsync(string adapterName, CancellationToken cancellationToken = default)
    {
        ValidateAdapter(adapterName);
        return RunNetshAsync(["interface", "ip", "set", "dns", $"name={adapterName}", "dhcp"], cancellationToken);
    }

    public async Task<NetworkCommandResult> SetStaticDnsAsync(
        string adapterName,
        string primaryDns,
        string? secondaryDns,
        CancellationToken cancellationToken = default)
    {
        ValidateAdapter(adapterName);
        ValidateIpv4(primaryDns);
        if (!string.IsNullOrWhiteSpace(secondaryDns)) ValidateIpv4(secondaryDns);
        var first = await RunNetshAsync(
            ["interface", "ip", "set", "dns", $"name={adapterName}", "static", primaryDns, "primary"],
            cancellationToken);
        if (!first.Success || string.IsNullOrWhiteSpace(secondaryDns))
        {
            return first;
        }

        try
        {
            var second = await RunNetshAsync(
                ["interface", "ip", "add", "dns", $"name={adapterName}", secondaryDns, "index=2"], cancellationToken);
            return second with { Output = CombineOutput(first.Output, second.Output), MayHaveChanged = !second.Success };
        }
        catch (OperationCanceledException)
        {
            // 首选地址已经写入，取消备用步骤不能声称配置未变。
            return new(false, 1223, first.Output, MayHaveChanged: true, WasCanceled: true);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new(false, -1, CombineOutput(first.Output, ex.Message), MayHaveChanged: true);
        }
    }

    public async Task<NetworkCommandResult> RenewDhcpAsync(string adapterName, CancellationToken cancellationToken = default)
    {
        ValidateAdapter(adapterName);
        var result = await _runner.RunAsync("ipconfig.exe", ["/renew", adapterName], cancellationToken);
        return new(result.Ok, result.ExitCode, result.Output, MayHaveChanged: !result.Ok);
    }

    private async Task<NetworkCommandResult> RunNetshAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync("netsh.exe", arguments, cancellationToken);
        return new NetworkCommandResult(result.Ok, result.ExitCode, result.Output, MayHaveChanged: !result.Ok);
    }

    private static void ValidateAdapter(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['*', '?', '\r', '\n', '\0']) >= 0)
            throw new ArgumentException("必须指定单个网络接口，不允许空名称或通配符。", nameof(name));
    }

    private static void ValidateIpv4(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("请输入有效的 IPv4 DNS 地址。", nameof(value));
    }

    private static string CombineOutput(string? first, string? second)
    {
        return string.Join(Environment.NewLine, new[] { first, second }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim()));
    }

}

public sealed record NetworkCommandResult(bool Success, int ExitCode, string Output, bool MayHaveChanged = false, bool WasCanceled = false);
