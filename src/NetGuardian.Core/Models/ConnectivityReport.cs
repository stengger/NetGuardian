using System.Text.Json.Serialization;

namespace NetGuardian.Core.Models;

public sealed class ConnectivityReport
{
    public string? NetworkContext { get; init; }
    public DateTimeOffset? SampleStartedAt { get; init; }
    public bool IsLightweight { get; init; }
    public NetworkState State { get; init; }
    public PortalEvidence PortalEvidence { get; init; }
    public string? PortalSanitizedOrigin { get; init; }

    /// <summary>仅在内存保存当次探测得到的门户入口地址，绝不序列化到磁盘状态或日志。</summary>
    [JsonIgnore]
    public Uri? PortalEntryUri { get; init; }

    [JsonIgnore]
    public int? WifiInterfaceIndex { get; init; }

    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;
    public string? CurrentSsid { get; init; }
    public string? Ipv4 { get; init; }
    public string? Gateway { get; init; }
    public List<ConnectivityProbe> Probes { get; init; } = [];
    public string Summary { get; init; } = "";
    public bool Ok => State == NetworkState.Healthy;
}
