namespace NetGuardian.Core.Models;

public sealed record ConnectivityProbe(
    string Name,
    bool Ok,
    double? LatencyMs = null,
    string Detail = "");

