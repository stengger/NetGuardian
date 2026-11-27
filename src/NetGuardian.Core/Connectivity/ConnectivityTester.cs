using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetGuardian.Core.Models;
using NetGuardian.Core.Wifi;

namespace NetGuardian.Core.Connectivity;

public sealed class ConnectivityTester
{
    private readonly MonitorPolicy _policy;
    private readonly IConnectivityEnvironment _environment;
    private readonly IConnectivityProbes _probes;

    private readonly bool _requireWifiRoute;

    public ConnectivityTester(MonitorPolicy policy) : this(policy, new SystemConnectivityEnvironment(), new SystemConnectivityProbes(), requireWifiRoute: true) { }

    public ConnectivityTester(MonitorPolicy policy, IConnectivityEnvironment environment, IConnectivityProbes probes, bool requireWifiRoute = false)
    {
        _policy = policy;
        _environment = environment;
        _probes = probes;
        _requireWifiRoute = requireWifiRoute;
    }

    /// <summary>只读接口快照及公网探测，不执行系统命令和独立网关、DNS 诊断。</summary>
    public async Task<ConnectivityReport> RunLightweightCheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var started = DateTimeOffset.Now;
        var snapshot = _environment.Capture();
        var probes = new List<ConnectivityProbe>();
        var state = NetworkState.Disconnected;
        if (snapshot.AdapterPresent && snapshot.AdapterUp && !string.IsNullOrWhiteSpace(snapshot.Ipv4))
        {
            var targets = MonitorPolicy.NormalizeProbeList(_policy.HttpsProbeHosts, MonitorPolicy.DefaultHttpsProbeHosts);
            probes.AddRange(await Task.WhenAll(targets.Select(host => HttpsHeadAsync(host, snapshot, cancellationToken))));
            state = probes.Count(probe => probe.Ok) >= Math.Min(2, targets.Count) ? NetworkState.Healthy : NetworkState.InternetIssue;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new ConnectivityReport
        {
            SampleStartedAt = started, NetworkContext = snapshot.Context,
            IsLightweight = true, State = state, Ipv4 = snapshot.Ipv4, Gateway = snapshot.Gateway, Probes = probes,
            Summary = state == NetworkState.Healthy ? "Lightweight internet check succeeded." : "Lightweight internet check failed."
        };
    }

    public async Task<ConnectivityReport> RunFullCheckAsync(
        string? currentSsid,
        WifiAdapterDiagnostics? wifiDiagnostics = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probes = new List<ConnectivityProbe>();
        var started = DateTimeOffset.Now;
        if (wifiDiagnostics?.ProblemCode is > 0)
        {
            probes.Add(new ConnectivityProbe("无线设备状态", false, null, wifiDiagnostics.Detail));
            return Report(NetworkState.AdapterFault, currentSsid, null, null, probes, wifiDiagnostics.Detail);
        }
        var wifi = _environment.Capture(wifiDiagnostics?.AdapterName);
        var adapterName = wifiDiagnostics?.AdapterName ?? wifi.AdapterName;
        var adapterPresent = wifiDiagnostics?.AdapterPresent ?? wifi.AdapterPresent;

        probes.Add(new ConnectivityProbe("Wi-Fi adapter", adapterPresent, null, adapterName ?? "No Wi-Fi adapter detected"));
        if (!adapterPresent)
        {
            return Report(NetworkState.WifiDisabled, currentSsid, null, null, probes, "No Wi-Fi adapter detected.");
        }

        if (wifiDiagnostics?.HardwareRadioOn == false)
        {
            probes.Add(new ConnectivityProbe("Wi-Fi hardware radio", false, null, "Off"));
            return Report(NetworkState.WifiDisabled, currentSsid, null, null, probes, "Wi-Fi hardware radio is off.");
        }

        if (wifiDiagnostics?.SoftwareRadioOn == false)
        {
            probes.Add(new ConnectivityProbe("Wi-Fi software radio", false, null, "Off"));
            return Report(NetworkState.WifiDisabled, currentSsid, null, null, probes, "Wi-Fi software radio is off.");
        }

        var wifiUp = wifiDiagnostics?.AdapterUp ?? wifi.AdapterUp;
        probes.Add(new ConnectivityProbe("Wi-Fi status", wifiUp, null, wifiDiagnostics?.State ?? (wifi.AdapterUp ? "启用" : "禁用")));
        if (!wifiUp)
        {
            return Report(NetworkState.WifiDisabled, currentSsid, null, null, probes, "Wi-Fi adapter is disabled.");
        }

        probes.Add(new ConnectivityProbe("Wi-Fi connection", !string.IsNullOrWhiteSpace(currentSsid), null, currentSsid ?? "Wi-Fi is not connected"));
        if (string.IsNullOrWhiteSpace(currentSsid))
        {
            return Report(NetworkState.WifiDisconnected, currentSsid, null, null, probes, "Wi-Fi is not connected.");
        }

        var ipv4 = wifi.Ipv4;
        var gateway = wifi.Gateway;

        probes.Add(new ConnectivityProbe("IPv4", !string.IsNullOrWhiteSpace(ipv4), null, ipv4 ?? "No active IPv4 address"));
        probes.Add(new ConnectivityProbe("Default gateway", !string.IsNullOrWhiteSpace(gateway), null, gateway ?? "No default gateway"));

        if (string.IsNullOrWhiteSpace(ipv4))
        {
            return Report(NetworkState.Disconnected, currentSsid, ipv4, gateway, probes, "No active IPv4 address.");
        }

        if (!string.IsNullOrWhiteSpace(gateway))
        {
            var gatewayProbe = await PingAsync(gateway, "Gateway ping", cancellationToken);
            probes.Add(gatewayProbe);
            // 部分网关屏蔽 ICMP，仍须继续以 DNS 与公网 HTTPS 确认联网状态。
        }

        var dnsTargets = MonitorPolicy.NormalizeProbeList(_policy.DnsProbeHosts, MonitorPolicy.DefaultDnsProbeHosts);
        var dnsProbes = await Task.WhenAll(dnsTargets.Select(host => ResolveAsync(host, cancellationToken)));
        probes.AddRange(dnsProbes);
        var dnsSuccesses = dnsProbes.Count(probe => probe.Ok);

        if (dnsSuccesses == 0)
        {
            var portal = await TryPortalProbeAsync(wifi, cancellationToken);
            return Report(NetworkState.DnsIssue, currentSsid, ipv4, gateway, probes, "DNS resolution failed.", wifi.Context, started, portal, wifi.Ipv4InterfaceIndex);
        }

        var httpsTargets = MonitorPolicy.NormalizeProbeList(_policy.HttpsProbeHosts, MonitorPolicy.DefaultHttpsProbeHosts);
        var httpsProbes = await Task.WhenAll(httpsTargets.Select(host => HttpsHeadAsync(host, wifi, cancellationToken)));
        probes.AddRange(httpsProbes);
        var httpsSuccesses = httpsProbes.Count(probe => probe.Ok);

        if (httpsSuccesses >= Math.Min(2, httpsTargets.Count))
        {
            return Report(NetworkState.Healthy, currentSsid, ipv4, gateway, probes, "Network is healthy.", wifi.Context, started, wifiInterfaceIndex: wifi.Ipv4InterfaceIndex);
        }

        var internetPortal = await TryPortalProbeAsync(wifi, cancellationToken);
        return Report(NetworkState.InternetIssue, currentSsid, ipv4, gateway, probes, "Public HTTPS probes did not reach quorum.", wifi.Context, started, internetPortal, wifi.Ipv4InterfaceIndex);
    }

