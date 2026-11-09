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
