using System.Collections;
using System.Collections.Generic;
using System;
using System.Windows.Data;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Presentation;

internal sealed class NearbySavedWifiView
{
    private HashSet<string> _nearby = new(StringComparer.Ordinal);
    public ListCollectionView View { get; }

    public NearbySavedWifiView(IList profiles)
    {
        View = new ListCollectionView(profiles);
        View.Filter = item => item is WifiProfile profile && profile.CredentialSaved && _nearby.Contains(profile.Ssid);
    }

    public void Update(IEnumerable<string> nearbySsids)
    {
        _nearby = new(nearbySsids, StringComparer.Ordinal);
        View.Refresh();
    }
}
