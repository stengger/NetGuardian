using NetGuardian.Core.Models;

namespace NetGuardian.Core.Monitoring;

/// <summary>Structured, localization-agnostic events emitted by <see cref="MonitoringEngine"/>.</summary>
public enum EngineEventKind
{
    MonitorStarted,
    MonitorStopped,
    TryingEnableWifiAdapter,
    WifiAdapterEnabled,
    WifiAdapterEnableFailed,
    CheckError,
    RecoveryCooldownActive,
    RecoveryStarted,
    RecoveryDnsFlush,
    RecoveryDnsFlushCompleted,
    RecoveryReconnectCurrent,
    RecoverySwitchWifi,
    RecoveryWifiConnected,
    RecoveryNoActionsEnabled,
    RecoveryFailed,
    HighRiskCooldownActive,
    HighRiskActionStarted,
    HighRiskActionCompleted
}

public sealed class EngineEventArgs : EventArgs
{
    public EngineEventArgs(EngineEventKind kind, params object?[] args)
    {
        Kind = kind;
        Args = args;
    }

    public EngineEventKind Kind { get; }

    /// <summary>Positional arguments for the UI to format into a localized message.</summary>
    public IReadOnlyList<object?> Args { get; }
}

public sealed class CheckCompletedEventArgs : EventArgs
{
    public CheckCompletedEventArgs(ConnectivityReport report, int failureStreak)
    {
        Report = report;
        FailureStreak = failureStreak;
    }

    public ConnectivityReport Report { get; }
    public int FailureStreak { get; }
}

public sealed class ProfileRecoveryResultEventArgs : EventArgs
{
    public ProfileRecoveryResultEventArgs(string ssid, bool succeeded)
    {
        Ssid = ssid;
        Succeeded = succeeded;
    }

    public string Ssid { get; }
    public bool Succeeded { get; }
}
