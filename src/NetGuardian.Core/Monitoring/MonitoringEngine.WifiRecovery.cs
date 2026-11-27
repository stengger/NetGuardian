using NetGuardian.Core.Models;
using NetGuardian.Core.Wifi;

namespace NetGuardian.Core.Monitoring;

public sealed partial class MonitoringEngine
{
    private const int MaxWifiSwitchAttempts = 3;
    private static readonly TimeSpan RecentWifiLifetime = TimeSpan.FromMinutes(15);
    private readonly Dictionary<string, DateTimeOffset> _recentVerifiedWifi = new(StringComparer.Ordinal);
    private readonly HashSet<string> _previousSwitchFailures = new(StringComparer.Ordinal);
    internal TimeSpan WifiSwitchBudget { get; set; } = TimeSpan.FromSeconds(45);
    internal TimeSpan RecoveryScanAttemptTimeout { get; set; } = TimeSpan.FromSeconds(5);

    private void RememberVerifiedWifi(string? ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return;
        var now = _time.GetUtcNow();
        foreach (var expired in _recentVerifiedWifi.Where(item => now - item.Value > RecentWifiLifetime).ToArray())
            _recentVerifiedWifi.Remove(expired.Key);
        _recentVerifiedWifi[ssid] = now;
    }

    private async Task<WifiRecoveryScan> ScanRecoveryWifiAsync(CancellationToken token)
    {
        var scan = new WifiRecoveryScan(WifiScanStatus.Failed, []);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            token.ThrowIfCancellationRequested();
            using var deadline = new CancellationTokenSource(RecoveryScanAttemptTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            try
            {
                scan = await _wifi.ScanForRecoveryAsync(linked.Token).WaitAsync(linked.Token);
                if (scan.Status == WifiScanStatus.Completed) return scan;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
            {
                scan = new(WifiScanStatus.TimedOut, []);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                scan = new(WifiScanStatus.Failed, []);
                Raise(EngineEventKind.CheckError, $"恢复扫描失败：{ex.Message}");
            }
        }
        return scan;
    }

    private async Task<(bool Attempted, bool Connected)> TrySwitchWifiAsync(
        MonitorPolicy policy,
        string? currentSsid,
        CancellationToken token,
        HashSet<string>? portalAttempted = null,
        DateTimeOffset? roundDeadline = null)
    {
        var scan = await ScanRecoveryWifiAsync(token);
        var profiles = _profilesProvider().ToArray();
        var visible = scan.Networks.Where(network => network.IsConnectable).Select(network => network.Ssid).ToHashSet(StringComparer.Ordinal);
        var ready = scan.Status == WifiScanStatus.Completed
            ? RecoveryPlanner.OrderSwitchCandidates(profiles, currentSsid, visible)
            : RecoveryPlanner.OrderSwitchCandidates(profiles, currentSsid)
                .Where(profile => WasRecentlyVerified(profile.Ssid) && !IsSsidCoolingDown(profile.Ssid))
                .Take(1).ToArray();
        var deferPreviousFailures = ready.Any(profile => !_previousSwitchFailures.Contains(profile.Ssid) && !IsSsidCoolingDown(profile.Ssid));
        var tried = 0;
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var reason = ready.Count == 0 ? "无合格候选" : "候选耗尽";
        var scanResult = scan.Status switch
        {
            WifiScanStatus.Completed => "扫描完成",
            WifiScanStatus.TimedOut => "扫描超时",
            _ => "扫描失败"
        };
        var started = _time.GetTimestamp();
        try
        {
            foreach (var profile in ready)
            {
                token.ThrowIfCancellationRequested();
                if (tried >= MaxWifiSwitchAttempts) { reason = "已达单轮尝试上限"; break; }
                var remaining = WifiSwitchBudget - _time.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero) { reason = "切换预算耗尽"; break; }
                if (IsSsidCoolingDown(profile.Ssid) || (deferPreviousFailures && _previousSwitchFailures.Contains(profile.Ssid)))
                {
                    reason = "候选耗尽，含冷却或上一轮失败项";
                    continue;
                }
                // 后台可能已热加载新设置，发出请求前再次核对恢复开关与凭据。
                if (!_profilesProvider().Any(item => item.Ssid == profile.Ssid && item.IsRecoveryCandidate)) continue;
                tried++;
                var timeout = remaining < WifiValidationTimeout ? remaining : WifiValidationTimeout;
                Raise(EngineEventKind.RecoverySwitchWifi, profile.Ssid);
                var connected = await TryRecoveryStepAsync(
                    () => ConnectAndValidateAsync(policy, profile.Ssid, timeout, token, portalAttempted, roundDeadline), token);
                RecordProfileResult(policy, profile.Ssid, connected);
                ProfileRecoveryResult?.Invoke(this, new ProfileRecoveryResultEventArgs(profile.Ssid, connected));
                if (connected)
                {
                    reason = "已验证公网恢复";
                    _failureStreak = _failedRecoveryRounds = 0;
                    Raise(EngineEventKind.RecoveryWifiConnected, profile.Ssid);
                    return (true, true);
                }
                failed.Add(profile.Ssid);
                if (_time.GetElapsedTime(started) >= WifiSwitchBudget) { reason = "切换预算耗尽"; break; }
            }
            return (tried > 0, false);
        }
        catch (OperationCanceledException)
        {
            reason = "本轮取消或达到总时限";
            throw;
        }
        finally
        {
            _previousSwitchFailures.Clear();
            if (reason != "已验证公网恢复") _previousSwitchFailures.UnionWith(failed);
            Raise(EngineEventKind.RecoveryWifiSwitchSummary, scanResult, profiles.Length, ready.Count, profiles.Length - tried, tried, reason);
        }
    }

    private bool WasRecentlyVerified(string ssid) => _recentVerifiedWifi.TryGetValue(ssid, out var verifiedAt)
        && _time.GetUtcNow() - verifiedAt is var age && age >= TimeSpan.Zero && age <= RecentWifiLifetime;
}
