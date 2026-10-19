using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

internal static class NetworkAdapterPresentation
{
    public static string Type(NetworkInterfaceType type, Localizer localizer) => localizer.T(type switch
    {
        NetworkInterfaceType.Wireless80211 => "AdapterTypeWifi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT => "AdapterTypeEthernet",
        NetworkInterfaceType.Tunnel => "AdapterTypeTunnel",
        NetworkInterfaceType.Ppp => "AdapterTypePpp",
        _ => "AdapterTypeOther"
    });

    public static string Status(OperationalStatus status, Localizer localizer) => localizer.T(status switch
    {
        OperationalStatus.Up => "LinkUp", OperationalStatus.Down => "LinkDown",
        OperationalStatus.Testing => "AdapterTesting", OperationalStatus.Dormant => "LinkDormant",
        OperationalStatus.NotPresent => "LinkNotPresent", OperationalStatus.LowerLayerDown => "LinkLowerLayerDown",
        _ => "LinkUnknown"
    });

    public static string Addresses(IEnumerable<IPAddress> addresses)
    {
        var values = addresses.Where(address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .Select(address => address.ToString()).Distinct().ToList();
        return values.Count == 0 ? "—" : string.Join(", ", values);
    }
}
