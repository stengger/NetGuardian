using System.Collections.Generic;
using System.Linq;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Presentation;

internal sealed record WifiActionAvailability(WifiProfile? ConnectTarget, bool CanConnect, bool CanAdd, bool CanEdit)
{
    public static WifiActionAvailability Create(IEnumerable<WifiProfile> profiles, WifiProfile? selected,
        string? nearbySsid, bool busy)
    {
        var nearbyProfile = nearbySsid is null ? null : profiles.FirstOrDefault(profile => profile.Ssid == nearbySsid);
        var target = selected ?? nearbyProfile;
        return new(target, !busy && target?.CredentialSaved == true,
            !busy && nearbySsid is not null && nearbyProfile is null,
            !busy && (selected is not null || nearbySsid is not null));
    }
}
