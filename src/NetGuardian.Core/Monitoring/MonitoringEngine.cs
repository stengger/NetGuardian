using System.Net;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Recovery;
using NetGuardian.Core.Models;
using NetGuardian.Core.Portal;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;

namespace NetGuardian.Core.Monitoring;

/// <summary>
/// UI-agnostic monitoring and recovery state machine.
/// Owns the periodic check loop, the failure streak, the recovery cooldown and the
/// graduated recovery execution. The UI subscribes to events and forwards commands.
/// Events are raised on background threads — subscribers must marshal to their own context.
/// </summary>
public sealed partial class MonitoringEngine : IDisposable
{
    private readonly IWifiClient _wifi;
    private readonly IConnectivityTester _tester;
    private readonly ISystemCommandRunner _commands;
    private readonly IHighRiskRecoveryService _highRisk;
    private readonly Func<MonitorPolicy> _policyProvider;
    private readonly Func<IEnumerable<WifiProfile>> _profilesProvider;
    private readonly TimeProvider _time;
    private readonly INetworkChangeSource? _networkChanges;
    private readonly IPortalAuthenticator? _portalAuthenticator;
    private readonly IPortalCredentialStore? _portalCredentials;
    private IDisposable? _networkSubscription;
    private long _generation;
    private DateTimeOffset? _networkChangeDue;
    private DateTimeOffset? _resumeNotBefore;
    private bool _lastCheckHealthy;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly object _lifecycle = new();
    private readonly Dictionary<string, DateTimeOffset> _failedSsids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _pausedPortalSince = new(StringComparer.Ordinal);
    private CancellationTokenSource? _activeCheck;
    private ITimer? _timer;
    private int _failureStreak;
    private DateTimeOffset? _lastRecoveryAttemptAt;
    private int _failedRecoveryRounds;
    private DateTimeOffset? _lastHighRiskAt;
    private bool _disposed;

    public MonitoringEngine(
        IWifiClient wifi,
        IConnectivityTester tester,
        ISystemCommandRunner commands,
        Func<MonitorPolicy> policyProvider,
        Func<IEnumerable<WifiProfile>> profilesProvider,
        TimeProvider? timeProvider = null,
        IHighRiskRecoveryService? highRiskService = null,
        INetworkChangeSource? networkChanges = null,
        IPortalAuthenticator? portalAuthenticator = null,
        IPortalCredentialStore? portalCredentials = null)
    {
        _wifi = wifi;
        _tester = tester;
        _commands = commands;
        _highRisk = highRiskService ?? new HighRiskRecoveryService(commands);
        _policyProvider = policyProvider;
        _profilesProvider = profilesProvider;
        _time = timeProvider ?? TimeProvider.System;
        _networkChanges = networkChanges;
        _portalAuthenticator = portalAuthenticator;
        _portalCredentials = portalCredentials;
    }

    public bool IsMonitoring { get; private set; }
    public bool IsChecking { get; private set; }
    public int FailureStreak => _failureStreak;
    public DateTimeOffset? NextCheckAt { get; private set; }

    /// <summary>How long to wait for a switched profile to actually connect.</summary>
    public TimeSpan WifiValidationTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>How long to wait for the current Wi-Fi to reconnect.</summary>
    public TimeSpan WifiRestoreTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Polling interval while waiting for an SSID to appear.</summary>
    public TimeSpan SsidPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Pause after enabling the Wi-Fi adapter before re-reading diagnostics.</summary>
    public TimeSpan AdapterEnableSettleDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Pause after a high-risk action before probing connectivity again.</summary>
    public TimeSpan HighRiskSettleDelay { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>单轮检测与恢复的总截止时间。</summary>
    public TimeSpan CheckTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>单次完整探测的时限，为候选网络恢复保留时间。</summary>
    public TimeSpan FullProbeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>单次网页自动认证与复测的子时限。</summary>
    public TimeSpan PortalAuthTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>当存在可尝试的备用 Wi-Fi 时，原地认证必须保留的备用切换预算。</summary>
    public TimeSpan BackupSwitchReserveBudget { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>用户更新凭据或重新验证后，清除引擎内存中的门户暂停标记。</summary>
    public void ClearPortalAuthPause(string ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return;
        lock (_lifecycle) _pausedPortalSince.Remove(ssid);
    }

    public event EventHandler<EngineEventArgs>? EngineEvent;
    public event EventHandler<CheckCompletedEventArgs>? CheckCompleted;
    public event EventHandler<ProfileRecoveryResultEventArgs>? ProfileRecoveryResult;

    /// <summary>Raised whenever IsMonitoring / IsChecking / NextCheckAt change.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Starts periodic monitoring. Does not run an immediate check — call <see cref="CheckNowAsync"/>.</summary>
    public void Start(TimeSpan? initialDelay = null)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsMonitoring) return;
            IsMonitoring = true;
            _resumeNotBefore = initialDelay is { } delay && delay > TimeSpan.Zero ? _time.GetLocalNow().Add(delay) : null;
            _lastCheckHealthy = false;
            var generation = ++_generation;
            _timer = _time.CreateTimer(_ => OnTimerTick(generation), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            try { _networkSubscription = _networkChanges?.Subscribe(() => OnNetworkChanged(generation)); }
            catch (Exception ex) { Raise(EngineEventKind.CheckError, $"网络事件订阅失败，继续定时检测：{ex.Message}"); }
            UpdateSchedule();
        }
        Raise(EngineEventKind.MonitorStarted);
    }

