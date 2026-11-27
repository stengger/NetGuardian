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
        if (!string.IsNullOrWhiteSpace(info.Band))
            details.Add(info.Channel is { } ch ? $"{info.Band} Ch{ch}" : info.Band);
        else if (info.Channel is { } chOnly)
            details.Add($"Ch{chOnly}");
        if (!string.IsNullOrWhiteSpace(info.RadioType)) details.Add(info.RadioType);
        if (info.ReceiveRateMbps is { } rx || info.TransmitRateMbps is { } tx)
        {
            var rateText = info.ReceiveRateMbps is { } r && info.TransmitRateMbps is { } t
                ? $"{r:0.#}/{t:0.#} Mbps"
                : $"{(info.ReceiveRateMbps ?? info.TransmitRateMbps):0.#} Mbps";
            details.Add(rateText);
        }
        if (!string.IsNullOrWhiteSpace(info.Bssid)) details.Add($"BSSID {info.Bssid}");
        return new(info.Ssid ?? "-", string.Join(" · ", details));
    }
}
