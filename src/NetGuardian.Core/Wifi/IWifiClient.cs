using NetGuardian.Core.Models;

namespace NetGuardian.Core.Wifi;

public interface IWifiClient
{
    Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken = default);
    Task<string?> GetCurrentSsidAsync(CancellationToken cancellationToken = default);
    Task<WifiAdapterDiagnostics> GetAdapterDiagnosticsAsync(CancellationToken cancellationToken = default);
    Task<bool> TryEnableWifiAdapterAsync(string? adapterName, CancellationToken cancellationToken = default);
    Task<string?> GetSavedPasswordAsync(string ssid, CancellationToken cancellationToken = default);
    Task<bool> SaveProfileAsync(string ssid, string? password, string authentication, CancellationToken cancellationToken = default);
    Task<bool> SaveProfileAsync(string profileName, string ssid, string? password, string authentication, CancellationToken cancellationToken = default);
    Task<bool> ConnectAsync(string ssid, CancellationToken cancellationToken = default);
    Task<bool> ConnectProfileAsync(string profileName, string ssid, CancellationToken cancellationToken = default);
    Task<bool> DeleteProfileAsync(string profileName, CancellationToken cancellationToken = default);
    Task<bool> DisconnectAsync(CancellationToken cancellationToken = default);
}
