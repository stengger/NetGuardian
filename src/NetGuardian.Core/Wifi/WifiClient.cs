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
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Fall back to netsh; the UI reports the final result rather than implementation details.
        }

        return await _netsh.ScanAsync(cancellationToken);
    }

    public Task<string?> GetCurrentSsidAsync(CancellationToken cancellationToken = default)
    {
        return _netsh.GetCurrentSsidAsync(cancellationToken);
    }

    public Task<WifiAdapterDiagnostics> GetAdapterDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        return _netsh.GetAdapterDiagnosticsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default)
    {
        return _netsh.GetProfileNamesAsync(cancellationToken);
    }

    public Task<bool> TryEnableWifiAdapterAsync(string? adapterName, CancellationToken cancellationToken = default)
    {
        return _netsh.TryEnableWifiAdapterAsync(adapterName, cancellationToken);
    }

    public Task<string?> GetSavedPasswordAsync(string ssid, CancellationToken cancellationToken = default)
    {
        return _netsh.GetSavedPasswordAsync(ssid, cancellationToken);
    }

    public Task<bool> SaveProfileAsync(string ssid, string? password, string authentication, CancellationToken cancellationToken = default)
    {
        return SaveProfileAsync(ssid, ssid, password, authentication, cancellationToken);
    }

    public Task<bool> SaveProfileAsync(string profileName, string ssid, string? password, string authentication, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _nativeScanner.SaveProfile(profileName, ssid, password, authentication), cancellationToken);
    }

    public Task<bool> ConnectAsync(string ssid, CancellationToken cancellationToken = default)
    {
        return _netsh.ConnectAsync(ssid, cancellationToken);
    }

    public Task<bool> ConnectProfileAsync(string profileName, string ssid, CancellationToken cancellationToken = default)
    {
        return _netsh.ConnectProfileAsync(profileName, ssid, cancellationToken);
    }

    public Task<bool> DeleteProfileAsync(string profileName, CancellationToken cancellationToken = default)
    {
        return _netsh.DeleteProfileAsync(profileName, cancellationToken);
    }

    public Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return _netsh.DisconnectAsync(cancellationToken);
    }
}
