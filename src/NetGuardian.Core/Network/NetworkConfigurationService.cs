using NetGuardian.Core.SystemCommands;

namespace NetGuardian.Core.Network;

public sealed class NetworkConfigurationService
{
    public Task<NetworkCommandResult> SetAutomaticDnsAsync(string adapterName, CancellationToken cancellationToken = default)
    {
        return RunNetshAsync(["interface", "ip", "set", "dns", $"name={adapterName}", "dhcp"], cancellationToken);
    }

    public async Task<NetworkCommandResult> SetStaticDnsAsync(
        string adapterName,
        string primaryDns,
        string? secondaryDns,
        CancellationToken cancellationToken = default)
    {
        var first = await RunNetshAsync(
            ["interface", "ip", "set", "dns", $"name={adapterName}", "static", primaryDns, "primary"],
            cancellationToken);
        if (!first.Success || string.IsNullOrWhiteSpace(secondaryDns))
        {
            return first;
        }

        var second = await RunNetshAsync(
            ["interface", "ip", "add", "dns", $"name={adapterName}", secondaryDns, "index=2"],
            cancellationToken);

        return second with { Output = CombineOutput(first.Output, second.Output) };
    }

    private static async Task<NetworkCommandResult> RunNetshAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var result = await new ProcessCommandRunner().RunAsync("netsh.exe", arguments, cancellationToken);
        return new NetworkCommandResult(result.Ok, result.ExitCode, result.Output);
    }

    private static string CombineOutput(string? first, string? second)
    {
        return string.Join(Environment.NewLine, new[] { first, second }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim()));
    }

}

public sealed record NetworkCommandResult(bool Success, int ExitCode, string Output);
