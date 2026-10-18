using System.Net;

namespace NetGuardian.Core.Connectivity;

/// <summary>系统 Wi-Fi 接口的只读快照。</summary>
public sealed record NetworkSnapshot(string? AdapterName, bool AdapterPresent, bool AdapterUp, string? Ipv4, string? Gateway);

public interface IConnectivityEnvironment
{
    NetworkSnapshot Capture();
}

/// <summary>仅封装外部网络 I/O，健康状态由 ConnectivityTester 判断。</summary>
public interface IConnectivityProbes
{
    Task<bool> PingAsync(string host, TimeSpan timeout, CancellationToken cancellationToken);
    Task ResolveAsync(string host, CancellationToken cancellationToken);
    Task<HttpStatusCode> HttpsHeadAsync(string host, CancellationToken cancellationToken);
}
