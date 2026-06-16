using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetGuardian.Core.Models;

namespace NetGuardian.Core.Connectivity;

public sealed class ConnectivityTester
{
    private static readonly string[] DnsTargets = ["www.microsoft.com", "www.baidu.com"];
    private static readonly string[] HttpsTargets = ["www.microsoft.com", "www.baidu.com", "www.cloudflare.com"];
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private readonly MonitorPolicy _policy;

    public ConnectivityTester(MonitorPolicy policy)
    {
        _policy = policy;
    }

    public async Task<ConnectivityReport> RunFullCheckAsync(string? currentSsid, CancellationToken cancellationToken = default)
    {
        var probes = new List<ConnectivityProbe>();
        var ipv4 = GetPrimaryIpv4();
        var gateway = GetDefaultGateway();

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
            if (!gatewayProbe.Ok)
            {
                return Report(NetworkState.GatewayIssue, currentSsid, ipv4, gateway, probes, "Gateway is not reachable.");
            }
        }

        var dnsSuccesses = 0;
        foreach (var host in DnsTargets)
        {
            var probe = await ResolveAsync(host, cancellationToken);
            probes.Add(probe);
            if (probe.Ok)
            {
                dnsSuccesses++;
            }
        }

        if (dnsSuccesses == 0)
        {
            return Report(NetworkState.DnsIssue, currentSsid, ipv4, gateway, probes, "DNS resolution failed.");
        }

        var httpsSuccesses = 0;
        foreach (var host in HttpsTargets)
        {
            var probe = await HttpsHeadAsync(host, cancellationToken);
            probes.Add(probe);
            if (probe.Ok)
            {
                httpsSuccesses++;
            }
        }

        return httpsSuccesses >= 2
            ? Report(NetworkState.Healthy, currentSsid, ipv4, gateway, probes, "Network is healthy.")
            : Report(NetworkState.InternetIssue, currentSsid, ipv4, gateway, probes, "Public HTTPS probes did not reach quorum.");
    }

    private static ConnectivityReport Report(
        NetworkState state,
        string? currentSsid,
        string? ipv4,
        string? gateway,
        List<ConnectivityProbe> probes,
        string summary)
    {
        return new ConnectivityReport
        {
            State = state,
            CurrentSsid = currentSsid,
            Ipv4 = ipv4,
            Gateway = gateway,
            Probes = probes,
            Summary = summary
        };
    }

    private static string? GetPrimaryIpv4()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var properties = networkInterface.GetIPProperties();
            var address = properties.UnicastAddresses
                .FirstOrDefault(item => item.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            if (address is not null && !IPAddress.IsLoopback(address))
            {
                return address.ToString();
            }
        }

        return null;
    }

    private static string? GetDefaultGateway()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var gateway = networkInterface.GetIPProperties().GatewayAddresses
                .Select(item => item.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any));
            if (gateway is not null)
            {
                return gateway.ToString();
            }
        }

        return null;
    }

    private async Task<ConnectivityProbe> PingAsync(string host, string name, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, (int)(_policy.ProbeTimeoutSeconds * 1000));
            stopwatch.Stop();
            return new ConnectivityProbe(name, reply.Status == IPStatus.Success, stopwatch.Elapsed.TotalMilliseconds, host);
        }
        catch (Exception ex) when (ex is PingException or SocketException or OperationCanceledException)
        {
            stopwatch.Stop();
            return new ConnectivityProbe(name, false, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    private async Task<ConnectivityProbe> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_policy.ProbeTimeoutSeconds));
            await Dns.GetHostAddressesAsync(host, cts.Token);
            stopwatch.Stop();
            return new ConnectivityProbe($"DNS {host}", true, stopwatch.Elapsed.TotalMilliseconds, "Resolved");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            stopwatch.Stop();
            return new ConnectivityProbe($"DNS {host}", false, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    private async Task<ConnectivityProbe> HttpsHeadAsync(string host, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_policy.ProbeTimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Head, $"https://{host}/");
            using var response = await SharedHttpClient.SendAsync(request, cts.Token);
            stopwatch.Stop();
            var ok = (int)response.StatusCode is >= 200 and < 500;
            return new ConnectivityProbe($"HTTPS {host}", ok, stopwatch.Elapsed.TotalMilliseconds, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            return new ConnectivityProbe($"HTTPS {host}", false, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
        }
    }
}
