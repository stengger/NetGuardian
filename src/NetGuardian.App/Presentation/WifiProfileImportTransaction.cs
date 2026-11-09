using System;
using System.Collections.Generic;
using System.Text.Json;
using NetGuardian.Core.Models;
using NetGuardian.Core.Settings;
using NetGuardian.Core.Wifi;

namespace NetGuardian.App.Presentation;

internal static class WifiProfileImportTransaction
{
    public static (AppSettings Snapshot, WifiProfileImportResult Result) PrepareAndSave(
        AppSettings settings, IEnumerable<WifiProfile> profiles, SavedWifiProfileInventory inventory,
        string? currentSsid, Action<AppSettings> save)
    {
        var snapshot = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        snapshot.Profiles = JsonSerializer.Deserialize<List<WifiProfile>>(JsonSerializer.Serialize(profiles))!;
        var result = SavedWifiProfileImporter.Apply(snapshot, inventory, currentSsid);
        if (result.Changed) save(snapshot);
        return (snapshot, result);
    }
}
