using NetGuardian.Core.Models;

namespace NetGuardian.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "zh-CN";
    public string CloseBehavior { get; set; } = "ask";
    public bool StartMonitoringOnLaunch { get; set; }
    public bool StartMinimizedToTray { get; set; }
    public int LogRetentionDays { get; set; } = 14;
    // 整机重启只能作为设备故障的最后兜底，默认关闭。
    public bool EnableComputerRestart { get; set; }
    public int DeviceFailureMinutesBeforeRestart { get; set; } = 30;
    /// <summary>Theme mode: "auto" (follow OS), "light" or "dark".</summary>
    public string Theme { get; set; } = "auto";
    public MonitorPolicy Policy { get; set; } = new();
    public List<WifiProfile> Profiles { get; set; } = [];
}
