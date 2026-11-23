namespace NetGuardian.Core.Models;

/// <summary>记录 HTTP 探测得到的线索。线索需要与已验证的配置核对后才能用于登录。</summary>
public enum PortalEvidence
{
    None,
    Redirect,
    Html,
    AuthenticationRequired
}