    /// <summary>配置更新后重新安排下一轮；每轮完成时也读取最新周期。</summary>
    public void UpdateSchedule()
    {
        lock (_lifecycle)
        {
            if (_disposed || !IsMonitoring || IsChecking) return;
            var seconds = Math.Max(1, _policyProvider().CheckIntervalSeconds);
            var interval = TimeSpan.FromSeconds(_lastCheckHealthy ? seconds : Math.Min(seconds, 10));
            if (_networkChangeDue is { } due)
                interval = due > _time.GetLocalNow() ? due - _time.GetLocalNow() : TimeSpan.FromMilliseconds(1);
            if (_resumeNotBefore is { } notBefore && notBefore - _time.GetLocalNow() > interval)
                interval = notBefore - _time.GetLocalNow();
            NextCheckAt = _time.GetLocalNow().Add(interval);
            _timer?.Change(interval, Timeout.InfiniteTimeSpan);
        }
        OnStateChanged();
    }

    public void Stop()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            StopCore();
        }
        Raise(EngineEventKind.MonitorStopped);
        OnStateChanged();
    }

    private void StopCore()
    {
        IsMonitoring = false;
        _generation++;
        _networkSubscription?.Dispose();
        _networkSubscription = null;
        _networkChangeDue = null;
        _resumeNotBefore = null;
        _timer?.Dispose();
        _timer = null;
        NextCheckAt = null;
        _activeCheck?.Cancel();
    }

    private void OnNetworkChanged(long generation)
    {
        lock (_lifecycle)
        {
            if (_disposed || !IsMonitoring || generation != _generation) return;
            _networkChangeDue ??= _time.GetLocalNow().AddSeconds(1);
            UpdateSchedule();
        }
    }

    private void OnTimerTick(long generation) => _ = RunTimerCheckAsync(generation);

    private async Task RunTimerCheckAsync(long generation)
    {
        try
        {
            Task check;
            lock (_lifecycle)
            {
                if (_disposed || !IsMonitoring || generation != _generation || IsChecking || NextCheckAt is null) return;
                var now = _time.GetLocalNow();
                if (NextCheckAt > now)
                {
                    // 单次定时回调可能提前或遇到时钟回拨，必须保留剩余等待。
                    var remaining = NextCheckAt.Value - now;
                    _timer?.Change(remaining < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : remaining, Timeout.InfiniteTimeSpan);
                    return;
                }
                check = RunCheckAsync(_lastCheckHealthy && _networkChangeDue is null);
            }
            await check;
        }
        catch (Exception ex)
        {
            // 定时回调必须观察异常，停止/销毁与回调交错也不能产生未观察任务。
            try { Raise(EngineEventKind.CheckError, ex.Message); } catch { }
        }
    }

    /// <summary>运行一轮检测与恢复；并发请求直接返回。</summary>
    public Task CheckNowAsync(CancellationToken cancellationToken = default) => RunCheckAsync(false, cancellationToken);

    private async Task RunCheckAsync(bool lightweight, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource active;
        long generation;
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_checkLock.Wait(0)) return;
            active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCheck = active;
            generation = _generation;
            IsChecking = true;
            _networkChangeDue = null;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            NextCheckAt = null;
        }
        using var deadline = new CancellationTokenSource(CheckTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(active.Token, deadline.Token);
        var token = linked.Token;
        var reportRecorded = false;
        try
        {
            OnStateChanged();
            var policy = _policyProvider();
            ConnectivityReport? report = lightweight ? await TryLightweightCheckAsync(policy, token) : null;
            string? adapterName = null;
            using var probeDeadline = new CancellationTokenSource(FullProbeTimeout);
            using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, probeDeadline.Token);
            var probeToken = probeCancellation.Token;
            try
            {
                if (report?.Ok != true)
                {
                    var wifiDiagnostics = await _wifi.GetAdapterDiagnosticsAsync(probeToken).WaitAsync(probeToken);
                    adapterName = wifiDiagnostics.AdapterName;
                    if (IsMonitoring && RecoveryPlanner.ShouldTryEnableWifi(policy, wifiDiagnostics))
                    {
                        Raise(EngineEventKind.TryingEnableWifiAdapter, adapterName ?? "Wi-Fi");
                        probeToken.ThrowIfCancellationRequested();
                        var enabled = await _wifi.TryEnableWifiAdapterAsync(adapterName, probeToken).WaitAsync(probeToken);
                        Raise(enabled ? EngineEventKind.WifiAdapterEnabled : EngineEventKind.WifiAdapterEnableFailed);
                        if (!enabled && _wifi.LastAdapterEnableFailure is { } failure)
                            Raise(EngineEventKind.CheckError, failure);
                        await DelayAsync(AdapterEnableSettleDelay, probeToken);
                        wifiDiagnostics = await _wifi.GetAdapterDiagnosticsAsync(probeToken).WaitAsync(probeToken);
                    }
                    report = await CheckWifiAsync(policy, wifiDiagnostics, probeToken);
                }
            }
            catch (OperationCanceledException) when (probeDeadline.IsCancellationRequested && !token.IsCancellationRequested)
            {
                Raise(EngineEventKind.CheckError, "完整网络探测超过时限");
                report = new ConnectivityReport { State = NetworkState.Failed, Summary = "完整网络探测超过时限" };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // 探针自身异常仍累计失败，允许后续轻恢复，不能据此推断设备损坏。
                Raise(EngineEventKind.CheckError, ex.Message);
                report = new ConnectivityReport { State = NetworkState.Failed, Summary = ex.Message };
            }
            lock (_lifecycle)
            {
                token.ThrowIfCancellationRequested();
                if (generation != _generation) throw new OperationCanceledException("监控生命周期已改变。", token);
                _lastCheckHealthy = report.Ok;
                _failureStreak = report.Ok ? 0 : _failureStreak + 1;
                if (report.Ok) _failedRecoveryRounds = 0;
                reportRecorded = true;
            }
            if (report.Ok)
            {
                _previousSwitchFailures.Clear();
                if (!report.IsLightweight) RememberVerifiedWifi(report.CurrentSsid);
            }
            ObserveReport(report);
            CheckCompleted?.Invoke(this, new CheckCompletedEventArgs(report, _failureStreak));
            token.ThrowIfCancellationRequested();
            await TryRecoverIfNeededAsync(policy, report, adapterName, token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (deadline.IsCancellationRequested && !active.IsCancellationRequested)
            {
                lock (_lifecycle)
                {
                    if (generation == _generation)
                    {
                        _lastCheckHealthy = false;
                        if (!reportRecorded) _failureStreak++;
                    }
                }
                Raise(EngineEventKind.CheckError, "本轮检测与恢复超过总时限");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            lock (_lifecycle)
            {
                if (generation == _generation && !active.IsCancellationRequested)
                {
                    _lastCheckHealthy = false;
                    if (!reportRecorded) _failureStreak++;
                }
            }
            Raise(EngineEventKind.CheckError, ex.Message);
        }
        finally
        {
            lock (_lifecycle)
            {
                _activeCheck = null;
                active.Dispose();
                IsChecking = false;
                _checkLock.Release();
            }
            UpdateSchedule();
            OnStateChanged();
        }
    }

    private async Task<ConnectivityReport?> TryLightweightCheckAsync(MonitorPolicy policy, CancellationToken token)
    {
        // 轻量超时后完整诊断仍有独立预算，不依据轻量失败直接执行恢复。
        using var deadline = new CancellationTokenSource(FullProbeTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        try
        {
            return await _tester.RunLightweightCheckAsync(policy, linked.Token).WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Raise(EngineEventKind.CheckError, $"轻量检测异常或超时，转为完整诊断：{ex.Message}");
            return null;
        }
    }

    private async Task TryRecoverIfNeededAsync(MonitorPolicy policy, ConnectivityReport report, string? adapterName, CancellationToken cancellationToken)
    {
        var decision = RecoveryPlanner.Evaluate(
            policy, report.Ok, IsMonitoring, _failureStreak, _lastRecoveryAttemptAt, _time.GetLocalNow());

        if (decision.Kind == RecoveryDecisionKind.CooldownActive)
        {
            Raise(EngineEventKind.RecoveryCooldownActive, decision.CooldownRemaining);
            return;
        }

        if (decision.Kind != RecoveryDecisionKind.Proceed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _lastRecoveryAttemptAt = _time.GetLocalNow();
        await AttemptRecoveryAsync(policy, report, adapterName, cancellationToken);
    }

    private async Task AttemptRecoveryAsync(MonitorPolicy policy, ConnectivityReport report, string? adapterName, CancellationToken cancellationToken)
    {
        var currentSsid = report.CurrentSsid;
        var attempted = false;
        var roundDeadline = _time.GetUtcNow().Add(CheckTimeout);
        var portalAttempted = new HashSet<string>(StringComparer.Ordinal);
        var skipCurrentReconnect = false;
        Raise(EngineEventKind.RecoveryStarted, _failureStreak);

        if (policy.EnableDnsFlush && report.State == NetworkState.DnsIssue)
        {
            attempted = true;
            Raise(EngineEventKind.RecoveryDnsFlush);
            var dnsRecovered = await TryRecoveryStepAsync(async () =>
            {
                var result = await _commands.RunAsync("ipconfig.exe", ["/flushdns"], cancellationToken).WaitAsync(cancellationToken);
                Raise(EngineEventKind.RecoveryDnsFlushCompleted, result.Ok, result.ExitCode, result.Output);
                if (!result.Ok) return false;
                var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(cancellationToken).WaitAsync(cancellationToken);
                var retest = await ProbeAsync(policy, diagnostics, cancellationToken);
                ObserveReport(retest, afterAction: true);
                if (!retest.Ok) return false;
                lock (_lifecycle)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _lastCheckHealthy = true;
                    _failureStreak = 0;
                    _failedRecoveryRounds = 0;
                }
                CheckCompleted?.Invoke(this, new CheckCompletedEventArgs(retest, 0));
                return true;
            }, cancellationToken);
            if (dnsRecovered) return;
        }

        if (report.State == NetworkState.DnsIssue && !string.IsNullOrWhiteSpace(currentSsid) &&
            report.Probes.Any(probe => probe.Name == "Gateway ping" && probe.Ok))
        {
            attempted = true;
            var dnsRecheckRecovered = await TryRecoveryStepAsync(async () =>
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                var token = linked.Token;
                try
                {
                    var before = await _wifi.GetConnectionInfoAsync(token).WaitAsync(token);
                    if (!before.ReadSucceeded || !before.Connected || !RecoveryPlanner.SsidEquals(before.Ssid, currentSsid))
                    {
                        currentSsid = before.ReadSucceeded && before.Connected ? before.Ssid : null;
                        return false;
                    }
                    var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(token).WaitAsync(token);
                    var retest = await CheckWifiAsync(policy, diagnostics, token, before);
                    var after = await _wifi.GetConnectionInfoAsync(token).WaitAsync(token);
                    if (!after.ReadSucceeded || !after.Connected || !RecoveryPlanner.SsidEquals(after.Ssid, before.Ssid)
                        || !string.Equals(after.AdapterName, before.AdapterName, StringComparison.OrdinalIgnoreCase))
                    {
                        currentSsid = after.ReadSucceeded && after.Connected ? after.Ssid : null;
                        return false;
                    }
                    ObserveReport(retest);
                    if (!retest.Ok) return false;
                    lock (_lifecycle)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _lastCheckHealthy = true;
                        _failureStreak = _failedRecoveryRounds = 0;
                    }
                    _previousSwitchFailures.Clear();
                    RememberVerifiedWifi(after.Ssid);
                    CheckCompleted?.Invoke(this, new CheckCompletedEventArgs(retest, 0));
                    return true;
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    Raise(EngineEventKind.CheckError, "原 Wi-Fi DNS 与公网复测超时。");
                    return false;
                }
            }, cancellationToken);
            if (dnsRecheckRecovered) return;
        }

        if (_portalAuthenticator is not null
            && !string.IsNullOrWhiteSpace(currentSsid)
            && report.PortalEvidence != PortalEvidence.None
            && !IsSsidCoolingDown(currentSsid))
        {
            var currentProfile = _profilesProvider().FirstOrDefault(p => RecoveryPlanner.SsidEquals(p.Ssid, currentSsid));
            if (currentProfile is not null && CanAttemptPortalAuth(currentProfile) && portalAttempted.Add(currentSsid))
            {
                var hasBackup = policy.EnableAutoSwitch
                    && RecoveryPlanner.OrderSwitchCandidates(_profilesProvider(), currentSsid).Any(p => !IsSsidCoolingDown(p.Ssid));
                var remaining = roundDeadline - _time.GetUtcNow();
                var reserved = hasBackup ? BackupSwitchReserveBudget : TimeSpan.Zero;
                var available = remaining - reserved;
                var subTimeout = available < PortalAuthTimeout ? available : PortalAuthTimeout;

                if (subTimeout > TimeSpan.Zero)
                {
                    attempted = true;
                    var (portalRecovered, pauseOrStopCurrent) = await TryPortalAuthenticateAndVerifyAsync(
                        policy, currentProfile, report, subTimeout, cancellationToken);
                    if (portalRecovered) return;
                    skipCurrentReconnect = pauseOrStopCurrent || policy.EnableAutoSwitch;
                }
                else
                {
                    Raise(EngineEventKind.CheckError, $"跳过“{currentSsid}”原地认证：需为备用 Wi-Fi 切换保留恢复预算。");
                }
            }
        }

        if (!skipCurrentReconnect && policy.EnableCurrentWifiReconnect && !string.IsNullOrWhiteSpace(currentSsid) && !IsSsidCoolingDown(currentSsid))
        {
            attempted = true;
            Raise(EngineEventKind.RecoveryReconnectCurrent, currentSsid);
            var reconnected = await TryRecoveryStepAsync(async () =>
            {
                await _wifi.DisconnectAsync(cancellationToken).WaitAsync(cancellationToken);
                await DelayAsync(SsidPollInterval, cancellationToken);
                return await ConnectAndValidateAsync(policy, currentSsid, WifiRestoreTimeout, cancellationToken, portalAttempted, roundDeadline);
            }, cancellationToken);
            RecordProfileResult(policy, currentSsid, reconnected);
            ProfileRecoveryResult?.Invoke(this, new ProfileRecoveryResultEventArgs(currentSsid, reconnected));
            if (reconnected)
            {
                _failedRecoveryRounds = 0;
                _failureStreak = 0;
                Raise(EngineEventKind.RecoveryWifiConnected, currentSsid);
                return;
            }
        }

        if (policy.EnableAutoSwitch)
        {
            var switched = await TrySwitchWifiAsync(policy, currentSsid, cancellationToken, portalAttempted, roundDeadline);
            attempted |= switched.Attempted;
            if (switched.Connected) return;
        }

        if (!attempted)
        {
            Raise(EngineEventKind.RecoveryNoActionsEnabled);
        }

        _failedRecoveryRounds++;
        Raise(EngineEventKind.RecoveryFailed);
        await TryHighRiskRecoveryAsync(policy, report, adapterName, cancellationToken);
    }

    private async Task TryHighRiskRecoveryAsync(MonitorPolicy policy, ConnectivityReport report, string? adapterName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!RecoveryPlanner.IsDeviceLayerFailure(report.State)) return;
        var decision = RecoveryPlanner.EvaluateHighRisk(policy, _failedRecoveryRounds, _lastHighRiskAt, _time.GetLocalNow());
        if (decision.Kind == HighRiskDecisionKind.CooldownActive)
        {
            Raise(EngineEventKind.HighRiskCooldownActive, decision.CooldownRemaining);
            return;
        }

        if (decision.Kind != HighRiskDecisionKind.Proceed)
        {
            return;
        }

        // 轻恢复可能已经改变关联状态；升级必须依据此刻的完整探测。
        // 验证异常直接终止本轮，不能把未知结果当成设备故障。
        var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(cancellationToken).WaitAsync(cancellationToken);
        report = await ProbeAsync(policy, diagnostics, cancellationToken);
        ObserveReport(report);
        if (!RecoveryPlanner.IsDeviceLayerFailure(report.State)) return;
        adapterName = diagnostics.AdapterName;
        _lastHighRiskAt = _time.GetLocalNow();

        // 旧配置只有主开关；出现细分配置后则严格按细分动作执行。
        var legacyActions = !policy.EnableWlanServiceRestart && !policy.EnableAdapterRestart;
        if ((legacyActions || policy.EnableWlanServiceRestart) && await RunHighRiskActionAsync(
                policy, report, "WlanService",
                token => _highRisk.RestartWlanServiceAsync(token), cancellationToken))
        {
            return;
        }

        if ((legacyActions || policy.EnableAdapterRestart) && !string.IsNullOrWhiteSpace(adapterName))
        {
            await RunHighRiskActionAsync(
                policy, report, "Adapter",
                token => _highRisk.RestartAdapterAsync(adapterName, token), cancellationToken);
        }
    }

    /// <summary>执行高风险动作后重新检测；网络恢复或已只剩上游故障时终止设备升级。</summary>
    private async Task<bool> RunHighRiskActionAsync(
        MonitorPolicy policy,
        ConnectivityReport reportBefore,
        string action,
        Func<CancellationToken, Task<SystemCommandResult>> run,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Raise(EngineEventKind.HighRiskActionStarted, action);
        SystemCommandResult commandResult;
        try
        {
            // 服务内部会在取消后执行有独立时限的必要收尾；完成前继续持有检查锁。
            commandResult = await run(cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 动作失败仍可重新验证；后续验证失败则交给外层结束本轮，不继续升级。
            Raise(EngineEventKind.CheckError, ex.Message);
            commandResult = new SystemCommandResult(-1, ex.Message);
        }
        cancellationToken.ThrowIfCancellationRequested();
        await DelayAsync(HighRiskSettleDelay, cancellationToken);

        var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(cancellationToken).WaitAsync(cancellationToken);
        var reportAfter = await ProbeAsync(policy, diagnostics, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (reportAfter.Ok)
        {
            _failedRecoveryRounds = 0;
            _failureStreak = 0;
        }

        Observe(IncidentStepKind.ActionResult, action, commandResult.Ok ? "系统命令执行成功" : "系统命令执行失败");
        ObserveReport(reportAfter, afterAction: commandResult.Ok);
        Raise(EngineEventKind.HighRiskActionCompleted,
            action, commandResult.Ok, reportAfter.Ok, reportBefore.State.ToString(), reportAfter.State.ToString());
        return reportAfter.Ok || !RecoveryPlanner.IsDeviceLayerFailure(reportAfter.State);
    }

    private bool IsSsidCoolingDown(string ssid) =>
        _failedSsids.TryGetValue(ssid, out var until) && _time.GetUtcNow() < until;

    private void RecordProfileResult(MonitorPolicy policy, string ssid, bool succeeded)
    {
        if (succeeded) _failedSsids.Remove(ssid);
        else _failedSsids[ssid] = _time.GetUtcNow().AddSeconds(Math.Max(0, policy.RecoveryCooldownSeconds));
    }

    private async Task<bool> ConnectAndValidateAsync(
        MonitorPolicy policy,
        string ssid,
        TimeSpan timeout,
        CancellationToken token,
        HashSet<string>? portalAttempted = null,
        DateTimeOffset? roundDeadline = null)
    {
        token.ThrowIfCancellationRequested();
        // 已启动的连接操作必须收尾后才释放检查控制权，避免停止后延迟切网。
        var profile = _profilesProvider().FirstOrDefault(item => RecoveryPlanner.SsidEquals(item.Ssid, ssid));
        using var deadline = new CancellationTokenSource(timeout);
        using var validation = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var validationToken = validation.Token;
        try
        {
            var accepted = profile is not null && profile.ConnectionProfileName != ssid
                ? await _wifi.ConnectProfileAsync(profile.ConnectionProfileName, ssid, validationToken)
                : await _wifi.ConnectAsync(ssid, validationToken);
            validationToken.ThrowIfCancellationRequested();
            if (!accepted)
            {
                var reason = _wifi.LastConnectionFailure ?? "系统未提供原因。";
                Observe(IncidentStepKind.ActionResult, ssid, $"连接请求失败：{reason}");
                Raise(EngineEventKind.CheckError, $"连接网络“{ssid}”的请求失败：{reason}");
                return false;
            }
            var associationRecorded = false;
            while (true)
            {
                var connection = await WaitForSsidAsync(ssid, timeout, validationToken);
                if (connection is null) return false;
                if (!associationRecorded)
                {
                    Observe(IncidentStepKind.WifiAssociated, ssid, connection.AdapterName);
                    associationRecorded = true;
                }
                var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(validationToken).WaitAsync(validationToken);
                // 公网探测保留独立预算，不能被已结束的地址等待预算截断。
                var report = await ProbeAsync(policy, diagnostics, token, connection);
                token.ThrowIfCancellationRequested();
                if (report.Ok)
                {
                    var confirmed = await _wifi.GetConnectionInfoAsync(token).WaitAsync(token);
                    var sameRoute = confirmed.ReadSucceeded && confirmed.Connected && RecoveryPlanner.SsidEquals(confirmed.Ssid, ssid)
                        && string.Equals(confirmed.AdapterName, connection.AdapterName, StringComparison.OrdinalIgnoreCase);
                    if (sameRoute)
                    {
                        RememberVerifiedWifi(ssid);
                        ObserveReport(report, afterAction: true);
                    }
                    else Observe(IncidentStepKind.Inconclusive, ssid, "复测后出口已改变或未知", report);
                    return sameRoute;
                }
                ObserveReport(report);

                if (report.PortalEvidence != PortalEvidence.None
                    && _portalAuthenticator is not null
                    && profile is not null
                    && CanAttemptPortalAuth(profile)
                    && (portalAttempted is null || portalAttempted.Add(ssid)))
                {
                    var subTimeout = PortalAuthTimeout;
                    if (roundDeadline is { } rd)
                    {
                        var remaining = rd - _time.GetUtcNow();
                        if (remaining < subTimeout) subTimeout = remaining;
                    }
                    if (subTimeout > TimeSpan.Zero)
                    {
                        var (portalRecovered, _) = await TryPortalAuthenticateAndVerifyAsync(
                            policy, profile, report, subTimeout, token, connection);
                        return portalRecovered;
                    }
                    return false;
                }

                // 连接建立期间接口状态及 SSID 可能短暂回落，明确的关闭或设备故障不等待。
                var canWaitForConnection = (report.State is NetworkState.Disconnected or NetworkState.WifiDisabled or NetworkState.WifiDisconnected)
                    && diagnostics.AdapterPresent && (diagnostics.ProblemCode is null or 0)
                    && diagnostics.HardwareRadioOn != false && diagnostics.SoftwareRadioOn != false;
                if (!canWaitForConnection)
                {
                    Observe(IncidentStepKind.ActionResult, ssid, $"已关联，公网验证失败：{report.State}，{report.Summary}");
                    Raise(EngineEventKind.CheckError, $"网络“{ssid}”连接后校验失败：{report.State}，{report.Summary}");
                    return false;
                }
                await DelayAsync(SsidPollInterval > TimeSpan.Zero ? SsidPollInterval : TimeSpan.FromMilliseconds(1), validationToken);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            Observe(IncidentStepKind.ActionResult, ssid, "连接或公网验证超时");
            Raise(EngineEventKind.CheckError, $"网络“{ssid}”在 {timeout.TotalSeconds:g} 秒内未完成连接或公网验证。");
            return false;
        }
    }

    private bool CanAttemptPortalAuth(WifiProfile profile)
    {
        if (profile.PortalAuth is not { CanAutoAuthenticate: true } config) return false;
        lock (_lifecycle)
        {
            if (_pausedPortalSince.TryGetValue(profile.Ssid, out var pausedAt))
            {
                if (config.LastAuthenticatedAt is { } authAt && authAt > pausedAt)
                    _pausedPortalSince.Remove(profile.Ssid);
                else
                    return false;
            }
        }
        return true;
    }

    private async Task<(bool Recovered, bool ShouldSkipCurrentReconnect)> TryPortalAuthenticateAndVerifyAsync(
        MonitorPolicy policy,
        WifiProfile profile,
        ConnectivityReport report,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        WifiConnectionInfo? initialConnection = null)
    {
        var config = profile.PortalAuth!;
        var sanitizedOrigin = report.PortalSanitizedOrigin ?? config.AllowedOrigin;
        PortalCredential? credential = null;
        if (config.Mode == PortalAuthMode.CredentialsForm)
        {
            if (_portalCredentials is null
                || string.IsNullOrWhiteSpace(config.CredentialId)
                || !_portalCredentials.TryLoad(config.CredentialId, out credential)
                || credential is null)
            {
                Raise(EngineEventKind.RecoveryPortalAuthCompleted, profile.Ssid, PortalAuthOutcome.CredentialMissing.ToString(), "未能读取已保存的网页认证凭据。");
                return (false, true);
            }
        }

        var beforeConnection = initialConnection ?? await _wifi.GetConnectionInfoAsync(cancellationToken).WaitAsync(cancellationToken);
        if (!beforeConnection.ReadSucceeded || !beforeConnection.Connected || !RecoveryPlanner.SsidEquals(beforeConnection.Ssid, profile.Ssid))
        {
            Observe(IncidentStepKind.Inconclusive, profile.Ssid, "网页认证前无线连接已变化", report);
            return (false, true);
        }

        IPAddress.TryParse(report.Ipv4, out var wifiAddress);
        Raise(EngineEventKind.RecoveryPortalAuthStarted, profile.Ssid, sanitizedOrigin);

        using var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        authCts.CancelAfter(timeout);

        PortalAuthResult authResult;
        try
        {
            var request = new PortalAuthRequest(profile.Ssid, config, report.PortalEntryUri, wifiAddress, report.WifiInterfaceIndex);
            authResult = await _portalAuthenticator!.AuthenticateAsync(request, credential, authCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && authCts.IsCancellationRequested)
        {
            authResult = new PortalAuthResult(PortalAuthOutcome.TimedOut, $"网页认证超过 {timeout.TotalSeconds:F0} 秒时限。", sanitizedOrigin);
        }

        Raise(EngineEventKind.RecoveryPortalAuthCompleted, profile.Ssid, authResult.Outcome.ToString(), authResult.Detail);

        if (authResult.ShouldPause)
        {
            lock (_lifecycle) _pausedPortalSince[profile.Ssid] = _time.GetUtcNow();
            Raise(EngineEventKind.RecoveryPortalAuthPaused, profile.Ssid, authResult.Detail);
            return (false, true);
        }

        if (authResult.Outcome is not (PortalAuthOutcome.Submitted or PortalAuthOutcome.AlreadyOnline))
        {
            return (false, true);
        }

        var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(cancellationToken).WaitAsync(cancellationToken);
        if (beforeConnection.Connected && !string.IsNullOrWhiteSpace(beforeConnection.AdapterName))
            diagnostics = diagnostics with { AdapterName = beforeConnection.AdapterName };

        var retest = await ProbeAsync(policy, diagnostics, cancellationToken, beforeConnection);
        cancellationToken.ThrowIfCancellationRequested();

        var afterConnection = await _wifi.GetConnectionInfoAsync(cancellationToken).WaitAsync(cancellationToken);
        var sameRoute = afterConnection.ReadSucceeded
            && afterConnection.Connected
            && RecoveryPlanner.SsidEquals(afterConnection.Ssid, profile.Ssid)
            && string.Equals(afterConnection.AdapterName, beforeConnection.AdapterName, StringComparison.OrdinalIgnoreCase);

        if (!sameRoute)
        {
            Observe(IncidentStepKind.Inconclusive, profile.Ssid, "网页认证复测时无线出口已改变或断开", retest);
            return (false, true);
        }

        if (!retest.Ok)
        {
            ObserveReport(retest);
            Raise(EngineEventKind.CheckError, $"网络“{profile.Ssid}”网页认证后公网复测未通过：{retest.State}，{retest.Summary}");
            return (false, true);
        }

        ObserveReport(retest, afterAction: true);
        lock (_lifecycle)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lastCheckHealthy = true;
            _failureStreak = 0;
            _failedRecoveryRounds = 0;
        }
        config.LastAuthenticatedAt = _time.GetLocalNow();
        config.LastFailureSummary = null;
        _previousSwitchFailures.Clear();
        RememberVerifiedWifi(profile.Ssid);
        RecordProfileResult(policy, profile.Ssid, true);
        ProfileRecoveryResult?.Invoke(this, new ProfileRecoveryResultEventArgs(profile.Ssid, true));
        CheckCompleted?.Invoke(this, new CheckCompletedEventArgs(retest, 0));
        Raise(EngineEventKind.RecoveryWifiConnected, profile.Ssid);
        return (true, false);
    }

    private async Task<ConnectivityReport> CheckWifiAsync(MonitorPolicy policy, WifiAdapterDiagnostics diagnostics,
        CancellationToken token, WifiConnectionInfo? connection = null)
    {
        connection ??= await _wifi.GetConnectionInfoAsync(token).WaitAsync(token);
        if (connection.Connected && !string.IsNullOrWhiteSpace(connection.AdapterName))
            diagnostics = diagnostics with { AdapterName = connection.AdapterName };
        var ssid = connection.ReadSucceeded && connection.Connected ? connection.Ssid : null;
        return await _tester.RunFullCheckAsync(policy, ssid, diagnostics, token).WaitAsync(token);
    }

    private async Task<ConnectivityReport> ProbeAsync(MonitorPolicy policy,
        WifiAdapterDiagnostics diagnostics, CancellationToken token, WifiConnectionInfo? connection = null)
    {
        long generation;
        lock (_lifecycle)
        {
            token.ThrowIfCancellationRequested();
            generation = _generation;
        }
        using var deadline = new CancellationTokenSource(FullProbeTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        try
        {
            var report = await CheckWifiAsync(policy, diagnostics, linked.Token, connection);
            lock (_lifecycle)
            {
                token.ThrowIfCancellationRequested();
                if (generation != _generation) throw new OperationCanceledException("监控生命周期已改变。", token);
                _lastCheckHealthy = report.Ok;
            }
            return report;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw new TimeoutException("完整网络探测超过时限");
        }
    }

    private async Task<bool> TryRecoveryStepAsync(Func<Task<bool>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { return await action(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Raise(EngineEventKind.CheckError, ex.Message);
            return false;
        }
    }

    private async Task<WifiConnectionInfo?> WaitForSsidAsync(string ssid, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = _time.GetLocalNow().Add(timeout);
        while (_time.GetLocalNow() < deadline)
        {
            var connection = await _wifi.GetConnectionInfoAsync(cancellationToken).WaitAsync(cancellationToken);
            if (connection.ReadSucceeded && connection.Connected && RecoveryPlanner.SsidEquals(connection.Ssid, ssid))
                return connection;

            await DelayAsync(SsidPollInterval, cancellationToken);
        }

        return null;
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, _time, cancellationToken);
    }

    private void Raise(EngineEventKind kind, params object?[] args)
    {
        EngineEvent?.Invoke(this, new EngineEventArgs(kind, args));
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lifecycle)
        {
            if (_disposed) return;
            StopCore();
            _disposed = true;
            // 活跃检查仍需在 finally 释放信号量；不销毁其正在使用的锁。
        }
    }
}
