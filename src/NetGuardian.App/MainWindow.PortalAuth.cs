using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Portal;
using NetGuardian.Core.Runtime;

namespace NetGuardian.App;

public partial class MainWindow
{
    private async void ConfigurePortalAuth_Click(object sender, RoutedEventArgs e)
    {
        var profile = ProfileGrid.SelectedItem as WifiProfile;
        if (profile is null && NearbyGrid.SelectedItem is NearbyWifiRow nearby)
            profile = _profiles.FirstOrDefault(item => SsidEquals(item.Ssid, nearby.Ssid));

        if (profile is null)
        {
            SetWifiSelectionMessage(_localizer.T("SelectProfileFirst"));
            return;
        }

        await RunUiActionAsync(() =>
        {
            ConfigureProfilePortalAuth(profile);
            return Task.CompletedTask;
        });
    }

    private void ConfigureProfilePortalAuth(WifiProfile profile)
    {
        var existing = profile.PortalAuth;
        string? existingAccount = null;
        PortalCredential? loadedCred = null;
        var hasCredential = existing?.CredentialId is { } credId && _portalCredentials.TryLoad(credId, out loadedCred) && loadedCred is not null;
        if (hasCredential) existingAccount = loadedCred!.Username;

        var dialog = new PortalAuthDialog(
            _localizer,
            profile.Ssid,
            existing,
            existingAccount,
            hasCredential,
            ct => DetectPortalForProfileAsync(profile.Ssid, existing?.AllowInsecureHttp == true, ct),
            (draft, cred, discoveredEntry, ct) => TestPortalForProfileAsync(profile, draft, cred, discoveredEntry, ct))
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;

        if (dialog.ClearRequested)
        {
            if (existing?.CredentialId is { } oldId) _portalCredentials.Delete(oldId);
            profile.PortalAuth = null;
            _engine.ClearPortalAuthPause(profile.Ssid);
            SaveSettings();
            ProfileGrid.Items.Refresh();
            UpdateWifiSelectionUi();
            Log($"{DisplaySsid(profile.Ssid)}：{_localizer.T("PortalStatusUnconfigured")}");
            return;
        }

        if (dialog.ResultConfig is null) return;

        if (dialog.ResultCredential is not null && !string.IsNullOrWhiteSpace(dialog.ResultConfig.CredentialId))
        {
            _portalCredentials.Save(dialog.ResultConfig.CredentialId, dialog.ResultCredential);
        }
        else if (existing?.Mode == PortalAuthMode.CredentialsForm
            && dialog.ResultConfig.Mode == PortalAuthMode.OneClickConsent
            && existing.CredentialId is { } staleId)
        {
            _portalCredentials.Delete(staleId);
        }

        profile.PortalAuth = dialog.ResultConfig;
        _engine.ClearPortalAuthPause(profile.Ssid);
        SaveSettings();
        ProfileGrid.Items.Refresh();
        UpdateWifiSelectionUi();

        var summary = profile.PortalAuth.CanAutoAuthenticate
            ? string.Format(_localizer.T("PortalStatusReady"), profile.PortalAuth.AllowedOrigin)
            : string.Format(_localizer.T("PortalStatusPending"), profile.PortalAuth.AllowedOrigin);
        Log($"{DisplaySsid(profile.Ssid)}：{summary}");
        if (profile.PortalAuth.CanAutoAuthenticate)
            ShowFeedback(_localizer.T("PortalAuthTitle"), _localizer.T("PortalTestPassed"), FeedbackSeverity.Success);
    }

