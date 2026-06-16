namespace NetGuardian.Core.Models;

public enum NetworkState
{
    Healthy,
    Disconnected,
    GatewayIssue,
    DnsIssue,
    InternetIssue,
    Recovering,
    Failed
}

