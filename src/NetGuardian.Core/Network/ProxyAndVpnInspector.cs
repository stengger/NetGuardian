using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NetGuardian.Core.Network;

/// <summary>系统代理与环境变量的解析输入。</summary>
public sealed record ProxyRegistryValues(
    bool ProxyEnabled,
    string? ProxyServer,
    string? ProxyOverride,
    string? AutoConfigUrl);

/// <summary>单个网卡的简要快照（便于探测与单元测试注入）。</summary>
public sealed record VpnInterfaceCandidate(
    string Name,
    string Description,
    NetworkInterfaceType InterfaceType,
    OperationalStatus OperationalStatus,
    IReadOnlyList<string> IPv4Addresses,
    IReadOnlyList<string> IPv6Addresses);

/// <summary>识别到的活动 VPN / TUN 接口信息。</summary>
public sealed record DetectedVpnInterface(
    string Name,
    string Description,
    string InterfaceType,
    IReadOnlyList<string> Addresses);

/// <summary>单个环境代理变量条目（已脱敏）。</summary>
public sealed record EnvironmentProxyEntry(
    string VariableName,
    string RedactedValue);

/// <summary>VPN 与系统代理检查快照。</summary>
public sealed record ProxyAndVpnSnapshot(
    bool SystemProxyEnabled,
    string? SystemProxyServer,
    string? SystemProxyOverride,
    string? AutoConfigUrl,
    bool? LocalProxyReachable,
    string? LocalProxyEndpoint,
    IReadOnlyList<EnvironmentProxyEntry> EnvironmentProxies,
    IReadOnlyList<DetectedVpnInterface> ActiveVpnInterfaces,
    DateTimeOffset CapturedAtUtc);

/// <summary>读取 Windows 系统代理、代理环境变量与活动 VPN/TUN 接口状态。</summary>
public static class ProxyAndVpnInspector
{
    private const string InternetSettingsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int TcpProbeTimeoutMs = 500;

    private static readonly string[] EnvVarNames =
    [
        "HTTPS_PROXY", "https_proxy",
        "HTTP_PROXY", "http_proxy",
        "ALL_PROXY", "all_proxy",
        "NO_PROXY", "no_proxy"
    ];

    private static readonly string[] VpnKeywords =
    [
        "wireguard", "wintun", "tailscale", "clash", "mihomo", "sing-box",
        "openvpn", "tap-windows", "tap-adapter", "cisco", "anyconnect",
        "fortinet", "forticlient", "globalprotect", "pulse secure",
        "juniper", "zerotier", "nekobox", "v2ray", "shadowsocks", "warp",
        "cloudflare", "tun", "vpn"
    ];

    private static readonly string[] NonVpnVirtualExclusions =
    [
        "hyper-v", "vethernet", "vmware", "virtualbox", "wsl",
        "bluetooth", "loopback", "kernel debug", "teredo", "isatap", "6to4"
    ];

