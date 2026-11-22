using System.Net;

namespace NetGuardian.Core.Connectivity;

/// <summary>系统 Wi-Fi 接口的只读快照。</summary>
public sealed record NetworkSnapshot(string? AdapterName, bool AdapterPresent, bool AdapterUp, string? Ipv4, string? Gateway, int? Ipv4InterfaceIndex = null,
    string? InterfaceId = null, string? DnsConfiguration = null)
{
    // 使用相同接口及配置关联轻量与完整检测，配置未知时禁止跨轮借用样本。
    public string? Context => AdapterUp && Ipv4InterfaceIndex.HasValue && !string.IsNullOrEmpty(InterfaceId)
        && !string.IsNullOrEmpty(Ipv4) && DnsConfiguration is not null
        ? $"{InterfaceId}|{Ipv4InterfaceIndex}|{Ipv4}|{Gateway}|{DnsConfiguration}" : null;
}

public interface IConnectivityEnvironment
{
    NetworkSnapshot Capture();
    NetworkSnapshot Capture(string? adapterName) => Capture();
}

/// <summary>仅封装外部网络 I/O，健康状态由 ConnectivityTester 判断。</summary>
public interface IConnectivityProbes
{
    Task<bool> PingAsync(string host, TimeSpan timeout, CancellationToken cancellationToken);
    Task ResolveAsync(string host, CancellationToken cancellationToken);
    Task<HttpStatusCode> HttpsHeadAsync(string host, CancellationToken cancellationToken);
    Task<HttpStatusCode> WifiHttpsHeadAsync(string host, IPAddress address, int interfaceIndex, CancellationToken cancellationToken) =>
        throw new HttpRequestException("探针不支持指定无线出口。");
}
