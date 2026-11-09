using NetGuardian.Core.Models;
using NetGuardian.Core.Settings;

namespace NetGuardian.Core.Wifi;

public sealed record SavedWifiProfile(string ProfileName, string Ssid, string Authentication = "");
public sealed record SavedWifiProfileInventory(bool Success, IReadOnlyList<SavedWifiProfile> Profiles, string? Error = null);
public sealed record WifiProfileImportResult(int AddedCount, bool Changed);

public static class SavedWifiProfileImporter
{
    /// <summary>合并至调用方的配置快照，保留已有策略；读取失败时完全不修改。</summary>
    public static WifiProfileImportResult Apply(AppSettings settings, SavedWifiProfileInventory inventory, string? currentSsid)
    {
        if (!inventory.Success) return new(0, false);
        var saved = inventory.Profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Ssid) && !string.IsNullOrWhiteSpace(profile.ProfileName)
                && !profile.ProfileName.StartsWith("NetGuardian-Verify-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var excluded = settings.ExcludedWifiSsids.ToHashSet(StringComparer.Ordinal);
        var known = settings.Profiles.Select(profile => profile.Ssid).ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var profile in settings.Profiles)
        {
            var match = saved.FirstOrDefault(item => item.Ssid == profile.Ssid && item.ProfileName == profile.ConnectionProfileName)
                ?? saved.FirstOrDefault(item => item.Ssid == profile.Ssid);
            var available = match is not null;
            if (profile.CredentialSaved != available) { profile.CredentialSaved = available; changed = true; }
            if (match is null) continue;
            if (profile.WindowsProfileName != match.ProfileName) { profile.WindowsProfileName = match.ProfileName; changed = true; }
            if (string.IsNullOrEmpty(profile.Security) && !string.IsNullOrEmpty(match.Authentication))
            { profile.Security = match.Authentication; changed = true; }
        }

        var priority = settings.Profiles.Count == 0 ? 0 : settings.Profiles.Max(profile => profile.Priority);
        var added = 0;
        foreach (var profile in saved.OrderByDescending(profile => profile.Ssid == currentSsid))
        {
            if (excluded.Contains(profile.Ssid) || !known.Add(profile.Ssid)) continue;
            settings.Profiles.Add(new WifiProfile
            {
                Ssid = profile.Ssid, WindowsProfileName = profile.ProfileName,
                Security = profile.Authentication, CredentialSaved = true,
                Priority = ++priority, Enabled = true, AutoConnect = true
            });
            added++;
        }
        return new(added, changed || added > 0);
    }
}
