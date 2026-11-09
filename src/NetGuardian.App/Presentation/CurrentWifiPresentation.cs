using System.Collections.Generic;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Wifi;

namespace NetGuardian.App.Presentation;

internal sealed record CurrentWifiPresentation(string Name, string Details)
{
    public static CurrentWifiPresentation Create(WifiConnectionInfo? info, Localizer localizer)
    {
        if (info is null) return new(localizer.T("WifiCurrentLoading"), "");
        if (!info.ReadSucceeded) return new(localizer.T("WifiCurrentReadFailed"), info.Error ?? "");
        if (!info.Connected) return new(localizer.T("WifiCurrentDisconnected"), "");
        var details = new List<string> { localizer.T("WifiCurrentConnected") };
        if (info.SignalPercent is { } signal) details.Add(string.Format(localizer.T("WifiCurrentSignal"), signal));
        if (!string.IsNullOrWhiteSpace(info.Authentication)) details.Add(info.Authentication);
        if (!string.IsNullOrWhiteSpace(info.Encryption)) details.Add(info.Encryption);
        details.Add(string.Format(localizer.T("WifiCurrentIpv4"), info.Ipv4 ?? localizer.T("WifiCurrentAddressPending")));
        return new(info.Ssid ?? "-", string.Join(" · ", details));
    }
}
