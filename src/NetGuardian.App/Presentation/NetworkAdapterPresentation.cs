using System;
using System.Globalization;
using System.Windows.Data;
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

    internal static string AddressLines(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "—") return value;
        var addresses = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ipv4 = addresses.Where(address => IPAddress.TryParse(address, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork).ToArray();
        var ipv6 = addresses.Where(address => IPAddress.TryParse(address, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
        var other = addresses.Where(address => !IPAddress.TryParse(address, out _)).ToArray();
        return string.Join(Environment.NewLine, new[] {
            ipv4.Length > 0 ? "IPv4  " + string.Join(", ", ipv4) : null,
            ipv6.Length > 0 ? "IPv6  " + string.Join(", ", ipv6) : null,
            other.Length > 0 ? string.Join(", ", other) : null }.Where(line => line is not null));
    }

    public static string Addresses(IEnumerable<IPAddress> addresses)
    {
        var values = addresses.Where(address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .Select(address => address.ToString()).Distinct().ToList();
        return values.Count == 0 ? "—" : string.Join(", ", values);
    }
}

public sealed class AddressLinesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        NetworkAdapterPresentation.AddressLines(value?.ToString() ?? "—");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
