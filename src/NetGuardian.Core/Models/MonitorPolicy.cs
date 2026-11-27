namespace NetGuardian.Core.Models;

public sealed class MonitorPolicy
{
    public static readonly string[] DefaultDnsProbeHosts = ["www.microsoft.com", "www.baidu.com"];
    public static readonly string[] DefaultHttpsProbeHosts = ["www.microsoft.com", "www.baidu.com", "www.cloudflare.com"];

    public int CheckIntervalSeconds { get; set; } = 90;
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

    /// <summary>是否启用每日维护窗口（窗口内仅继续探测与记录日志，不执行断网重连或设备重启）。</summary>
    public bool EnableMaintenanceWindow { get; set; }

    /// <summary>维护窗口开始时间（本地 24 小时制 HH:mm）。</summary>
    public string MaintenanceStartTime { get; set; } = "02:00";

    /// <summary>维护窗口结束时间（本地 24 小时制 HH:mm，支持跨夜如 23:00 -> 05:00）。</summary>
    public string MaintenanceEndTime { get; set; } = "05:00";

    /// <summary>临时暂停自动恢复的 UTC 截止时间（只继续监测与记录日志）。</summary>
    public DateTimeOffset? PauseRecoveryUntilUtc { get; set; }

    /// <summary>当前连接备用 Wi-Fi 时，按低频间隔自动尝试回切更高优先级主 Wi-Fi。</summary>
    public bool EnablePrimaryFailback { get; set; }

    /// <summary>主 Wi-Fi 自动回切检查间隔（分钟）。</summary>
    public int PrimaryFailbackIntervalMinutes { get; set; } = 30;

    /// <summary>检测到有线网卡已可连通互联网时，抑制 WLAN 服务或无线网卡高风险重启。</summary>
    public bool PreferEthernetWhenHealthy { get; set; } = true;

    public void Normalize()
    {
        CheckIntervalSeconds = Math.Clamp(CheckIntervalSeconds, 5, 24 * 60 * 60);
        ConsecutiveFailuresBeforeRecovery = Math.Clamp(ConsecutiveFailuresBeforeRecovery, 1, 100);
        RecoveryCooldownSeconds = Math.Clamp(RecoveryCooldownSeconds, 30, 24 * 60 * 60);
        ProbeTimeoutSeconds = Math.Clamp(ProbeTimeoutSeconds, 0.5, 30);
        HighRiskAfterFailedRounds = Math.Clamp(HighRiskAfterFailedRounds, 1, 10);
        HighRiskCooldownMinutes = Math.Clamp(HighRiskCooldownMinutes, 5, 24 * 60);
        PrimaryFailbackIntervalMinutes = Math.Clamp(PrimaryFailbackIntervalMinutes, 5, 24 * 60);
        MaintenanceStartTime = NormalizeTimeOfDay(MaintenanceStartTime, "02:00");
        MaintenanceEndTime = NormalizeTimeOfDay(MaintenanceEndTime, "05:00");
        DnsProbeHosts = NormalizeProbeList(DnsProbeHosts, DefaultDnsProbeHosts);
        HttpsProbeHosts = NormalizeProbeList(HttpsProbeHosts, DefaultHttpsProbeHosts);
    }

    public bool IsRecoverySuppressed(DateTimeOffset localNow, out string reason)
    {
        if (PauseRecoveryUntilUtc is { } pausedUntil && localNow.ToUniversalTime() < pausedUntil)
        {
            reason = $"PausedUntil {pausedUntil.ToLocalTime():HH:mm}";
            return true;
        }

        if (EnableMaintenanceWindow
            && TryParseTimeOfDay(MaintenanceStartTime, out var start)
            && TryParseTimeOfDay(MaintenanceEndTime, out var end)
            && IsWithinDailyWindow(TimeOnly.FromDateTime(localNow.DateTime), start, end))
        {
            reason = $"Maintenance {start:HH\\:mm}-{end:HH\\:mm}";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    public static bool IsWithinDailyWindow(TimeOnly current, TimeOnly start, TimeOnly end)
    {
        if (start == end)
        {
            return false;
        }

        return start < end
            ? current >= start && current < end
            : current >= start || current < end;
    }

    public static bool TryParseTimeOfDay(string? raw, out TimeOnly time)
        => TimeOnly.TryParseExact(
            (raw ?? string.Empty).Trim(),
            ["H:mm", "HH:mm"],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out time);

    private static string NormalizeTimeOfDay(string? raw, string fallback)
        => TryParseTimeOfDay(raw, out var parsed) ? parsed.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : fallback;

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
