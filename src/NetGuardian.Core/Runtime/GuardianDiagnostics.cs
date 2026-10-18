using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Settings;
using NetGuardian.Core.Wifi;

namespace NetGuardian.Core.Runtime;

public sealed record DiagnosticProfile(string Ssid, bool Enabled, bool AutoConnect, bool Available);
public sealed record GuardianDiagnosticReport(bool InventorySuccess, string? InventoryError,
    IReadOnlyList<DiagnosticProfile> Profiles, WifiAdapterDiagnostics Adapter, ConnectivityReport Connectivity)
{
    public bool ReadyForUnattendedRecovery => InventorySuccess && Connectivity.Ok
        && Profiles.Count(profile => profile.Enabled && profile.AutoConnect && profile.Available) >= 2;
}

public static class GuardianDiagnostics
{
    /// <summary>在实际运行身份下只读检查，不执行启用、重连、切换或凭据导出。</summary>
    public static async Task<GuardianDiagnosticReport> RunAsync(AppSettings settings, IWifiClient wifi,
        IConnectivityTester tester, CancellationToken cancellationToken = default)
    {
        var inventory = await wifi.GetProfileInventoryAsync(cancellationToken);
        WifiProfileAvailability.Apply(settings.Profiles, inventory);
        var ssid = await wifi.GetCurrentSsidAsync(cancellationToken);
        var adapter = await wifi.GetAdapterDiagnosticsAsync(cancellationToken);
        var report = await tester.RunFullCheckAsync(settings.Policy, ssid, adapter, cancellationToken);
        return new GuardianDiagnosticReport(inventory.Success, inventory.Error,
            settings.Profiles.Select(profile => new DiagnosticProfile(profile.Ssid, profile.Enabled, profile.AutoConnect, profile.CredentialSaved)).ToArray(),
            adapter, report);
    }
}
