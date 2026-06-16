using System.Diagnostics;
using System.Text.RegularExpressions;
using NetGuardian.Core.Models;

namespace NetGuardian.Core.Wifi;

public sealed partial class NetshWifiClient : IWifiClient
{
    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunNetshAsync(["wlan", "show", "networks", "mode=bssid"], cancellationToken);
        return ParseScanOutput(result.Output);
    }

    public async Task<string?> GetCurrentSsidAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunNetshAsync(["wlan", "show", "interfaces"], cancellationToken);
        return ParseCurrentSsid(result.Output);
    }

    public async Task<bool> ConnectAsync(string ssid, CancellationToken cancellationToken = default)
    {
        var result = await RunNetshAsync(["wlan", "connect", $"name={ssid}", $"ssid={ssid}"], cancellationToken);
        return result.ExitCode == 0;
    }

    public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunNetshAsync(["wlan", "disconnect"], cancellationToken);
        return result.ExitCode == 0;
    }

    public static string? ParseCurrentSsid(string output)
    {
        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();
            if (line.Contains("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Regex.IsMatch(line, @"(^|\s)(SSID|SSID 名称)\s*:", RegexOptions.IgnoreCase))
            {
                var value = line.Split(':', 2).Last().Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    public static IReadOnlyList<WifiNetwork> ParseScanOutput(string output)
    {
        var networks = new List<WifiNetwork>();
        var current = new MutableNetwork();

        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();
            var ssidMatch = SsidRegex().Match(line);
            if (ssidMatch.Success)
            {
                AddCurrent();
                var ssid = ssidMatch.Groups.Values
                    .Skip(1)
                    .Select(group => group.Value)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
                current = new MutableNetwork { Ssid = ssid.Trim() };
                continue;
            }

            if (string.IsNullOrWhiteSpace(current.Ssid) || !line.Contains(':'))
            {
                continue;
            }

            var parts = line.Split(':', 2);
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            if (key.StartsWith("Signal", StringComparison.OrdinalIgnoreCase) || key.StartsWith("信号", StringComparison.OrdinalIgnoreCase))
            {
                var signalMatch = Regex.Match(value, @"\d+");
                if (signalMatch.Success)
                {
                    current.Signal = int.Parse(signalMatch.Value);
                }
            }
            else if (key.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase) || key.StartsWith("身份验证", StringComparison.OrdinalIgnoreCase))
            {
                current.Authentication = value;
            }
            else if (key.StartsWith("Encryption", StringComparison.OrdinalIgnoreCase) || key.StartsWith("加密", StringComparison.OrdinalIgnoreCase))
            {
                current.Encryption = value;
            }
        }

        AddCurrent();
        return networks
            .GroupBy(network => network.Ssid)
            .Select(group => group.OrderByDescending(network => network.SignalPercent).First())
            .OrderByDescending(network => network.SignalPercent)
            .ThenBy(network => network.Ssid)
            .ToList();

        void AddCurrent()
        {
            if (!string.IsNullOrWhiteSpace(current.Ssid))
            {
                networks.Add(new WifiNetwork(
                    current.Ssid,
                    current.Signal,
                    current.Authentication,
                    current.Encryption,
                    !current.Authentication.Contains("Open", StringComparison.OrdinalIgnoreCase)
                        && !current.Authentication.Contains("开放", StringComparison.OrdinalIgnoreCase),
                    true));
            }
        }
    }

    private static IEnumerable<string> SplitLines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static async Task<(int ExitCode, string Output)> RunNetshAsync(string[] arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "netsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask + await errorTask;
        return (process.ExitCode, output);
    }

    [GeneratedRegex(@"^SSID\s+\d+\s*:\s*(.*)$|^SSID\s*\d*\s*名称\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SsidRegex();

    private sealed class MutableNetwork
    {
        public string Ssid { get; init; } = "";
        public int Signal { get; set; }
        public string Authentication { get; set; } = "";
        public string Encryption { get; set; } = "";
    }
}
