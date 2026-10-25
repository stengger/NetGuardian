namespace NetGuardian.Core.Models;

public sealed class ConnectivityReport
{
    public bool IsLightweight { get; init; }
    public NetworkState State { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;
    public string? CurrentSsid { get; init; }
    public string? Ipv4 { get; init; }
    public string? Gateway { get; init; }
    public List<ConnectivityProbe> Probes { get; init; } = [];
    public string Summary { get; init; } = "";
    public bool Ok => State == NetworkState.Healthy;
}

