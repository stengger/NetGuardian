using NetGuardian.Core.Models;

namespace NetGuardian.Core.Wifi;

public sealed record WifiProfileInventory(bool Success, IReadOnlyList<string> Names, string? Error = null);

public static class WifiProfileAvailability
{
    /// <summary>Windows 的现有配置是凭据可用性的依据；服务故障时不清空最近有效标记。</summary>
    public static void Apply(IEnumerable<WifiProfile> profiles, WifiProfileInventory inventory)
    {
        if (!inventory.Success) return;
        var saved = inventory.Names.ToHashSet(StringComparer.Ordinal);
        foreach (var profile in profiles) profile.CredentialSaved = saved.Contains(profile.Ssid);
    }
}
