namespace NetGuardian.Core.Models;

/// <summary>记录 HTTP 探测得到的线索。线索需要与已验证的配置核对后才能用于登录。</summary>
public enum PortalEvidence
{
    None,
    Redirect,
    Html,
    AuthenticationRequired
}

/// <summary>单次门户线索探测结果，仅在内存中保留完整入口地址。</summary>
public sealed record PortalDiscoveryResult(
    PortalEvidence Evidence,
    Uri? EntryUri,
    string? SanitizedEntry)
{
    public static PortalDiscoveryResult Empty { get; } = new(PortalEvidence.None, null, null);

    /// <summary>提取不含查询参数、片段与用户凭据的脱敏路径，供日志与状态展示。</summary>
    public static string? SanitizeUri(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri) return null;
        if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) return null;
        var builder = new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port, uri.AbsolutePath);
        return builder.Uri.GetLeftPart(UriPartial.Path);
    }
}
