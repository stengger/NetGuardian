using System.Net.NetworkInformation;
using NetGuardian.Core.Network;
using Xunit;

namespace NetGuardian.Tests;

public sealed class ProxyAndVpnInspectorTests
{
    [Fact]
    public void Inspect_ReadsProxySettings_RedactsCredentials_AndProbesLocalEndpoint()
    {
        var reg = new ProxyRegistryValues(
            ProxyEnabled: true,
            ProxyServer: "http=http://alice:secret@127.0.0.1:7890;https=https://127.0.0.1:7890",
            ProxyOverride: "localhost;127.*;<local>",
            AutoConfigUrl: "http://user:pw@127.0.0.1:7891/pac");

        var env = new Dictionary<string, string?>
        {
            ["HTTPS_PROXY"] = "http://bob:tok@proxy.corp.local:8080",
            ["NO_PROXY"] = "localhost,10.0.0.0/8"
        };

        var probedHost = string.Empty;
        var probedPort = 0;
        var snapshot = ProxyAndVpnInspector.Inspect(
            registryOverride: reg,
            envOverride: env,
            interfacesOverride: [],
            localPortProbe: (host, port) =>
            {
                probedHost = host;
                probedPort = port;
                return true;
            });

        Assert.True(snapshot.SystemProxyEnabled);
        Assert.DoesNotContain("secret", snapshot.SystemProxyServer ?? string.Empty);
        Assert.DoesNotContain("alice", snapshot.SystemProxyServer ?? string.Empty);
        Assert.DoesNotContain("pw", snapshot.AutoConfigUrl ?? string.Empty);
        Assert.Equal("127.0.0.1", probedHost);
        Assert.Equal(7890, probedPort);
        Assert.True(snapshot.LocalProxyReachable);
        Assert.Equal("127.0.0.1:7890", snapshot.LocalProxyEndpoint);

        var httpsEnv = Assert.Single(snapshot.EnvironmentProxies, e => e.VariableName == "HTTPS_PROXY");
        Assert.DoesNotContain("tok", httpsEnv.RedactedValue);
        Assert.Contains("proxy.corp.local:8080", httpsEnv.RedactedValue);
    }

    [Fact]
    public void DetectActiveVpnInterfaces_IdentifiesTunAndVpn_ExcludesHyperVAndDownAdapters()
    {
        var candidates = new List<VpnInterfaceCandidate>
        {
            new(
                "Clash",
                "Clash Meta Tunnel",
                NetworkInterfaceType.Unknown,
                OperationalStatus.Up,
                ["198.18.0.1"],
                []),
            new(
                "wg0",
                "WireGuard Tunnel",
                NetworkInterfaceType.Tunnel,
                OperationalStatus.Up,
                ["10.66.66.2"],
                []),
            new(
                "vEthernet (Default Switch)",
                "Hyper-V Virtual Ethernet Adapter",
                NetworkInterfaceType.Ethernet,
                OperationalStatus.Up,
                ["172.20.0.1"],
                []),
            new(
                "OpenVPN TAP",
                "TAP-Windows Adapter V9",
                NetworkInterfaceType.Ethernet,
                OperationalStatus.Down,
                ["10.8.0.6"],
                [])
        };

        var active = ProxyAndVpnInspector.DetectActiveVpnInterfaces(candidates);

        Assert.Equal(2, active.Count);
        Assert.Contains(active, a => a.Name == "Clash");
        Assert.Contains(active, a => a.Name == "wg0");
        Assert.DoesNotContain(active, a => a.Name.Contains("vEthernet"));
        Assert.DoesNotContain(active, a => a.Name == "OpenVPN TAP");
    }

    [Fact]
    public void TryExtractLocalProxyEndpoint_SupportsBareHostPortAndIpv6Loopback()
    {
        var bare = ProxyAndVpnInspector.TryExtractLocalProxyEndpoint("127.0.0.1:10808");
        Assert.NotNull(bare);
        Assert.Equal("127.0.0.1", bare.Value.Host);
        Assert.Equal(10808, bare.Value.Port);

        var remote = ProxyAndVpnInspector.TryExtractLocalProxyEndpoint("10.0.0.5:3128");
        Assert.Null(remote);

        var ipv6 = ProxyAndVpnInspector.TryExtractLocalProxyEndpoint("[::1]:7890");
        Assert.NotNull(ipv6);
        Assert.Equal("::1", ipv6.Value.Host);
        Assert.Equal(7890, ipv6.Value.Port);
    }
}