    /// <summary>采集当前系统的代理与 VPN/TUN 状态。</summary>
    public static ProxyAndVpnSnapshot Inspect(
        ProxyRegistryValues? registryOverride = null,
        IReadOnlyDictionary<string, string?>? envOverride = null,
        IReadOnlyList<VpnInterfaceCandidate>? interfacesOverride = null,
        Func<string, int, bool>? localPortProbe = null)
    {
        var reg = registryOverride ?? ReadWindowsInternetSettings();
        var envEntries = CollectEnvironmentProxies(envOverride);
        var vpnList = DetectActiveVpnInterfaces(interfacesOverride ?? ReadSystemInterfaces());

        var probeTarget = reg.ProxyEnabled ? TryExtractLocalProxyEndpoint(reg.ProxyServer) : null;
        bool? localReachable = null;
        string? localEndpointText = null;
        if (probeTarget is { } target)
        {
            localEndpointText = $"{target.Host}:{target.Port}";
            localReachable = (localPortProbe ?? ProbeTcpPort)(target.Host, target.Port);
        }

        return new ProxyAndVpnSnapshot(
            SystemProxyEnabled: reg.ProxyEnabled,
            SystemProxyServer: SanitizeProxyValue(reg.ProxyServer),
            SystemProxyOverride: NullIfWhiteSpace(reg.ProxyOverride),
            AutoConfigUrl: SanitizeProxyValue(reg.AutoConfigUrl),
            LocalProxyReachable: localReachable,
            LocalProxyEndpoint: localEndpointText,
            EnvironmentProxies: envEntries,
            ActiveVpnInterfaces: vpnList,
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }

    /// <summary>从代理环境变量字典中提取并脱敏代理配置。</summary>
    public static IReadOnlyList<EnvironmentProxyEntry> CollectEnvironmentProxies(
        IReadOnlyDictionary<string, string?>? envOverride = null)
    {
        var results = new List<EnvironmentProxyEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in EnvVarNames)
        {
            var raw = envOverride is not null
                ? (envOverride.TryGetValue(name, out var v) ? v : null)
                : Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(raw) || !seen.Add(name))
            {
                continue;
            }

            var value = name.Equals("NO_PROXY", StringComparison.OrdinalIgnoreCase)
                ? raw.Trim()
                : SanitizeProxyValue(raw) ?? raw.Trim();
            results.Add(new EnvironmentProxyEntry(name, value));
        }
        return results;
    }

    /// <summary>识别处于活动状态（Up）的 VPN / TUN 网络接口。</summary>
    public static IReadOnlyList<DetectedVpnInterface> DetectActiveVpnInterfaces(
        IReadOnlyList<VpnInterfaceCandidate> candidates)
    {
        var result = new List<DetectedVpnInterface>();
        foreach (var nic in candidates)
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (!IsVpnOrTunInterface(nic.Name, nic.Description, nic.InterfaceType))
            {
                continue;
            }

            var addresses = new List<string>(nic.IPv4Addresses.Count + nic.IPv6Addresses.Count);
            addresses.AddRange(nic.IPv4Addresses);
            addresses.AddRange(nic.IPv6Addresses);
            result.Add(new DetectedVpnInterface(
                nic.Name,
                nic.Description,
                nic.InterfaceType.ToString(),
                addresses));
        }
        return result;
    }

    /// <summary>判断网卡名称、描述或类型是否属于 VPN / TUN 接口，同时过滤本地虚拟机交换机。</summary>
    public static bool IsVpnOrTunInterface(string? name, string? description, NetworkInterfaceType interfaceType)
    {
        var combined = $"{name} {description}".Trim();
        if (ContainsAnyKeyword(combined, NonVpnVirtualExclusions))
        {
            return false;
        }

        if (interfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
        {
            return true;
        }

        return ContainsVpnToken(combined);
    }

    /// <summary>从 Windows ProxyServer 字符串（如 127.0.0.1:7890 或 http=127.0.0.1:7890;https=...）中提取本机端口。</summary>
    public static (string Host, int Port)? TryExtractLocalProxyEndpoint(string? proxyServer)
    {
        if (string.IsNullOrWhiteSpace(proxyServer))
        {
            return null;
        }

        var segments = proxyServer.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var rawSegment in segments)
        {
            var segment = rawSegment;
            var eqIdx = segment.IndexOf('=');
            if (eqIdx >= 0)
            {
                segment = segment[(eqIdx + 1)..].Trim();
            }

            if (TryParseHostPort(segment, out var host, out var port) && IsLocalHost(host))
            {
                return (host, port);
            }
        }
        return null;
    }

    /// <summary>对代理或 PAC URL 中的 user:password@ 凭据进行脱敏。</summary>
    public static string? SanitizeProxyValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        var segments = trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length > 1)
        {
            return string.Join(';', segments.Select(SanitizeSingleProxySegment));
        }
        return SanitizeSingleProxySegment(trimmed);
    }

    private static string SanitizeSingleProxySegment(string segment)
    {
        var eqIdx = segment.IndexOf('=');
        if (eqIdx > 0 && !segment[..eqIdx].Contains("://", StringComparison.Ordinal))
        {
            var prefix = segment[..(eqIdx + 1)];
            var rest = segment[(eqIdx + 1)..];
            return prefix + RedactUriUserInfo(rest);
        }
        return RedactUriUserInfo(segment);
    }

    private static string RedactUriUserInfo(string candidate)
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
        {
            var builder = new UriBuilder(uri)
            {
                UserName = "***",
                Password = string.Empty
            };
            return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        var atIdx = candidate.IndexOf('@');
        if (atIdx > 0 && !candidate.Contains(' '))
        {
            var schemeIdx = candidate.IndexOf("://", StringComparison.Ordinal);
            if (schemeIdx >= 0 && schemeIdx + 3 < atIdx)
            {
                return candidate[..(schemeIdx + 3)] + "***@" + candidate[(atIdx + 1)..];
            }
            if (schemeIdx < 0)
            {
                return "***@" + candidate[(atIdx + 1)..];
            }
        }
        return candidate;
    }

    private static bool TryParseHostPort(string value, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        var candidate = value.Trim();
        if (candidate.Contains("://", StringComparison.Ordinal))
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Port > 0)
            {
                host = uri.Host;
                port = uri.Port;
                return true;
            }
            return false;
        }

        var atIdx = candidate.LastIndexOf('@');
        if (atIdx >= 0)
        {
            candidate = candidate[(atIdx + 1)..];
        }

        if (candidate.StartsWith('[') && candidate.Contains(']'))
        {
            var endBracket = candidate.IndexOf(']');
            host = candidate[1..endBracket];
            var rest = candidate[(endBracket + 1)..];
            return rest.StartsWith(':') && int.TryParse(rest[1..], out port) && port is > 0 and <= 65535;
        }

        var colonIdx = candidate.LastIndexOf(':');
        if (colonIdx <= 0)
        {
            return false;
        }

        host = candidate[..colonIdx].Trim();
        return int.TryParse(candidate[(colonIdx + 1)..], out port) && port is > 0 and <= 65535;
    }

    private static bool IsLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (IPAddress.TryParse(host, out var ip))
        {
            return IPAddress.IsLoopback(ip);
        }
        return false;
    }

    private static bool ProbeTcpPort(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(host, port);
            return task.Wait(TcpProbeTimeoutMs) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<VpnInterfaceCandidate> ReadSystemInterfaces()
    {
        var list = new List<VpnInterfaceCandidate>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var v4 = new List<string>();
                var v6 = new List<string>();
                try
                {
                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            v4.Add(unicast.Address.ToString());
                        }
                        else if (unicast.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            v6.Add(unicast.Address.ToString());
                        }
                    }
                }
                catch
                {
                    // 忽略瞬态接口属性读取失败。
                }

                list.Add(new VpnInterfaceCandidate(
                    nic.Name,
                    nic.Description,
                    nic.NetworkInterfaceType,
                    nic.OperationalStatus,
                    v4,
                    v6));
            }
        }
        catch
        {
            // 忽略接口枚举异常，返回已收集条目。
        }
        return list;
    }

    private static bool ContainsAnyKeyword(string text, string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsVpnToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var kw in VpnKeywords)
        {
            if (kw.Equals("tun", StringComparison.Ordinal))
            {
                // 避免匹配 "tuning"/"tunnel" 以外无关词；检查独立词或前缀如 tun0/utun/wintun。
                var tokens = text.Split([' ', '-', '_', '(', ')', '/', '\\'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokens)
                {
                    if (token.Equals("tun", StringComparison.OrdinalIgnoreCase)
                        || token.Equals("utun", StringComparison.OrdinalIgnoreCase)
                        || (token.StartsWith("tun", StringComparison.OrdinalIgnoreCase)
                            && token.Length <= 5
                            && token[3..].All(char.IsDigit))
                        || (token.StartsWith("utun", StringComparison.OrdinalIgnoreCase)
                            && token.Length <= 6
                            && token[4..].All(char.IsDigit)))
                    {
                        return true;
                    }
                }
                continue;
            }

            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ProxyRegistryValues ReadWindowsInternetSettings()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ProxyRegistryValues(false, null, null, null);
        }

        if (NativeRegistry.RegOpenKeyExW(
            NativeRegistry.HKEY_CURRENT_USER,
            InternetSettingsSubKey,
            0,
            NativeRegistry.KEY_READ,
            out var hKey) != 0)
        {
            return new ProxyRegistryValues(false, null, null, null);
        }

        try
        {
            var proxyEnable = ReadDword(hKey, "ProxyEnable") == 1;
            var proxyServer = ReadString(hKey, "ProxyServer");
            var proxyOverride = ReadString(hKey, "ProxyOverride");
            var autoConfigUrl = ReadString(hKey, "AutoConfigURL");
            return new ProxyRegistryValues(proxyEnable, proxyServer, proxyOverride, autoConfigUrl);
        }
        finally
        {
            _ = NativeRegistry.RegCloseKey(hKey);
        }
    }

    private static int ReadDword(IntPtr hKey, string valueName)
    {
        uint type = 0;
        uint size = 4;
        var data = new byte[4];
        var rc = NativeRegistry.RegQueryValueExW(hKey, valueName, IntPtr.Zero, ref type, data, ref size);
        if (rc == 0 && size >= 4 && type == NativeRegistry.REG_DWORD)
        {
            return BitConverter.ToInt32(data, 0);
        }
        return 0;
    }

    private static string? ReadString(IntPtr hKey, string valueName)
    {
        uint type = 0;
        uint size = 0;
        var rc = NativeRegistry.RegQueryValueExW(hKey, valueName, IntPtr.Zero, ref type, null, ref size);
        if (rc != 0 || size == 0 || (type != NativeRegistry.REG_SZ && type != NativeRegistry.REG_EXPAND_SZ))
        {
            return null;
        }

        var buffer = new byte[size];
        rc = NativeRegistry.RegQueryValueExW(hKey, valueName, IntPtr.Zero, ref type, buffer, ref size);
        if (rc != 0 || size == 0)
        {
            return null;
        }

        var text = System.Text.Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static class NativeRegistry
    {
        public static readonly IntPtr HKEY_CURRENT_USER = new(unchecked((int)0x80000001));
        public const int KEY_READ = 0x20019;
        public const uint REG_SZ = 1;
        public const uint REG_EXPAND_SZ = 2;
        public const uint REG_DWORD = 4;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        public static extern int RegOpenKeyExW(
            IntPtr hKey,
            string lpSubKey,
            int ulOptions,
            int samDesired,
            out IntPtr phkResult);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        public static extern int RegQueryValueExW(
            IntPtr hKey,
            string lpValueName,
            IntPtr lpReserved,
            ref uint lpType,
            byte[]? lpData,
            ref uint lpcbData);

        [DllImport("advapi32.dll", SetLastError = false)]
        public static extern int RegCloseKey(IntPtr hKey);
    }
}
