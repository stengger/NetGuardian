using System.Text.Json.Serialization;

namespace NetGuardian.Core.Models;

public sealed class WifiProfile
{
    public string Ssid { get; set; } = "";
    public string WindowsProfileName { get; set; } = "";

    [JsonIgnore]
    public string ConnectionProfileName => string.IsNullOrEmpty(WindowsProfileName) ? Ssid : WindowsProfileName;
    public int Priority { get; set; } = 100;
    public bool Enabled { get; set; } = true;
    public bool AutoConnect { get; set; } = true;

    // 保留旧配置字段的含义，界面显式切换时同时更新两个字段。
    [JsonIgnore]
    public bool ParticipatesInRecovery
    {
        get => Enabled && AutoConnect;
        set => Enabled = AutoConnect = value;
    }

    [JsonIgnore]
    public bool IsRecoveryCandidate => ParticipatesInRecovery && CredentialSaved;

    public int MinSignalPercent { get; set; }
    public bool CredentialSaved { get; set; }
    public string Security { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastFailureAt { get; set; }
    public int FailureCount { get; set; }
    public double? AverageLatencyMs { get; set; }
    public double? LastSpeedMbps { get; set; }

    [JsonIgnore]
    public string DragHandle => "⋮⋮";

}
