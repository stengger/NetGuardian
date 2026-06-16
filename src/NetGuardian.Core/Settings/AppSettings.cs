using NetGuardian.Core.Models;

namespace NetGuardian.Core.Settings;

public sealed class AppSettings
{
    public string Language { get; set; } = "zh-CN";
    public MonitorPolicy Policy { get; set; } = new();
    public List<WifiProfile> Profiles { get; set; } = [];
}

