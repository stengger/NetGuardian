using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;

namespace NetGuardian.Core.Wifi;

public sealed record ManualWifiConnectionResult(bool Connected, ConnectivityReport? Report = null, string? Error = null);

/// <summary>手动连接必须等待目标关联与联网检查，不把连接请求接受当成成功。</summary>
public sealed class ManualWifiConnector(IWifiClient wifi, IConnectivityTester tester)
{
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(25);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    public async Task<ManualWifiConnectionResult> ConnectAsync(WifiProfile profile, MonitorPolicy policy,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ConnectionTimeout);
        var token = deadline.Token;
        var associated = false;
        ConnectivityReport? lastReport = null;
        try
        {
            var current = await wifi.GetCurrentSsidAsync(token);
            token.ThrowIfCancellationRequested();
            if (current != profile.Ssid)
            {
                var accepted = profile.ConnectionProfileName == profile.Ssid
                    ? await wifi.ConnectAsync(profile.Ssid, token)
                    : await wifi.ConnectProfileAsync(profile.ConnectionProfileName, profile.Ssid, token);
                token.ThrowIfCancellationRequested();
                if (!accepted) return new(false, Error: wifi.LastConnectionFailure);
            }
            while (true)
            {
                token.ThrowIfCancellationRequested();
                associated = await wifi.GetCurrentSsidAsync(token) == profile.Ssid;
                if (associated)
                {
                    var diagnostics = await wifi.GetAdapterDiagnosticsAsync(token);
                    lastReport = await tester.RunFullCheckAsync(policy, profile.Ssid, diagnostics, token);
                    token.ThrowIfCancellationRequested();
                    associated = await wifi.GetCurrentSsidAsync(token) == profile.Ssid;
                    token.ThrowIfCancellationRequested();
                    if (!associated) return new(false, Error: "连接期间目标网络已变化。");
                    if (lastReport.State is not (NetworkState.Disconnected or NetworkState.WifiDisconnected or NetworkState.WifiDisabled))
                        return new(true, lastReport);
                }
                await Task.Delay(PollInterval, token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, lastReport);
        }
    }
}
