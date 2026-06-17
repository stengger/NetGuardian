namespace NetGuardian.Core.Models;

public enum NetworkState
{
    Healthy,
    WifiDisabled,
    WifiDisconnected,
    Disconnected,
    GatewayIssue,
    DnsIssue,
    InternetIssue,
    Recovering,
    Failed
}
