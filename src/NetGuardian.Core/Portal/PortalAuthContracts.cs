using System.Net;
using NetGuardian.Core.Models;
using NetGuardian.Core.Portal;

namespace NetGuardian.Core.Portal;

public enum PortalAuthOutcome
{
    Submitted,
    AlreadyOnline,
    EntryNotFound,
    EntryOriginMismatch,
    SubmitTargetMismatch,
    ControlNotFound,
    ControlAmbiguous,
    CredentialMissing,
    PayloadTransformed,
    CredentialsRejected,
    AccountExpired,
    BrowserUnavailable,
    TimedOut,
    Cancelled,
    Failed
}

public sealed record PortalAuthRequest(
    string Ssid,
    PortalAuthConfig Config,
    Uri? DiscoveredEntryUri,
    IPAddress? WifiAddress,
    int? WifiInterfaceIndex);

public sealed record PortalAuthResult(
    PortalAuthOutcome Outcome,
    string Detail,
    string? SanitizedOrigin = null,
    bool ShouldPause = false);

public sealed record PortalSelectOption(string Value, string Text);

public sealed record PortalDiscoveredControl(
    string Selector,
    string Label,
    string Kind,
    IReadOnlyList<PortalSelectOption>? Options = null);

public sealed record PortalInspectionResult(
    bool Success,
    string Detail,
    string? Origin,
    string? LoginPath,
    string? SubmitOrigin,
    string? SubmitPath,
    bool IsHttps,
    IReadOnlyList<PortalDiscoveredControl> UsernameCandidates,
    IReadOnlyList<PortalDiscoveredControl> PasswordCandidates,
    IReadOnlyList<PortalDiscoveredControl> SelectCandidates,
    IReadOnlyList<PortalDiscoveredControl> SubmitCandidates);

public interface IPortalAuthenticator
{
    Task<PortalAuthResult> AuthenticateAsync(
        PortalAuthRequest request,
        PortalCredential? credential,
        CancellationToken cancellationToken);

    Task<PortalInspectionResult> InspectEntryAsync(
        Uri entryUri,
        IPAddress? wifiAddress,
        int? interfaceIndex,
        bool allowInsecureHttp,
        CancellationToken cancellationToken);
}