    private async Task<PortalDetectionFeedback> DetectPortalForProfileAsync(string ssid, bool allowInsecureHttp, CancellationToken token)
    {
        var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(token);
        var connection = await _wifi.GetConnectionInfoAsync(token);
        if (!connection.ReadSucceeded || !connection.Connected || !SsidEquals(connection.Ssid, ssid))
            return new PortalDetectionFeedback(false, string.Format(_localizer.T("ManualWifiFailed"), DisplaySsid(ssid)));

        var report = await _connectivityTester.RunFullCheckAsync(_settings.Policy, ssid, diagnostics, token);
        if (report.Ok || report.PortalEntryUri is null)
            return new PortalDetectionFeedback(false, _localizer.T("PortalDetectNotNeeded"));

        IPAddress.TryParse(report.Ipv4, out var wifiAddress);
        var allowHttp = allowInsecureHttp || report.PortalEntryUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase);
        var inspection = await _portalAuthenticator.InspectEntryAsync(report.PortalEntryUri, wifiAddress, report.WifiInterfaceIndex, allowHttp, token);
        return new PortalDetectionFeedback(inspection.Success, inspection.Detail, report.PortalEntryUri, inspection);
    }

    private async Task<PortalTestFeedback> TestPortalForProfileAsync(
        WifiProfile profile,
        PortalAuthConfig draft,
        PortalCredential? newCredential,
        Uri? knownEntryUri,
        CancellationToken token)
    {
        PortalTestFeedback? outcome = null;
        await RunExclusiveNetworkActionAsync(async opToken =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, opToken);
            var ct = linked.Token;

            PortalCredential? effectiveCredential = newCredential;
            if (draft.Mode == PortalAuthMode.CredentialsForm && effectiveCredential is null)
            {
                if (string.IsNullOrWhiteSpace(draft.CredentialId) || !_portalCredentials.TryLoad(draft.CredentialId, out effectiveCredential) || effectiveCredential is null)
                {
                    outcome = new PortalTestFeedback(false, _localizer.T("PortalValidationFieldsRequired"));
                    return false;
                }
            }

            var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(ct);
            var connection = await _wifi.GetConnectionInfoAsync(ct);
            if (!connection.ReadSucceeded || !connection.Connected || !SsidEquals(connection.Ssid, profile.Ssid))
            {
                outcome = new PortalTestFeedback(false, string.Format(_localizer.T("ManualWifiFailed"), DisplaySsid(profile.Ssid)));
                return false;
            }

            var before = await _connectivityTester.RunFullCheckAsync(_settings.Policy, profile.Ssid, diagnostics, ct);
            if (before.Ok)
            {
                draft.LastAuthenticatedAt = DateTimeOffset.Now;
                outcome = new PortalTestFeedback(true, _localizer.T("PortalTestPassed"), draft);
                return true;
            }

            var entryUri = before.PortalEntryUri ?? knownEntryUri;
            if (entryUri is null)
            {
                outcome = new PortalTestFeedback(false, _localizer.T("PortalDetectNotNeeded"));
                return false;
            }

            IPAddress.TryParse(before.Ipv4, out var wifiAddress);
            var authResult = await _portalAuthenticator.AuthenticateAsync(
                new PortalAuthRequest(profile.Ssid, draft, entryUri, wifiAddress, before.WifiInterfaceIndex),
                effectiveCredential,
                ct);

            if (authResult.Outcome is not (PortalAuthOutcome.Submitted or PortalAuthOutcome.AlreadyOnline))
            {
                outcome = new PortalTestFeedback(false, authResult.Detail);
                return false;
            }

            var afterDiagnostics = await _wifi.GetAdapterDiagnosticsAsync(ct);
            var after = await _connectivityTester.RunFullCheckAsync(_settings.Policy, profile.Ssid, afterDiagnostics, ct);
            if (!after.Ok)
            {
                outcome = new PortalTestFeedback(false, $"{after.State} · {after.Summary}");
                return false;
            }

            draft.LastAuthenticatedAt = DateTimeOffset.Now;
            draft.LastFailureSummary = null;
            outcome = new PortalTestFeedback(true, _localizer.T("PortalTestPassed"), draft);
            return true;
        });

        return outcome ?? new PortalTestFeedback(false, _localizer.T("ActionCancelled"));
    }
}