    private async Task<PortalDiscoveryResult> TryPortalProbeAsync(NetworkSnapshot wifi, CancellationToken cancellationToken)
    {
        if (!_requireWifiRoute || wifi.Ipv4InterfaceIndex is not > 0 || !IPAddress.TryParse(wifi.Ipv4, out var address))
            return PortalDiscoveryResult.Empty;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(2, _policy.ProbeTimeoutSeconds)));
        try
        {
            var result = await _probes.WifiPortalDiscoverAsync(address, wifi.Ipv4InterfaceIndex.Value, timeout.Token).WaitAsync(timeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PortalDiscoveryResult.Empty;
        }
    }

    private Task<ConnectivityProbe> PingAsync(string host, string name, CancellationToken cancellationToken) =>
        ProbeAsync(name, async token => (await _probes.PingAsync(host, TimeSpan.FromSeconds(_policy.ProbeTimeoutSeconds), token), host), cancellationToken);

    private Task<ConnectivityProbe> ResolveAsync(string host, CancellationToken cancellationToken) =>
        ProbeAsync($"DNS {host}", async token => { await _probes.ResolveAsync(host, token); return (true, "解析成功"); }, cancellationToken);

    private Task<ConnectivityProbe> HttpsHeadAsync(string host, NetworkSnapshot wifi, CancellationToken cancellationToken) =>
        ProbeAsync($"HTTPS {host}", async token =>
        {
            HttpStatusCode status;
            if (_requireWifiRoute)
            {
                if (wifi.Ipv4InterfaceIndex is not > 0 || !IPAddress.TryParse(wifi.Ipv4, out var address))
                    return (false, "无法确定用于验证的无线出口。");
                status = await _probes.WifiHttpsHeadAsync(host, address, wifi.Ipv4InterfaceIndex.Value, token);
            }
            else
            {
                status = await _probes.HttpsHeadAsync(host, token);
            }
            return ((int)status is >= 200 and < 300, $"HTTP {(int)status}");
        }, cancellationToken);

    private async Task<ConnectivityProbe> ProbeAsync(string name, Func<CancellationToken, Task<(bool Ok, string Detail)>> operation, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_policy.ProbeTimeoutSeconds));
            var result = await operation(timeout.Token).WaitAsync(timeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            return new ConnectivityProbe(name, result.Ok, stopwatch.Elapsed.TotalMilliseconds, result.Detail);
        }
        catch (Exception ex) when (ex is PingException or SocketException or HttpRequestException or OperationCanceledException)
        {
            // 调用方取消属于控制流；只有探针自身超时才记录为网络失败。
            cancellationToken.ThrowIfCancellationRequested();
            return new ConnectivityProbe(name, false, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    private static ConnectivityReport Report(
        NetworkState state,
        string? currentSsid,
        string? ipv4,
        string? gateway,
        List<ConnectivityProbe> probes,
        string summary,
        string? context = null,
        DateTimeOffset? started = null,
        PortalDiscoveryResult? portal = null,
        int? wifiInterfaceIndex = null)
    {
        return new ConnectivityReport
        {
            State = state,
            PortalEvidence = portal?.Evidence ?? PortalEvidence.None,
            PortalSanitizedOrigin = portal?.SanitizedEntry,
            PortalEntryUri = portal?.EntryUri,
            WifiInterfaceIndex = wifiInterfaceIndex,
            SampleStartedAt = started,
            NetworkContext = context,
            CurrentSsid = currentSsid,
            Ipv4 = ipv4,
            Gateway = gateway,
            Probes = probes,
            Summary = summary
        };
    }

}

internal sealed class SystemConnectivityEnvironment : IConnectivityEnvironment
{
    public NetworkSnapshot Capture() => Capture(null);

    public NetworkSnapshot Capture(string? adapterName)
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).ToArray();
        var active = adapters.Where(item => item.OperationalStatus == OperationalStatus.Up).ToArray();
        // 轻量检查没有 SSID 对应的接口名称，多个活动接口时交给完整检查确认。
        var wifi = string.IsNullOrWhiteSpace(adapterName)
            ? active.Length == 1 ? active[0] : null
            : adapters.FirstOrDefault(item => string.Equals(item.Name, adapterName, StringComparison.OrdinalIgnoreCase));
        var hasIpv4 = wifi?.Supports(NetworkInterfaceComponent.IPv4) == true;
        return new NetworkSnapshot(wifi?.Name, adapters.Length > 0, wifi?.OperationalStatus == OperationalStatus.Up,
            GetPrimaryIpv4(wifi), GetDefaultGateway(wifi), hasIpv4 ? wifi!.GetIPProperties().GetIPv4Properties()?.Index : null,
            wifi?.Id, wifi is null ? null : string.Join(",", wifi.GetIPProperties().DnsAddresses));
    }

    private static string? GetPrimaryIpv4(NetworkInterface? networkInterface)
    {
        if (networkInterface is null)
        {
            return null;
        }

        var properties = networkInterface.GetIPProperties();
        var address = properties.UnicastAddresses
            .FirstOrDefault(item => item.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
        return address is not null && !IPAddress.IsLoopback(address) ? address.ToString() : null;
    }

    private static string? GetDefaultGateway(NetworkInterface? networkInterface)
    {
        if (networkInterface is null)
        {
            return null;
        }

        var gateway = networkInterface.GetIPProperties().GatewayAddresses
            .Select(item => item.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any));
        return gateway?.ToString();
    }

}


