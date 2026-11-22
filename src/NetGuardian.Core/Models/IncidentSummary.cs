using System.Text.Json.Serialization;

namespace NetGuardian.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<IncidentStepKind>))]
public enum IncidentStepKind
{
    Failure, PublicVerified, Inconclusive, RoundStarted, SwitchAttempt, ReconnectAttempt,
    WifiAssociated, ActionStarted, ActionResult, Boundary, LegacyRestored
}

public enum IncidentOutcome { Open, NaturalRecovery, RecoveredAfterAction, CauseUnknown }

/// <summary>一个真实观测点。事件标识在采集时生成，所有视图和重读共用。</summary>
public sealed record IncidentStep(string Id, DateTimeOffset At, IncidentStepKind Kind, string Actor,
    string Session, string? Target = null, string? Detail = null, IReadOnlyList<ConnectivityProbe>? Evidence = null,
    bool AfterAction = false, int MaxSilenceSeconds = 0)
{
    [JsonIgnore] public string? RawLog { get; init; }
}

public sealed record IncidentSummary(string Id, DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    string? OriginalNetwork, IncidentOutcome Outcome, TimeSpan ObservedDuration, bool HasObservationGap,
    bool IsLegacy, IReadOnlyList<IncidentStep> Steps)
{
    public int SwitchAttempts => Steps.Count(s => s.Kind == IncidentStepKind.SwitchAttempt);
    public int WifiAssociations => Steps.Count(s => s.Kind == IncidentStepKind.WifiAssociated);
    public int VerifiedRecoveries => Steps.Count(s => s.Kind == IncidentStepKind.PublicVerified);
}
