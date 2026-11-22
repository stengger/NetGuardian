using System;
using System.Collections.Generic;
using System.Linq;
using NetGuardian.Core.Models;
using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

public static class IncidentPresentation
{
    private const string IncidentPrefix = "incident:";

    internal static UiLogEntry HistoryEntry(IncidentSummary incident, Localizer localizer) =>
        new(incident.Steps[^1].At, HomeText(incident, localizer.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)),
            incident.EndedAt is null ? UiLogLevel.Warning : UiLogLevel.Information, UiLogSource.Recovery,
            localizer, persistedId: IncidentPrefix + incident.Id);

    internal static string? LinkedIncident(UiLogEntry? entry) => entry?.PersistedId is { } id && id.StartsWith(IncidentPrefix, StringComparison.Ordinal)
        ? id[IncidentPrefix.Length..] : null;

    public static string HomeText(IncidentSummary? incident, bool english = false)
    {
        if (incident is null) return english ? "No incidents recorded" : "暂无故障记录";
        var time = incident.StartedAt.ToLocalTime();
        if (english)
        {
            var result = incident.Outcome switch
            {
                IncidentOutcome.Open => "Awaiting internet verification", IncidentOutcome.NaturalRecovery => "Recovered naturally",
                IncidentOutcome.RecoveredAfterAction => "Internet verified after action", _ => "Recovered, cause unknown"
            };
            return $"{time:yyyy-MM-dd HH:mm:ss} · {incident.OriginalNetwork ?? "Unknown network"} · {result} · Observed {incident.ObservedDuration:c}";
        }
        return $"{time:yyyy-MM-dd HH:mm:ss} · {incident.OriginalNetwork ?? "原网络未知"} · {Result(incident)} · 观测 {Duration(incident.ObservedDuration)}";
    }

    private static string Result(IncidentSummary incident) => incident.Outcome switch
    {
        IncidentOutcome.Open => "等待公网恢复验证",
        IncidentOutcome.NaturalRecovery => "公网自然恢复",
        IncidentOutcome.RecoveredAfterAction => "恢复动作后公网验证通过",
        _ => incident.IsLegacy ? "旧日志记录恢复，原因未知" : "公网已恢复，原因未确认"
    };

    private static string Duration(TimeSpan time) => time.TotalDays >= 1 ? $"{(int)time.TotalDays} 天 {time.Hours} 小时 {time.Minutes} 分 {time.Seconds} 秒" :
        time.TotalHours >= 1 ? $"{time.Hours} 小时 {time.Minutes} 分 {time.Seconds} 秒" :
        time.TotalMinutes >= 1 ? $"{time.Minutes} 分 {time.Seconds} 秒" : $"{time.Seconds} 秒";

    internal static string Counts(IncidentSummary incident, bool english = false) => english
        ? incident.IsLegacy ? "Legacy log: action and public verification counts unknown" :
            $"Switch attempts: {incident.SwitchAttempts}    Wi-Fi associations: {incident.WifiAssociations}    Verified recoveries: {incident.VerifiedRecoveries}"
        : incident.IsLegacy
        ? "旧版日志仅保留当时记录的边界，动作与公网探针计数未知"
        : $"尝试切换 {incident.SwitchAttempts} 次　确认 Wi-Fi 关联 {incident.WifiAssociations} 次　公网验证恢复 {incident.VerifiedRecoveries} 次";

    internal static string Describe(IncidentStep step, bool english = false)
    {
        var text = step.Kind switch
        {
            IncidentStepKind.Failure => "完整诊断确认公网不可用",
            IncidentStepKind.PublicVerified => "公网验证通过",
            IncidentStepKind.Inconclusive => "验证结果未知",
            IncidentStepKind.WifiAssociated => "已确认 Wi-Fi 关联，等待公网复测",
            IncidentStepKind.LegacyRestored => "旧日志记录恢复",
            IncidentStepKind.Boundary => "期间缺少观测",
            _ => step.Detail ?? "执行恢复动作"
        };
        if (step.Session == "v1" && step.Kind == IncidentStepKind.Failure) text = "旧日志记录失联";
        var actor = step.Actor;
        if (english)
        {
            actor = step.Actor switch { "前台" => "Desktop", "后台" or "后台守护" => "Guardian", "旧版日志" => "Legacy", _ => actor };
            text = step.Kind switch
            {
                IncidentStepKind.Failure => step.Session == "v1" ? "Outage reported" : "Full diagnostic confirmed outage",
                IncidentStepKind.PublicVerified => "Internet verified", IncidentStepKind.WifiAssociated => "Wi-Fi association confirmed",
                IncidentStepKind.Inconclusive => "Verification inconclusive", IncidentStepKind.LegacyRestored => "Recovery reported",
                IncidentStepKind.Boundary => "Observation gap", IncidentStepKind.RoundStarted => "Recovery threshold reached",
                IncidentStepKind.SwitchAttempt => "Attempting Wi-Fi switch", IncidentStepKind.ReconnectAttempt => "Reconnecting current network",
                IncidentStepKind.ActionStarted => "Recovery action started", _ => "Recovery action result"
            };
        }
        var parts = new List<string> { $"{step.At.ToLocalTime():yyyy-MM-dd HH:mm:ss}", actor, text };
        if (!string.IsNullOrWhiteSpace(step.Target)) parts.Add(step.Target);
        if (!english && step.Kind == IncidentStepKind.Boundary && step.Detail != text) parts.Add(step.Detail ?? "");
        if (step.Evidence is { } evidence && evidence.Any(p => p.Name.StartsWith("HTTPS ", StringComparison.Ordinal)))
        {
            var https = evidence.Where(p => p.Name.StartsWith("HTTPS ", StringComparison.Ordinal)).ToList();
            parts.Add($"HTTPS {https.Count(p => p.Ok)}/{https.Count}");
        }
        return string.Join(" · ", parts);
    }
}