internal sealed class SystemConnectivityProbes : IConnectivityProbes
{
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AllowAutoRedirect = false
    }) { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<bool> PingAsync(string host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(host, (int)timeout.TotalMilliseconds).WaitAsync(cancellationToken);
        return reply.Status == IPStatus.Success;
    }

    public async Task ResolveAsync(string host, CancellationToken cancellationToken) =>
        _ = await Dns.GetHostAddressesAsync(host, cancellationToken);

    public Task<HttpStatusCode> HttpsHeadAsync(string host, CancellationToken cancellationToken) =>
        SendHeadAsync(SharedHttpClient, host, cancellationToken);

    public async Task<HttpStatusCode> WifiHttpsHeadAsync(string host, IPAddress address, int interfaceIndex, CancellationToken cancellationToken)
    {
        using var client = WifiSocketConnector.CreateHttpClient(address, interfaceIndex);
        return await SendHeadAsync(client, host, cancellationToken);
    }

    public async Task<PortalEvidence> WifiPortalAsync(IPAddress address, int interfaceIndex, CancellationToken cancellationToken)
    {
        using var client = WifiSocketConnector.CreateHttpClient(address, interfaceIndex);
        return await PortalProbe.InspectAsync(client, cancellationToken);
    }

    public async Task<PortalDiscoveryResult> WifiPortalDiscoverAsync(IPAddress address, int interfaceIndex, CancellationToken cancellationToken)
    {
        using var client = WifiSocketConnector.CreateHttpClient(address, interfaceIndex);
        return await PortalProbe.DiscoverAsync(client, cancellationToken);
    }

    private static async Task<HttpStatusCode> SendHeadAsync(HttpClient client, string host, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, new UriBuilder(Uri.UriSchemeHttps, host).Uri);
        using var response = await client.SendAsync(request, cancellationToken);
        return response.StatusCode;
    }
}
