using System.Text.Json.Serialization;

namespace NetGuardian.Core.Models;

public enum PortalAuthMode
{
    CredentialsForm,
    OneClickConsent
}

public enum PortalFillStrategy
{
    PlaceholderSubstitution,
    ScopedBrowserFill
}

public sealed class PortalFieldSelection
{
    public string Selector { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>单个 Wi-Fi 配置的网页认证白名单与控件定位配置，绝不包含账户名或密码。</summary>
public sealed class PortalAuthConfig
{
    public bool Enabled { get; set; }
    public bool Ready { get; set; }
    public bool Paused { get; set; }
    public string? PauseReason { get; set; }
    public string? CredentialId { get; set; }
    public PortalAuthMode Mode { get; set; } = PortalAuthMode.CredentialsForm;
    public PortalFillStrategy FillStrategy { get; set; } = PortalFillStrategy.PlaceholderSubstitution;

    /// <summary>默认严格要求 HTTPS；仅在向导由用户明确确认内网 HTTP 门户时允许为 true。</summary>
    public bool AllowInsecureHttp { get; set; }

    public string AllowedOrigin { get; set; } = "";
    public string AllowedLoginPath { get; set; } = "/";
    public string? AllowedSubmitOrigin { get; set; }
    public string AllowedSubmitPath { get; set; } = "";
    public string? UsernameSelector { get; set; }
    public string? PasswordSelector { get; set; }
    public string SubmitSelector { get; set; } = "";
    public string? UsernameFieldName { get; set; }
    public string? PasswordFieldName { get; set; }
    public List<PortalFieldSelection> FixedSelectValues { get; set; } = [];
    public DateTimeOffset? LastAuthenticatedAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastFailureSummary { get; set; }

    [JsonIgnore]
    public bool CanAutoAuthenticate => Enabled && Ready && !Paused && IsConfigurationValid();

    public bool IsConfigurationValid()
    {
        if (!TryNormalizeOrigin(AllowedOrigin, AllowInsecureHttp, out _)) return false;
        if (!string.IsNullOrWhiteSpace(AllowedSubmitOrigin) && !TryNormalizeOrigin(AllowedSubmitOrigin, AllowInsecureHttp, out _))
            return false;
        if (string.IsNullOrWhiteSpace(SubmitSelector)) return false;
        if (Mode == PortalAuthMode.CredentialsForm)
        {
            if (string.IsNullOrWhiteSpace(UsernameSelector) || string.IsNullOrWhiteSpace(PasswordSelector))
                return false;
            if (!IsValidCredentialId(CredentialId))
                return false;
        }
        return true;
    }

    public bool MatchesEntryUri(Uri? uri)
    {
        if (!IsValidHttpUri(uri, AllowInsecureHttp)) return false;
        if (!TryNormalizeOrigin(AllowedOrigin, AllowInsecureHttp, out var expectedOrigin)) return false;
        if (!string.Equals(FormatOrigin(uri!), expectedOrigin, StringComparison.OrdinalIgnoreCase)) return false;
        return MatchesPath(uri!.AbsolutePath, AllowedLoginPath);
    }

    public bool MatchesSubmitUri(Uri? uri)
    {
        if (!IsValidHttpUri(uri, AllowInsecureHttp)) return false;
        var originText = string.IsNullOrWhiteSpace(AllowedSubmitOrigin) ? AllowedOrigin : AllowedSubmitOrigin;
        if (!TryNormalizeOrigin(originText, AllowInsecureHttp, out var expectedOrigin)) return false;
        if (!string.Equals(FormatOrigin(uri!), expectedOrigin, StringComparison.OrdinalIgnoreCase)) return false;
        var pathPattern = string.IsNullOrWhiteSpace(AllowedSubmitPath) ? AllowedLoginPath : AllowedSubmitPath;
        return MatchesPath(uri!.AbsolutePath, pathPattern);
    }

    public void Normalize()
    {
        PauseReason = TrimOrNull(PauseReason);
        CredentialId = TrimOrNull(CredentialId)?.ToLowerInvariant();
        if (TryNormalizeOrigin(AllowedOrigin, allowHttp: true, out var normalizedOrigin))
            AllowedOrigin = normalizedOrigin;
        else
            AllowedOrigin = AllowedOrigin?.Trim() ?? "";

        AllowedLoginPath = NormalizePath(AllowedLoginPath, defaultRoot: true);

        if (!string.IsNullOrWhiteSpace(AllowedSubmitOrigin)
            && TryNormalizeOrigin(AllowedSubmitOrigin, allowHttp: true, out var normalizedSubmitOrigin))
            AllowedSubmitOrigin = normalizedSubmitOrigin;
        else
            AllowedSubmitOrigin = TrimOrNull(AllowedSubmitOrigin);

        AllowedSubmitPath = string.IsNullOrWhiteSpace(AllowedSubmitPath) ? "" : NormalizePath(AllowedSubmitPath, defaultRoot: false);
        UsernameSelector = TrimOrNull(UsernameSelector);
        PasswordSelector = TrimOrNull(PasswordSelector);
        SubmitSelector = SubmitSelector?.Trim() ?? "";
        UsernameFieldName = TrimOrNull(UsernameFieldName);
        PasswordFieldName = TrimOrNull(PasswordFieldName);
        LastFailureSummary = TrimOrNull(LastFailureSummary);
        FixedSelectValues = (FixedSelectValues ?? [])
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Selector))
            .Select(item => new PortalFieldSelection { Selector = item.Selector.Trim(), Value = item.Value ?? "" })
            .ToList();
    }

    public static bool IsValidCredentialId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length == 32 && Guid.TryParseExact(id, "N", out _);

    public static bool TryNormalizeOrigin(string? raw, bool allowHttp, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return false;
        if (!IsValidHttpUri(uri, allowHttp)) return false;
        normalized = FormatOrigin(uri);
        return true;
    }

    public static string FormatOrigin(Uri uri) =>
        uri.IsDefaultPort
            ? $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}"
            : $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}:{uri.Port}";

    private static bool IsValidHttpUri(Uri? uri, bool allowHttp)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.UserInfo.Length > 0) return false;
        return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            || (allowHttp && uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPath(string actualPath, string configuredPath)
    {
        var actual = string.IsNullOrEmpty(actualPath) ? "/" : actualPath;
        var expected = NormalizePath(configuredPath, defaultRoot: true);
        if (expected == "/") return true;
        if (expected.EndsWith('*'))
        {
            var prefix = expected[..^1];
            return actual.StartsWith(prefix, StringComparison.Ordinal);
        }
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private static string NormalizePath(string? path, bool defaultRoot)
    {
        var trimmed = path?.Trim() ?? "";
        if (trimmed.Length == 0) return defaultRoot ? "/" : "";
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
