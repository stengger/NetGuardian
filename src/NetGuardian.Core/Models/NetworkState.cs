namespace NetGuardian.Core.Models;

public enum NetworkState
{
    Healthy,
    AdapterFault,
    WifiDisabled,
    WifiDisconnected,
    Disconnected,
    GatewayIssue,
    DnsIssue,
    InternetIssue,
    Recovering,
    Failed
}
