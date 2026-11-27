using NetGuardian.Core.Models;
using NetGuardian.Core.Wifi;

namespace NetGuardian.Core.Monitoring;

public enum RecoveryDecisionKind
{
    /// <summary>Report is healthy or monitoring is off — nothing to do.</summary>
    NotNeeded,

    /// <summary>Failure streak has not reached the policy threshold yet.</summary>
    BelowThreshold,

    /// <summary>Threshold reached but the recovery cooldown is still active.</summary>
    CooldownActive,

    /// <summary>Recovery should be attempted now.</summary>
    Proceed
}

public readonly record struct RecoveryDecision(RecoveryDecisionKind Kind, TimeSpan CooldownRemaining)
{
    public static RecoveryDecision NotNeeded => new(RecoveryDecisionKind.NotNeeded, TimeSpan.Zero);
    public static RecoveryDecision BelowThreshold => new(RecoveryDecisionKind.BelowThreshold, TimeSpan.Zero);
    public static RecoveryDecision Proceed => new(RecoveryDecisionKind.Proceed, TimeSpan.Zero);
}

public enum HighRiskDecisionKind
{
    /// <summary>Master switch is off.</summary>
    NotEnabled,

    /// <summary>Not enough failed low-risk rounds yet.</summary>
    BelowRounds,

    /// <summary>High-risk cooldown still active.</summary>
    CooldownActive,

    /// <summary>High-risk actions may run now.</summary>
    Proceed
}

public readonly record struct HighRiskDecision(HighRiskDecisionKind Kind, TimeSpan CooldownRemaining);

/// <summary>
/// Pure decision logic for the graduated recovery strategy.
/// No I/O — all methods are deterministic given their inputs, which keeps them unit-testable.
/// </summary>
public static class RecoveryPlanner
{
    /// <summary>Decides whether a recovery attempt should start after a check.</summary>
    public static RecoveryDecision Evaluate(
        MonitorPolicy policy,
        bool reportOk,
        bool isMonitoring,
        int failureStreak,
        DateTimeOffset? lastRecoveryAttemptAt,
        DateTimeOffset now)
    {
        if (!isMonitoring || reportOk)
        {
            return RecoveryDecision.NotNeeded;
        }

        if (failureStreak < policy.ConsecutiveFailuresBeforeRecovery)
        {
            return RecoveryDecision.BelowThreshold;
        }

        var cooldown = TimeSpan.FromSeconds(policy.RecoveryCooldownSeconds);
        if (lastRecoveryAttemptAt is { } lastAttempt && now - lastAttempt < cooldown)
        {
            return new RecoveryDecision(RecoveryDecisionKind.CooldownActive, cooldown - (now - lastAttempt));
        }

        return RecoveryDecision.Proceed;
    }

    /// <summary>
    /// Orders the Wi-Fi profiles eligible for auto-switch: enabled, auto-connect,
    /// with saved credentials, excluding the currently connected SSID, by priority.
    /// </summary>
    public static IReadOnlyList<WifiProfile> OrderSwitchCandidates(IEnumerable<WifiProfile> profiles, string? currentSsid,
        IReadOnlySet<string>? nearbySsids = null)
    {
        return profiles
            .Where(profile => profile.IsRecoveryCandidate)
            .Where(profile => !SsidEquals(profile.Ssid, currentSsid))
            .Where(profile => nearbySsids is null || nearbySsids.Contains(profile.Ssid))
            .OrderBy(profile => profile.Priority)
            .ToList();
    }

    /// <summary>当连接中的 Wi-Fi 并非最高优先级可用候选时，选出值得尝试低频回切的主 Wi-Fi。</summary>
    public static WifiProfile? SelectPrimaryProfileForFailback(IEnumerable<WifiProfile> profiles, string? currentSsid)
    {
        if (string.IsNullOrWhiteSpace(currentSsid))
        {
            return null;
        }

        var ordered = profiles
            .Where(profile => profile.IsRecoveryCandidate)
            .OrderBy(profile => profile.Priority)
            .ToList();
        if (ordered.Count == 0)
        {
            return null;
        }

        var primary = ordered[0];
        if (SsidEquals(primary.Ssid, currentSsid))
        {
            return null;
        }

        var currentProfile = ordered.FirstOrDefault(p => SsidEquals(p.Ssid, currentSsid));
        // 若当前连着配置的备用 Wi-Fi（Priority > primary.Priority），或连接了临时未入配置但希望回到主 Wi-Fi 的情景：
        return currentProfile is null || currentProfile.Priority > primary.Priority ? primary : null;
    }

    /// <summary>Whether the Wi-Fi adapter should be auto-enabled before probing.</summary>
    public static bool ShouldTryEnableWifi(MonitorPolicy policy, WifiAdapterDiagnostics diagnostics)
    {
        // 代码22表示设备被禁用；代码10等启动故障交由分级设备恢复处理。
        if (diagnostics.ProblemCode is > 0 and not 22) return false;
        if (!policy.EnableWifiAdapterAutoEnable || !diagnostics.AdapterPresent)
        {
            return false;
        }

        if (diagnostics.HardwareRadioOn == false)
        {
            return false;
        }

        return !diagnostics.AdapterUp || diagnostics.SoftwareRadioOn == false;
    }

    /// <summary>
    /// Decides whether high-risk recovery (WLAN service / adapter restart) may run.
    /// Requires the master switch, a minimum number of fully-failed low-risk rounds,
    /// and an independent long cooldown.
    /// </summary>
    public static HighRiskDecision EvaluateHighRisk(
        MonitorPolicy policy,
        int failedRecoveryRounds,
        DateTimeOffset? lastHighRiskAt,
        DateTimeOffset now)
    {
        if (!policy.EnableHighRiskRecovery)
        {
            return new HighRiskDecision(HighRiskDecisionKind.NotEnabled, TimeSpan.Zero);
        }

        if (failedRecoveryRounds < policy.HighRiskAfterFailedRounds)
        {
            return new HighRiskDecision(HighRiskDecisionKind.BelowRounds, TimeSpan.Zero);
        }

        var cooldown = TimeSpan.FromMinutes(policy.HighRiskCooldownMinutes);
        if (lastHighRiskAt is { } last && now - last < cooldown)
        {
            return new HighRiskDecision(HighRiskDecisionKind.CooldownActive, cooldown - (now - last));
        }

        return new HighRiskDecision(HighRiskDecisionKind.Proceed, TimeSpan.Zero);
    }

    /// <summary>仅设备或 WLAN 层故障允许重启服务/网卡，上游故障优先切换网络。</summary>
    public static bool IsDeviceLayerFailure(NetworkState state) => state is
        NetworkState.WifiDisabled or NetworkState.WifiDisconnected or NetworkState.AdapterFault;

    public static bool SsidEquals(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && left.Equals(right, StringComparison.Ordinal);
    }
}
