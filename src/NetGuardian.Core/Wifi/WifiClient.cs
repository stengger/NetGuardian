using NetGuardian.Core.Models;

namespace NetGuardian.Core.Wifi;

public sealed class WifiClient : IWifiClient
{
    private readonly NativeWifiScanner _nativeScanner = new();
    private readonly NetshWifiClient _netsh = new();

    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var networks = await _nativeScanner.ScanAsync(cancellationToken);
            if (networks.Count > 0)
            {
                return networks;
            }
        }
        catch
        {
            // Fall back to netsh; the UI reports the final result rather than implementation details.
        }

        return await _netsh.ScanAsync(cancellationToken);
    }

    public Task<string?> GetCurrentSsidAsync(CancellationToken cancellationToken = default)
    {
        return _netsh.GetCurrentSsidAsync(cancellationToken);
    }

    public Task<bool> SaveProfileAsync(string ssid, string? password, string authentication, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _nativeScanner.SaveProfile(ssid, password, authentication), cancellationToken);
    }

    public Task<bool> ConnectAsync(string ssid, CancellationToken cancellationToken = default)
    {
        return _netsh.ConnectAsync(ssid, cancellationToken);
    }

    public Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return _netsh.DisconnectAsync(cancellationToken);
    }
}
