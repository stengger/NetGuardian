using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Recovery;
using NetGuardian.Core.Models;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;

namespace NetGuardian.Core.Monitoring;

/// <summary>
/// UI-agnostic monitoring and recovery state machine.
/// Owns the periodic check loop, the failure streak, the recovery cooldown and the
/// graduated recovery execution. The UI subscribes to events and forwards commands.
/// Events are raised on background threads — subscribers must marshal to their own context.
/// </summary>
public sealed class MonitoringEngine : IDisposable
{
    private readonly IWifiClient _wifi;
    private readonly IConnectivityTester _tester;
    private readonly ISystemCommandRunner _commands;
    private readonly IHighRiskRecoveryService _highRisk;
    private readonly Func<MonitorPolicy> _policyProvider;
    private readonly Func<IEnumerable<WifiProfile>> _profilesProvider;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly object _lifecycle = new();
    private readonly Dictionary<string, DateTimeOffset> _failedSsids = new(StringComparer.OrdinalIgnoreCase);
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
        IHighRiskRecoveryService? highRiskService = null)
    {
        _wifi = wifi;
        _tester = tester;
        _commands = commands;
        _highRisk = highRiskService ?? new HighRiskRecoveryService(commands);
        _policyProvider = policyProvider;
        _profilesProvider = profilesProvider;
        _time = timeProvider ?? TimeProvider.System;
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

    public event EventHandler<EngineEventArgs>? EngineEvent;
    public event EventHandler<CheckCompletedEventArgs>? CheckCompleted;
    public event EventHandler<ProfileRecoveryResultEventArgs>? ProfileRecoveryResult;

    /// <summary>Raised whenever IsMonitoring / IsChecking / NextCheckAt change.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Starts periodic monitoring. Does not run an immediate check — call <see cref="CheckNowAsync"/>.</summary>
    public void Start()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _timer?.Dispose();
            IsMonitoring = true;
            _timer = _time.CreateTimer(OnTimerTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            UpdateSchedule();
        }
        Raise(EngineEventKind.MonitorStarted);
    }

    /// <summary>配置更新后重新安排下一轮；每轮完成时也读取最新周期。</summary>
    public void UpdateSchedule()
    {
        lock (_lifecycle)
        {
            if (_disposed || !IsMonitoring) return;
            var interval = TimeSpan.FromSeconds(Math.Max(1, _policyProvider().CheckIntervalSeconds));
            _timer?.Change(interval, Timeout.InfiniteTimeSpan);
            NextCheckAt = _time.GetLocalNow().Add(interval);
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
        _timer?.Dispose();
        _timer = null;
        NextCheckAt = null;
        _activeCheck?.Cancel();
    }

    private void OnTimerTick(object? state) => _ = RunTimerCheckAsync();

    private async Task RunTimerCheckAsync()
    {
        try
        {
            Task check;
            lock (_lifecycle)
            {
                if (_disposed || !IsMonitoring) return;
                check = CheckNowAsync();
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
    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource active;
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_checkLock.Wait(0)) return;
            active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCheck = active;
            IsChecking = true;
        }
        using var deadline = new CancellationTokenSource(CheckTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(active.Token, deadline.Token);
        var token = linked.Token;
        try
        {
            OnStateChanged();
            var policy = _policyProvider();
            ConnectivityReport report;
            string? adapterName = null;
            using var probeDeadline = new CancellationTokenSource(FullProbeTimeout);
            using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, probeDeadline.Token);
            var probeToken = probeCancellation.Token;
            try
            {
                var wifiDiagnostics = await _wifi.GetAdapterDiagnosticsAsync(probeToken).WaitAsync(probeToken);
                adapterName = wifiDiagnostics.AdapterName;
                if (RecoveryPlanner.ShouldTryEnableWifi(policy, wifiDiagnostics))
                {
                    Raise(EngineEventKind.TryingEnableWifiAdapter, adapterName ?? "Wi-Fi");
                    probeToken.ThrowIfCancellationRequested();
                    var enabled = await _wifi.TryEnableWifiAdapterAsync(adapterName, probeToken).WaitAsync(probeToken);
                    Raise(enabled ? EngineEventKind.WifiAdapterEnabled : EngineEventKind.WifiAdapterEnableFailed);
                    await DelayAsync(AdapterEnableSettleDelay, probeToken);
                    wifiDiagnostics = await _wifi.GetAdapterDiagnosticsAsync(probeToken).WaitAsync(probeToken);
                }
                var currentSsid = await _wifi.GetCurrentSsidAsync(probeToken).WaitAsync(probeToken);
                report = await _tester.RunFullCheckAsync(policy, currentSsid, wifiDiagnostics, probeToken).WaitAsync(probeToken);
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
            token.ThrowIfCancellationRequested();
            _failureStreak = report.Ok ? 0 : _failureStreak + 1;
            if (report.Ok) _failedRecoveryRounds = 0;
            CheckCompleted?.Invoke(this, new CheckCompletedEventArgs(report, _failureStreak));
            token.ThrowIfCancellationRequested();
            await TryRecoverIfNeededAsync(policy, report, adapterName, token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (deadline.IsCancellationRequested && !active.IsCancellationRequested)
            {
                _failureStreak++;
                Raise(EngineEventKind.CheckError, "本轮检测与恢复超过总时限");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _failureStreak++;
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
        Raise(EngineEventKind.RecoveryStarted, _failureStreak);

        if (policy.EnableDnsFlush)
        {
            attempted = true;
            Raise(EngineEventKind.RecoveryDnsFlush);
            await TryRecoveryStepAsync(async () =>
            {
                var result = await _commands.RunAsync("ipconfig.exe", ["/flushdns"], cancellationToken).WaitAsync(cancellationToken);
                Raise(EngineEventKind.RecoveryDnsFlushCompleted, result.Ok, result.ExitCode, result.Output);
                return result.Ok;
            }, cancellationToken);
        }

        if (policy.EnableCurrentWifiReconnect && !string.IsNullOrWhiteSpace(currentSsid) && !IsSsidCoolingDown(currentSsid))
        {
            attempted = true;
            Raise(EngineEventKind.RecoveryReconnectCurrent, currentSsid);
            var reconnected = await TryRecoveryStepAsync(async () =>
            {
                await _wifi.DisconnectAsync(cancellationToken).WaitAsync(cancellationToken);
                await DelayAsync(SsidPollInterval, cancellationToken);
                return await ConnectAndValidateAsync(policy, currentSsid, WifiRestoreTimeout, cancellationToken);
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
            // 长期关联后可用网络缓存可能过期；刷新失败仍保留已保存配置的连接机会。
            await TryRecoveryStepAsync(async () =>
            {
                using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                scanCancellation.CancelAfter(TimeSpan.FromSeconds(8));
                try { await _wifi.ScanAsync(scanCancellation.Token).WaitAsync(scanCancellation.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("恢复前无线扫描超过时限，继续尝试保存的配置。");
                }
                return true;
            }, cancellationToken);
            foreach (var profile in RecoveryPlanner.OrderSwitchCandidates(_profilesProvider(), currentSsid))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsSsidCoolingDown(profile.Ssid)) continue;
                attempted = true;
                Raise(EngineEventKind.RecoverySwitchWifi, profile.Ssid);
                var connected = await TryRecoveryStepAsync(
                    () => ConnectAndValidateAsync(policy, profile.Ssid, WifiValidationTimeout, cancellationToken), cancellationToken);
                RecordProfileResult(policy, profile.Ssid, connected);
                ProfileRecoveryResult?.Invoke(this, new ProfileRecoveryResultEventArgs(profile.Ssid, connected));
                if (connected)
                {
                    _failedRecoveryRounds = 0;
                    _failureStreak = 0;
                    Raise(EngineEventKind.RecoveryWifiConnected, profile.Ssid);
                    return;
                }
            }
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
        var currentSsid = await _wifi.GetCurrentSsidAsync(cancellationToken).WaitAsync(cancellationToken);
        var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(cancellationToken).WaitAsync(cancellationToken);
        report = await ProbeAsync(policy, currentSsid, diagnostics, cancellationToken);
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

        var currentSsid = await _wifi.GetCurrentSsidAsync(cancellationToken).WaitAsync(cancellationToken);
        var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(cancellationToken).WaitAsync(cancellationToken);
        var reportAfter = await ProbeAsync(policy, currentSsid, diagnostics, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (reportAfter.Ok)
        {
            _failedRecoveryRounds = 0;
            _failureStreak = 0;
        }

        Raise(EngineEventKind.HighRiskActionCompleted,
            action, commandResult.Ok, reportAfter.Ok, reportBefore.State.ToString(), reportAfter.State.ToString());
        return reportAfter.Ok || !RecoveryPlanner.IsDeviceLayerFailure(reportAfter.State);
    }

    private bool IsSsidCoolingDown(string ssid) =>
        _failedSsids.TryGetValue(ssid.Trim(), out var until) && _time.GetUtcNow() < until;

    private void RecordProfileResult(MonitorPolicy policy, string ssid, bool succeeded)
    {
        if (succeeded) _failedSsids.Remove(ssid.Trim());
        else _failedSsids[ssid.Trim()] = _time.GetUtcNow().AddSeconds(Math.Max(0, policy.RecoveryCooldownSeconds));
    }

    private async Task<bool> ConnectAndValidateAsync(MonitorPolicy policy, string ssid, TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await _wifi.ConnectAsync(ssid, token).WaitAsync(token))
        {
            Raise(EngineEventKind.CheckError, $"连接网络“{ssid}”的请求失败：{_wifi.LastConnectionFailure ?? "系统未提供原因。"}");
            return false;
        }
        using var deadline = new CancellationTokenSource(timeout);
        using var validation = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var validationToken = validation.Token;
        try
        {
            if (!await WaitForSsidAsync(ssid, timeout, validationToken)) return false;
            while (true)
            {
                var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(validationToken).WaitAsync(validationToken);
                // 公网探测保留独立预算，不能被已结束的地址等待预算截断。
                var report = await ProbeAsync(policy, ssid, diagnostics, token);
                token.ThrowIfCancellationRequested();
                if (report.Ok) return true;
                // 关联先于 DHCP 完成；仅地址未就绪允许等待，已确认的上游故障继续换网。
                if (report.State != NetworkState.Disconnected)
                {
                    Raise(EngineEventKind.CheckError, $"网络“{ssid}”连接后校验失败：{report.State}，{report.Summary}");
                    return false;
                }
                await DelayAsync(SsidPollInterval > TimeSpan.Zero ? SsidPollInterval : TimeSpan.FromMilliseconds(1), validationToken);
                var actualSsid = await _wifi.GetCurrentSsidAsync(validationToken).WaitAsync(validationToken);
                if (!RecoveryPlanner.SsidEquals(actualSsid, ssid)) return false;
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            Raise(EngineEventKind.CheckError, $"网络“{ssid}”在 {timeout.TotalSeconds:g} 秒内未完成关联或地址配置。");
            return false;
        }
    }

    private async Task<ConnectivityReport> ProbeAsync(MonitorPolicy policy, string? ssid,
        WifiAdapterDiagnostics diagnostics, CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(FullProbeTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        try
        {
            return await _tester.RunFullCheckAsync(policy, ssid, diagnostics, linked.Token).WaitAsync(linked.Token);
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

    private async Task<bool> WaitForSsidAsync(string ssid, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = _time.GetLocalNow().Add(timeout);
        while (_time.GetLocalNow() < deadline)
        {
            var currentSsid = await _wifi.GetCurrentSsidAsync(cancellationToken).WaitAsync(cancellationToken);
            if (RecoveryPlanner.SsidEquals(currentSsid, ssid))
            {
                return true;
            }

            await DelayAsync(SsidPollInterval, cancellationToken);
        }

        return false;
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
