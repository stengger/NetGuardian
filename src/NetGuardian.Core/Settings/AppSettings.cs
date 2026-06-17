using NetGuardian.Core.Models;

namespace NetGuardian.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "zh-CN";
    public string CloseBehavior { get; set; } = "ask";
    public bool StartMonitoringOnLaunch { get; set; }
    public bool StartMinimizedToTray { get; set; }
    public MonitorPolicy Policy { get; set; } = new();
    public List<WifiProfile> Profiles { get; set; } = [];
}
