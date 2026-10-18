using NetGuardian.Core.Models;
using NetGuardian.Core.Wifi;

namespace NetGuardian.Core.Connectivity;

/// <summary>
/// Abstraction over the full connectivity health check so callers (and tests)
/// can substitute the probing implementation.
/// </summary>
public interface IConnectivityTester
{
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
    public Task<ConnectivityReport> RunFullCheckAsync(
        MonitorPolicy policy,
        string? currentSsid,
        WifiAdapterDiagnostics? wifiDiagnostics = null,
        CancellationToken cancellationToken = default)
    {
        return new ConnectivityTester(policy).RunFullCheckAsync(currentSsid, wifiDiagnostics, cancellationToken);
    }
}
