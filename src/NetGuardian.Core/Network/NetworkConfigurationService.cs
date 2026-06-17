using System.Diagnostics;

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

        return second.Success
            ? second with { Output = CombineOutput(first.Output, second.Output) }
            : second with { Output = CombineOutput(first.Output, second.Output) };
    }

    private static async Task<NetworkCommandResult> RunNetshAsync(string[] arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = CombineOutput(await outputTask, await errorTask);
            return new NetworkCommandResult(process.ExitCode == 0, process.ExitCode, output);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcess(process);
            throw;
        }
    }

    private static string CombineOutput(string? first, string? second)
    {
        return string.Join(Environment.NewLine, new[] { first, second }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim()));
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }
}

public sealed record NetworkCommandResult(bool Success, int ExitCode, string Output);
