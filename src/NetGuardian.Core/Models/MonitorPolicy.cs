namespace NetGuardian.Core.Models;

public sealed class MonitorPolicy
{
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

    public void Normalize()
    {
        CheckIntervalSeconds = Math.Clamp(CheckIntervalSeconds, 5, 24 * 60 * 60);
        ConsecutiveFailuresBeforeRecovery = Math.Clamp(ConsecutiveFailuresBeforeRecovery, 1, 100);
        RecoveryCooldownSeconds = Math.Clamp(RecoveryCooldownSeconds, 30, 24 * 60 * 60);
        ProbeTimeoutSeconds = Math.Clamp(ProbeTimeoutSeconds, 0.5, 30);
    }
}
