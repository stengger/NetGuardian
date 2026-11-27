using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Wifi;

namespace NetGuardian.Core.Monitoring;

public sealed partial class MonitoringEngine
{
    private DateTimeOffset? _lastPrimaryFailbackAttemptAt;
    private readonly Func<MonitorPolicy, CancellationToken, Task<(bool Healthy, string? AdapterName)>>? _wiredInternetProbe;

    private Task<(bool Healthy, string? AdapterName)> CheckWiredInternetHealthyAsync(
        MonitorPolicy policy,
        CancellationToken cancellationToken)
    {
        if (!policy.PreferEthernetWhenHealthy)
        {
            return Task.FromResult((false, (string?)null));
        }

        if (_wiredInternetProbe is not null)
        {
            return _wiredInternetProbe(policy, cancellationToken);
        }

        return ProbeActiveEthernetInternetAsync(policy, cancellationToken);
    }

    private static async Task<(bool Healthy, string? AdapterName)> ProbeActiveEthernetInternetAsync(
        MonitorPolicy policy,
        CancellationToken cancellationToken)
    {
        try
        {
            var host = policy.HttpsProbeHosts.FirstOrDefault() ?? MonitorPolicy.DefaultHttpsProbeHosts[0];
            var uri = new Uri($"https://{host}/");
            var timeout = TimeSpan.FromSeconds(Math.Clamp(policy.ProbeTimeoutSeconds, 1.0, 10.0));

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (nic.NetworkInterfaceType is not NetworkInterfaceType.Ethernet
                    and not NetworkInterfaceType.GigabitEthernet
                    and not NetworkInterfaceType.FastEthernetT
                    and not NetworkInterfaceType.FastEthernetFx)
                {
                    continue;
                }

                var props = nic.GetIPProperties();
                if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(g.Address)))
                {
                    continue;
                }

                var ipv4Props = props.GetIPv4Properties();
                var unicast = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4Props is null || unicast is null)
                {
                    continue;
                }

                using var client = WifiSocketConnector.CreateHttpClient(unicast.Address, ipv4Props.Index, allowAutoRedirect: false);
                client.Timeout = timeout;
                using var request = new HttpRequestMessage(HttpMethod.Head, uri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode is >= 200 and < 400)
                {
                    return (true, nic.Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 有线探测异常按非健康处理，继续正常 Wi-Fi 流程。
        }

        return (false, null);
    }

    private async Task TryPrimaryWifiFailbackIfDueAsync(
        MonitorPolicy policy,
        string? currentSsid,
        CancellationToken cancellationToken)
    {
        if (!policy.EnablePrimaryFailback || !IsMonitoring || string.IsNullOrWhiteSpace(currentSsid))
        {
            return;
        }

        var now = _time.GetLocalNow();
        var interval = TimeSpan.FromMinutes(Math.Clamp(policy.PrimaryFailbackIntervalMinutes, 5, 24 * 60));
        if (_lastPrimaryFailbackAttemptAt is { } lastAttempt && now - lastAttempt < interval)
        {
            return;
        }

        var profiles = _profilesProvider().ToArray();
        var primary = RecoveryPlanner.SelectPrimaryProfileForFailback(profiles, currentSsid);
        if (primary is null)
        {
            return;
        }

        // 先扫描确认目标主 Wi-Fi 在当前环境中可见，再消耗回切窗口避免盲切掉线。
        var scan = await ScanRecoveryWifiAsync(cancellationToken);
        var visible = scan.Networks.Where(n => n.IsConnectable).Select(n => n.Ssid).ToHashSet(StringComparer.Ordinal);
        if (scan.Status != WifiScanStatus.Completed || !visible.Contains(primary.Ssid))
        {
            return;
        }

        _lastPrimaryFailbackAttemptAt = now;
        Raise(EngineEventKind.PrimaryFailbackStarted, primary.Ssid);

        var budget = WifiValidationTimeout > TimeSpan.Zero ? WifiValidationTimeout : TimeSpan.FromSeconds(20);
        var switched = await TryRecoveryStepAsync(
            () => ConnectAndValidateAsync(policy, primary.Ssid, budget, cancellationToken),
            cancellationToken);

        if (switched)
        {
            RememberVerifiedWifi(primary.Ssid);
            Raise(EngineEventKind.PrimaryFailbackSucceeded, primary.Ssid);
            return;
        }

        // 回切主 Wi-Fi 未能通过公网校验，立即回滚到原先可用的备用 Wi-Fi。
        await TryRecoveryStepAsync(
            () => ConnectAndValidateAsync(policy, currentSsid, budget, cancellationToken),
            cancellationToken);
        Raise(EngineEventKind.PrimaryFailbackRolledBack, primary.Ssid, currentSsid);
    }
}
