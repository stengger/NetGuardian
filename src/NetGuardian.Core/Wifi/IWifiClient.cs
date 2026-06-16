using NetGuardian.Core.Models;

namespace NetGuardian.Core.Wifi;

public interface IWifiClient
{
    Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken = default);
    Task<string?> GetCurrentSsidAsync(CancellationToken cancellationToken = default);
    Task<bool> ConnectAsync(string ssid, CancellationToken cancellationToken = default);
    Task<bool> DisconnectAsync(CancellationToken cancellationToken = default);
}

