using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NetGuardian.Core.Network;

/// <summary>选中网卡的扩展只读详情（MAC/速率/MTU/默认路由跃点数/IPv6/DHCP）。</summary>
public sealed record AdapterExtendedDetail(
    string InterfaceId,
    string Name,
    bool IsDefaultIpv4Route,
    int? Ipv4Metric,
    string MacAddress,
    double? LinkSpeedMbps,
    int? Mtu,
    IReadOnlyList<string> Ipv6Addresses,
    bool? DhcpEnabled,
    string? DhcpServer,
    TimeSpan? DhcpLeaseRemaining);

/// <summary>系统 hosts 文件只读检查摘要。</summary>
public sealed record HostsFileSummary(
    int CustomEntryCount,
    IReadOnlyList<string> SampleHosts,
    bool ReadSucceeded,
    string? Error = null);

/// <summary>本机与公网 IPv6 双栈可达状态。</summary>
public sealed record Ipv6DualStackStatus(
    bool HasLocalGlobalIpv6,
    string? LocalGlobalIpv6,
    bool PublicIpv6Reachable,
    string? PublicIpv6);

/// <summary>采集默认路由出口、网卡物理/DHCP 扩展属性、hosts 自定义条目及 IPv6 双栈状态。</summary>
public static class NetworkEnvironmentInspector
{
    private const uint PublicProbeAddressNetworkOrder = 0x01010101; // 1.1.1.1

