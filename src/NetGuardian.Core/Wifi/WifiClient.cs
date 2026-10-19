using NetGuardian.Core.Models;

namespace NetGuardian.Core.Wifi;

public sealed class WifiClient : IWifiClient
{
    private readonly NativeWifiScanner _nativeScanner = new();
    private readonly INativeWifiConnector _nativeConnector;
    private readonly IWifiClient _netsh;
    private readonly WifiDeviceInspector _deviceInspector;
    public string? LastConnectionFailure { get; private set; }

    public WifiClient(INativeWifiConnector? nativeConnector = null, IWifiClient? fallback = null,
        WifiDeviceInspector? deviceInspector = null)
    {
        _nativeConnector = nativeConnector ?? _nativeScanner;
        _netsh = fallback ?? new NetshWifiClient();
        _deviceInspector = deviceInspector ?? new WifiDeviceInspector(new NetGuardian.Core.SystemCommands.ProcessCommandRunner());
    }

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

    public async Task<WifiAdapterDiagnostics> GetAdapterDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _netsh.GetAdapterDiagnosticsAsync(cancellationToken);
        if (result.AdapterPresent && result.AdapterUp) return result;
        var fault = await _deviceInspector.FindFaultAsync(cancellationToken);
        return fault is null ? result : result with
        {
            // PnP仍报告物理设备存在；禁用后没有WLAN接口不等于设备消失。
            AdapterPresent = true,
            AdapterName = fault.InstanceId,
            DeviceInstanceId = fault.InstanceId,
            ProblemCode = fault.ProblemCode,
            Detail = $"无线设备启动异常，设备管理器代码 {fault.ProblemCode}：{fault.Name}"
        };
    }

    public Task<IReadOnlyList<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default)
    {
        return _netsh.GetProfileNamesAsync(cancellationToken);
    }

    public Task<WifiProfileInventory> GetProfileInventoryAsync(CancellationToken cancellationToken = default) =>
        _netsh.GetProfileInventoryAsync(cancellationToken);

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
        return ConnectProfileAsync(ssid, ssid, cancellationToken);
    }

    public async Task<bool> ConnectProfileAsync(string profileName, string ssid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastConnectionFailure = null;
        string nativeFailure;
        try
        {
            var accepted = await Task.Run(() => _nativeConnector.TryConnectProfile(profileName, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (accepted) return true;
            nativeFailure = $"原生 WLAN 请求未接受，错误码：{_nativeConnector.LastNativeError?.ToString() ?? "未提供"}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            nativeFailure = $"原生 WLAN 请求异常：{ex.Message}";
        }
        cancellationToken.ThrowIfCancellationRequested();
        var connected = await _netsh.ConnectProfileAsync(profileName, ssid, cancellationToken);
        if (!connected) LastConnectionFailure = $"{nativeFailure}；{_netsh.LastConnectionFailure ?? "回退连接请求未接受。"}";
        return connected;
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
