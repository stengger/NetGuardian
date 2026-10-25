using NetGuardian.Core.Models;
using NetGuardian.Core.Wifi;

namespace NetGuardian.Core.Connectivity;

/// <summary>
/// Abstraction over the full connectivity health check so callers (and tests)
/// can substitute the probing implementation.
/// </summary>
public interface IConnectivityTester
{
    // 保持第三方实现兼容；未实现轻量探测的检测器继续执行完整检查。
    Task<ConnectivityReport> RunLightweightCheckAsync(MonitorPolicy policy, CancellationToken cancellationToken = default)
        => RunFullCheckAsync(policy, null, cancellationToken: cancellationToken);
    Task<ConnectivityReport> RunFullCheckAsync(
        MonitorPolicy policy,
        string? currentSsid,
        WifiAdapterDiagnostics? wifiDiagnostics = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation that delegates to <see cref="ConnectivityTester"/>,
/// creating one instance per check (mirrors the historical behaviour).
/// </summary>
public sealed class ConnectivityTesterService : IConnectivityTester
{
    public Task<ConnectivityReport> RunLightweightCheckAsync(MonitorPolicy policy, CancellationToken cancellationToken = default)
        => new ConnectivityTester(policy).RunLightweightCheckAsync(cancellationToken);
    public Task<ConnectivityReport> RunFullCheckAsync(
        MonitorPolicy policy,
        string? currentSsid,
        WifiAdapterDiagnostics? wifiDiagnostics = null,
        CancellationToken cancellationToken = default)
    {
        return new ConnectivityTester(policy).RunFullCheckAsync(currentSsid, wifiDiagnostics, cancellationToken);
    }
}
