using System.Collections.Generic;
using System.Linq;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Network;

namespace NetGuardian.App.Presentation;

internal static class ProxyVpnPresentation
{
    public static string FormatSystemProxyValue(ProxyAndVpnSnapshot snapshot, Localizer localizer)
    {
        var parts = new List<string>();
        if (snapshot.SystemProxyEnabled)
        {
            var proxyDesc = string.Format(localizer.T("ProxySystemOn"), snapshot.SystemProxyServer ?? "-");
            if (snapshot.LocalProxyReachable is { } reachable && snapshot.LocalProxyEndpoint is { } ep)
            {
                proxyDesc += $" ({string.Format(localizer.T(reachable ? "ProxyLocalReachable" : "ProxyLocalUnreachable"), ep)})";
            }
            parts.Add(proxyDesc);
        }
        else
        {
            parts.Add(localizer.T("ProxySystemOff"));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.AutoConfigUrl))
        {
            parts.Add(string.Format(localizer.T("ProxyPac"), snapshot.AutoConfigUrl));
        }

        if (snapshot.EnvironmentProxies.Count > 0)
        {
            var envList = string.Join(", ", snapshot.EnvironmentProxies.Select(e => $"{e.VariableName}={e.RedactedValue}"));
            parts.Add(string.Format(localizer.T("ProxyEnv"), envList));
        }

        return string.Join(" · ", parts);
    }

    public static string FormatVpnValue(ProxyAndVpnSnapshot snapshot, Localizer localizer)
    {
        if (snapshot.ActiveVpnInterfaces.Count == 0)
        {
            return localizer.T("VpnNoneActive");
        }

        var items = snapshot.ActiveVpnInterfaces.Select(v =>
            v.Addresses.Count > 0 ? $"{v.Name} ({v.Addresses[0]})" : v.Name);
        return string.Join(", ", items);
    }

    public static string FormatBanner(ProxyAndVpnSnapshot snapshot, string? defaultRouteAdapter, Localizer localizer)
    {
        var parts = new List<string>
        {
            FormatSystemProxyValue(snapshot, localizer),
            snapshot.ActiveVpnInterfaces.Count > 0
                ? string.Format(localizer.T("VpnActiveInterfaces"), string.Join(", ", snapshot.ActiveVpnInterfaces.Select(v => v.Name)))
                : localizer.T("VpnNoneActive")
        };

        var routeText = string.IsNullOrWhiteSpace(defaultRouteAdapter)
            ? localizer.T("DefaultRouteNone")
            : defaultRouteAdapter;
        parts.Add(string.Format(localizer.T("DefaultIpv4RouteLabel"), routeText));
        return string.Join(" · ", parts);
    }

    public static string FormatAdapterDetail(AdapterExtendedDetail detail, Localizer localizer)
    {
        var parts = new List<string>
        {
            string.Format(localizer.T("AdapterDetailDefaultRoute"), localizer.T(detail.IsDefaultIpv4Route ? "YesText" : "NoText")),
            string.Format(localizer.T("AdapterDetailMac"), detail.MacAddress),
            string.Format(localizer.T("AdapterDetailLinkSpeed"), detail.LinkSpeedMbps is { } spd ? $"{spd:0.#}" : "—"),
            string.Format(localizer.T("AdapterDetailMtu"), detail.Mtu?.ToString() ?? "—"),
            string.Format(localizer.T("AdapterDetailMetric"), detail.Ipv4Metric?.ToString() ?? "—"),
            string.Format(localizer.T("AdapterDetailIpv6"), detail.Ipv6Addresses.Count > 0 ? string.Join(", ", detail.Ipv6Addresses.Take(2)) : "—")
        };

        var dhcpLabel = detail.DhcpEnabled == true
            ? $"{localizer.T("DhcpEnabledText")}{(string.IsNullOrWhiteSpace(detail.DhcpServer) ? "" : $" ({detail.DhcpServer})")}"
            : detail.DhcpEnabled == false
                ? localizer.T("DhcpStaticText")
                : "—";
        parts.Add(string.Format(localizer.T("AdapterDetailDhcp"), dhcpLabel));
        return string.Join(" · ", parts);
    }
}
