using NetGuardian.Core.Models;

namespace NetGuardian.Core.Wifi;

public interface IWifiClient
{
    string? LastConnectionFailure => null;
    Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken = default);
    Task<string?> GetCurrentSsidAsync(CancellationToken cancellationToken = default);
    async Task<WifiConnectionInfo> GetConnectionInfoAsync(CancellationToken cancellationToken = default)
    {
        var ssid = await GetCurrentSsidAsync(cancellationToken);
        return new(true, !string.IsNullOrWhiteSpace(ssid), ssid);
    }
    Task<WifiAdapterDiagnostics> GetAdapterDiagnosticsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default);
    Task<SavedWifiProfileInventory> GetSavedProfilesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new SavedWifiProfileInventory(false, [], "当前无线客户端不支持读取已保存网络。"));
    async Task<WifiProfileInventory> GetProfileInventoryAsync(CancellationToken cancellationToken = default) =>
        new(true, await GetProfileNamesAsync(cancellationToken));
    Task<bool> TryEnableWifiAdapterAsync(string? adapterName, CancellationToken cancellationToken = default);
    Task<string?> GetSavedPasswordAsync(string ssid, CancellationToken cancellationToken = default);
    Task<bool> SaveProfileAsync(string ssid, string? password, string authentication, CancellationToken cancellationToken = default);
    Task<bool> SaveProfileAsync(string profileName, string ssid, string? password, string authentication, CancellationToken cancellationToken = default);
    Task<bool> ConnectAsync(string ssid, CancellationToken cancellationToken = default);
    Task<bool> ConnectProfileAsync(string profileName, string ssid, CancellationToken cancellationToken = default);
    Task<bool> DeleteProfileAsync(string profileName, CancellationToken cancellationToken = default);
    Task<bool> DisconnectAsync(CancellationToken cancellationToken = default);
}
