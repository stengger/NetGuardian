namespace NetGuardian.Core.Models;

public sealed class MonitorPolicy
{
    public int CheckIntervalSeconds { get; set; } = 30;
    public int ConsecutiveFailuresBeforeRecovery { get; set; } = 3;
    public bool EnableAutoSwitch { get; set; } = true;
    public bool EnableCurrentWifiReconnect { get; set; } = true;
    public bool EnableDnsFlush { get; set; }
    public bool EnableWlanServiceRestart { get; set; }
    public bool EnableAdapterRestart { get; set; }
    public int RecoveryCooldownSeconds { get; set; } = 300;
    public double ProbeTimeoutSeconds { get; set; } = 2;

    public void Normalize()
    {
        CheckIntervalSeconds = Math.Max(5, CheckIntervalSeconds);
        ConsecutiveFailuresBeforeRecovery = Math.Max(1, ConsecutiveFailuresBeforeRecovery);
        RecoveryCooldownSeconds = Math.Max(30, RecoveryCooldownSeconds);
        ProbeTimeoutSeconds = Math.Max(0.5, ProbeTimeoutSeconds);
    }
}