    /// <summary>查询当前系统的 IPv4 默认路由接口索引（失败返回 null）。</summary>
    public static int? GetDefaultIpv4RouteInterfaceIndex()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            if (NativeIpHelper.GetBestRoute(PublicProbeAddressNetworkOrder, 0, out var row) == 0 && row.dwForwardIfIndex > 0)
            {
                return unchecked((int)row.dwForwardIfIndex);
            }
        }
        catch
        {
            // 忽略 P/Invoke 或环境异常，降级返回 null。
        }
        return null;
    }

    /// <summary>根据默认出口接口索引查找网卡展示名称。</summary>
    public static string? GetDefaultRouteAdapterName(int? defaultIpv4Index = null)
    {
        var idx = defaultIpv4Index ?? GetDefaultIpv4RouteInterfaceIndex();
        if (idx is null)
        {
            return null;
        }

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                try
                {
                    var v4 = nic.GetIPProperties().GetIPv4Properties();
                    if (v4 is not null && v4.Index == idx.Value)
                    {
                        return nic.Name;
                    }
                }
                catch
                {
                    // 无 IPv4 属性的网卡跳过。
                }
            }
        }
        catch
        {
            // 忽略接口枚举异常。
        }
        return null;
    }

    /// <summary>读取指定网卡的 MAC、链路速率、MTU、IPv4 Metric 与 DHCP 租约信息。</summary>
    public static AdapterExtendedDetail ReadAdapterDetail(NetworkInterface adapter, int? defaultIpv4Index = null)
    {
        var resolvedDefaultIdx = defaultIpv4Index ?? GetDefaultIpv4RouteInterfaceIndex();
        var props = adapter.GetIPProperties();
        IPv4InterfaceProperties? ipv4Props = null;
        if (adapter.Supports(NetworkInterfaceComponent.IPv4))
        {
            try { ipv4Props = props.GetIPv4Properties(); } catch { }
        }

        var isDefault = resolvedDefaultIdx is { } defIdx && ipv4Props is not null && ipv4Props.Index == defIdx;
        int? mtu = ipv4Props?.Mtu > 0 ? ipv4Props.Mtu : null;
        var macBytes = adapter.GetPhysicalAddress().GetAddressBytes();
        var macText = macBytes.Length > 0 ? string.Join(":", macBytes.Select(b => b.ToString("X2"))) : "-";
        double? speedMbps = adapter.Speed > 0 ? Math.Round(adapter.Speed / 1_000_000.0, 1) : null;

        var ipv6List = props.UnicastAddresses
            .Where(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6 && !u.Address.IsIPv6LinkLocal)
            .Select(u => StripScopeId(u.Address.ToString()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dhcpServer = OperatingSystem.IsWindows() || OperatingSystem.IsLinux()
            ? props.DhcpServerAddresses
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(ip))
                .Select(ip => ip.ToString())
                .FirstOrDefault()
            : null;

        bool? dhcpEnabled = null;
        TimeSpan? leaseRemaining = null;
        if (OperatingSystem.IsWindows() && ipv4Props is not null)
        {
            dhcpEnabled = ipv4Props.IsDhcpEnabled;
            if (dhcpEnabled == true)
            {
                var v4Unicast = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                if (v4Unicast is not null)
                {
                    try
                    {
                        if (v4Unicast.AddressPreferredLifetime is > 0 and < uint.MaxValue)
                        {
                            leaseRemaining = TimeSpan.FromSeconds(v4Unicast.AddressPreferredLifetime);
                        }
                    }
                    catch
                    {
                        // 某些虚拟网卡不支持 DhcpLeaseLifetime。
                    }
                }
            }
        }

        var metric = ipv4Props is not null ? TryReadIpv4InterfaceMetric(ipv4Props.Index) : null;

        return new AdapterExtendedDetail(
            InterfaceId: adapter.Id,
            Name: adapter.Name,
            IsDefaultIpv4Route: isDefault,
            Ipv4Metric: metric,
            MacAddress: macText,
            LinkSpeedMbps: speedMbps,
            Mtu: mtu,
            Ipv6Addresses: ipv6List,
            DhcpEnabled: dhcpEnabled,
            DhcpServer: dhcpServer,
            DhcpLeaseRemaining: leaseRemaining);
    }

    /// <summary>只读检查系统 hosts 文件中的自定义域名映射数量及前几项主机名示例。</summary>
    public static HostsFileSummary InspectHostsFile(string? hostsFilePath = null)
    {
        var path = !string.IsNullOrWhiteSpace(hostsFilePath)
            ? hostsFilePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

        if (!File.Exists(path))
        {
            return new HostsFileSummary(0, [], true);
        }

        try
        {
            var customCount = 0;
            var samples = new List<string>();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            while (sr.ReadLine() is { } line)
            {
                var hashIdx = line.IndexOf('#');
                var effective = (hashIdx >= 0 ? line[..hashIdx] : line).Trim();
                if (effective.Length == 0)
                {
                    continue;
                }

                var parts = effective.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !IPAddress.TryParse(parts[0], out _))
                {
                    continue;
                }

                for (var i = 1; i < parts.Length; i++)
                {
                    var host = parts[i].Trim();
                    if (host.Length == 0
                        || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                        || host.Equals("broadcasthost", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    customCount++;
                    if (samples.Count < 5 && !samples.Contains(host, StringComparer.OrdinalIgnoreCase))
                    {
                        samples.Add(host);
                    }
                }
            }

            return new HostsFileSummary(customCount, samples, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HostsFileSummary(0, [], false, ex.Message);
        }
    }

    /// <summary>检查本机全局 IPv6 与公网 IPv6 双栈连通状态。</summary>
    public static async Task<Ipv6DualStackStatus> ProbeIpv6StatusAsync(
        CancellationToken cancellationToken = default,
        IReadOnlyList<IPAddress>? localAddressesOverride = null,
        Func<CancellationToken, Task<string?>>? publicIpv6Lookup = null)
    {
        var globalIpv6 = FindFirstLocalGlobalIpv6(localAddressesOverride);
        if (globalIpv6 is null)
        {
            return new Ipv6DualStackStatus(false, null, false, null);
        }

        string? publicV6 = null;
        try
        {
            publicV6 = publicIpv6Lookup is not null
                ? await publicIpv6Lookup(cancellationToken)
                : await FetchPublicIpv6Async(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            publicV6 = null;
        }

        var validPublic = !string.IsNullOrWhiteSpace(publicV6)
            && IPAddress.TryParse(publicV6.Trim(), out var parsed)
            && parsed.AddressFamily == AddressFamily.InterNetworkV6;

        return new Ipv6DualStackStatus(
            true,
            globalIpv6,
            validPublic,
            validPublic ? publicV6!.Trim() : null);
    }

    internal static string? FindFirstLocalGlobalIpv6(IReadOnlyList<IPAddress>? overrideList = null)
    {
        IEnumerable<IPAddress> addresses;
        if (overrideList is not null)
        {
            addresses = overrideList;
        }
        else
        {
            var collected = new List<IPAddress>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }
                try
                {
                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        collected.Add(unicast.Address);
                    }
                }
                catch
                {
                    // 忽略瞬态属性读取失败。
                }
            }
            addresses = collected;
        }

        foreach (var addr in addresses)
        {
            if (addr.AddressFamily != AddressFamily.InterNetworkV6)
            {
                continue;
            }
            if (IPAddress.IsLoopback(addr) || addr.IsIPv6LinkLocal || addr.IsIPv6SiteLocal || addr.IsIPv6Multicast)
            {
                continue;
            }

            var bytes = addr.GetAddressBytes();
            // 排除 fc00::/7 (Unique Local Address)
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                continue;
            }

            return StripScopeId(addr.ToString());
        }

        return null;
    }

    private static async Task<string?> FetchPublicIpv6Async(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(2500));
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(2500) };
        var text = await client.GetStringAsync("https://api6.ipify.org?format=text", cts.Token);
        return text.Trim();
    }

    private static string StripScopeId(string ip)
    {
        var pct = ip.IndexOf('%');
        return pct > 0 ? ip[..pct] : ip;
    }

    private static int? TryReadIpv4InterfaceMetric(int interfaceIndex)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            if (NativeIpHelper.GetBestRoute(PublicProbeAddressNetworkOrder, 0, out var row) == 0
                && row.dwForwardIfIndex == unchecked((uint)interfaceIndex)
                && row.dwForwardMetric1 > 0)
            {
                return unchecked((int)row.dwForwardMetric1);
            }
        }
        catch
        {
            // 忽略不支持此查询的场景。
        }
        return null;
    }

    private static class NativeIpHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_IPFORWARDROW
        {
            public uint dwForwardDest;
            public uint dwForwardMask;
            public uint dwForwardPolicy;
            public uint dwForwardNextHop;
            public uint dwForwardIfIndex;
            public uint dwForwardType;
            public uint dwForwardProto;
            public uint dwForwardAge;
            public uint dwForwardNextHopAS;
            public uint dwForwardMetric1;
            public uint dwForwardMetric2;
            public uint dwForwardMetric3;
            public uint dwForwardMetric4;
            public uint dwForwardMetric5;
        }

        [DllImport("iphlpapi.dll", SetLastError = false)]
        public static extern uint GetBestRoute(uint dwDestAddr, uint dwSourceAddr, out MIB_IPFORWARDROW pBestRoute);
    }
}
