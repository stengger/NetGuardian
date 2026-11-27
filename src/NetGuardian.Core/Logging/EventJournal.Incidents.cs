using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;

namespace NetGuardian.Core.Logging;

public sealed partial class EventJournal
{
    private bool _observesIncidents;

    /// <summary>只订阅本进程持有控制权的引擎。界面导入心跳和历史时无需再次写日志。</summary>
    public void ObserveIncidents(MonitoringEngine engine, string actor)
    {
        lock (_syncRoot)
        {
            if (_observesIncidents) throw new InvalidOperationException("日志已绑定监控引擎。");
            _observesIncidents = true;
        }
        var session = Guid.NewGuid().ToString("N");
        var healthy = false;
        var observing = 0;
        void Write(IncidentStepKind kind, string? target, string? detail, ConnectivityReport? report, bool afterAction)
        {
            lock (_syncRoot)
            {
                if (kind == IncidentStepKind.PublicVerified && healthy) return;
                healthy = kind == IncidentStepKind.PublicVerified;
                var now = _time.GetLocalNow();
                var step = new IncidentStep(Guid.NewGuid().ToString("N"), now, kind, actor, session, target, detail,
                    report?.Probes, afterAction, engine.ObservationBudgetSeconds);
                Append(new JournalRecord { Timestamp = now, Type = "incident", Incident = step });
            }
        }
        engine.IncidentObserved += Write;
        engine.ProfileRecoveryResult += (_, e) =>
        {
            // 成功在相同接口的公网复测点已经入账，失败仍需记录命令拒绝、超时等结果。
            if (!e.Succeeded) Write(IncidentStepKind.ActionResult, e.Ssid, "连接或公网验证失败", null, false);
        };
        engine.EngineEvent += (_, e) =>
        {
            if (e.Kind == EngineEventKind.MonitorStarted) Interlocked.Exchange(ref observing, 1);
            if (e.Kind == EngineEventKind.MonitorStopped && Interlocked.Exchange(ref observing, 0) == 0) return;
            string? Arg(int n) => e.Args.Count > n ? e.Args[n]?.ToString() : null;
            (IncidentStepKind? Kind, string? Target, string? Detail) entry = e.Kind switch
            {
                EngineEventKind.MonitorStarted => (IncidentStepKind.Boundary, null, "监控开始"),
                EngineEventKind.MonitorStopped => (IncidentStepKind.Boundary, null, "监控停止"),
                EngineEventKind.RecoveryStarted => (IncidentStepKind.RoundStarted, null, $"已达到恢复阈值，连续失败 {Arg(0)} 次"),
                EngineEventKind.RecoveryWifiSwitchSummary => (IncidentStepKind.ActionResult, null,
                    $"{Arg(0)}，配置 {Arg(1)} 条，候选 {Arg(2)} 条，跳过 {Arg(3)} 条，尝试 {Arg(4)} 次。{Arg(5)}"),
                EngineEventKind.RecoverySwitchWifi => (IncidentStepKind.SwitchAttempt, Arg(0), "尝试切换 Wi-Fi"),
                EngineEventKind.RecoveryReconnectCurrent => (IncidentStepKind.ReconnectAttempt, Arg(0), "尝试重连当前网络"),
                EngineEventKind.RecoveryDnsFlush => (IncidentStepKind.ActionStarted, "DNS", "刷新 DNS 缓存"),
                EngineEventKind.RecoveryDnsFlushCompleted => (IncidentStepKind.ActionResult, "DNS", $"刷新 DNS，退出码 {Arg(1)}"),
                EngineEventKind.RecoveryPortalAuthStarted => (IncidentStepKind.ActionStarted, Arg(0), $"尝试 Wi-Fi 网页认证（{Arg(1) ?? "已验证门户"}）"),
                EngineEventKind.RecoveryPortalAuthCompleted => (IncidentStepKind.ActionResult, Arg(0), $"网页认证结果：{Arg(1)} · {Arg(2)}"),
                EngineEventKind.RecoveryPortalAuthPaused => (IncidentStepKind.ActionResult, Arg(0), $"已暂停自动认证：{Arg(1)}"),
                EngineEventKind.TryingEnableWifiAdapter => (IncidentStepKind.ActionStarted, Arg(0), "尝试启用无线网卡"),
                EngineEventKind.WifiAdapterEnabled => (IncidentStepKind.ActionResult, null, "已启用无线网卡"),
                EngineEventKind.WifiAdapterEnableFailed => (IncidentStepKind.ActionResult, null, "启用无线网卡失败"),
                EngineEventKind.HighRiskActionStarted => (IncidentStepKind.ActionStarted, Arg(0), "尝试重启网络组件"),
                EngineEventKind.RecoveryNoActionsEnabled => (IncidentStepKind.ActionResult, null, "本轮没有可执行动作"),
                EngineEventKind.RecoveryFailed => (IncidentStepKind.ActionResult, null, "恢复轮次结束，公网尚未验证恢复"),
                _ => ((IncidentStepKind?)null, null, null)
            };
            if (entry.Kind is { } kind) Write(kind, entry.Target, entry.Detail, null, false);
        };
    }
}
