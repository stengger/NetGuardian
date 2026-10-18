namespace NetGuardian.Core.Models;

public sealed class MonitorPolicy
{
    public static readonly string[] DefaultDnsProbeHosts = ["www.microsoft.com", "www.baidu.com"];
    public static readonly string[] DefaultHttpsProbeHosts = ["www.microsoft.com", "www.baidu.com", "www.cloudflare.com"];

    public int CheckIntervalSeconds { get; set; } = 30;
    public int ConsecutiveFailuresBeforeRecovery { get; set; } = 3;
    public bool EnableAutoSwitch { get; set; } = true;
    public bool EnableCurrentWifiReconnect { get; set; } = true;
    public bool EnableWifiAdapterAutoEnable { get; set; } = true;
    public bool EnableDnsFlush { get; set; }
    public bool EnableWlanServiceRestart { get; set; }
    public bool EnableAdapterRestart { get; set; }
    public int RecoveryCooldownSeconds { get; set; } = 300;
    public double ProbeTimeoutSeconds { get; set; } = 2;

    /// <summary>Master switch for high-risk recovery (WLAN service / adapter restart). Default off.</summary>
    public bool EnableHighRiskRecovery { get; set; }

    /// <summary>How many full low-risk recovery rounds must fail before high-risk actions are allowed.</summary>
    public int HighRiskAfterFailedRounds { get; set; } = 2;

    /// <summary>Independent (long) cooldown between high-risk attempts, in minutes.</summary>
    public int HighRiskCooldownMinutes { get; set; } = 30;

    /// <summary>DNS 解析目标主机名（1 至 5 项）。</summary>
    public List<string> DnsProbeHosts { get; set; } = [.. DefaultDnsProbeHosts];

    /// <summary>HTTPS HEAD 目标主机名（1 至 5 项）；单目标须成功，多目标至少成功两项。</summary>
    public List<string> HttpsProbeHosts { get; set; } = [.. DefaultHttpsProbeHosts];

    public void Normalize()
    {
        CheckIntervalSeconds = Math.Clamp(CheckIntervalSeconds, 5, 24 * 60 * 60);
        ConsecutiveFailuresBeforeRecovery = Math.Clamp(ConsecutiveFailuresBeforeRecovery, 1, 100);
        RecoveryCooldownSeconds = Math.Clamp(RecoveryCooldownSeconds, 30, 24 * 60 * 60);
        ProbeTimeoutSeconds = Math.Clamp(ProbeTimeoutSeconds, 0.5, 30);
        HighRiskAfterFailedRounds = Math.Clamp(HighRiskAfterFailedRounds, 1, 10);
        HighRiskCooldownMinutes = Math.Clamp(HighRiskCooldownMinutes, 5, 24 * 60);
        DnsProbeHosts = NormalizeProbeList(DnsProbeHosts, DefaultDnsProbeHosts);
        HttpsProbeHosts = NormalizeProbeList(HttpsProbeHosts, DefaultHttpsProbeHosts);
    }

    internal static List<string> NormalizeProbeList(List<string>? hosts, string[] defaults)
    {
        var cleaned = (hosts ?? [])
            .Select(host => host?.Trim() ?? "")
            .Where(host => host.Length > 0 && Uri.CheckHostName(host) != UriHostNameType.Unknown)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        return cleaned.Count > 0 ? cleaned : [.. defaults];
    }
}
