using System.Text;

namespace NetGuardian.Core.Notifications;

/// <summary>远程通知渠道类型。</summary>
public enum NotificationChannelType
{
    GenericWebhook = 0,
    WeCom = 1,
    Feishu = 2,
    DingTalk = 3
}

/// <summary>告警通知事件类型。</summary>
public enum AlertEventKind
{
    OfflineDetected,
    RecoverySucceeded,
    RecoveryFailed,
    HighRiskActionTriggered,
    PrimaryFailbackSucceeded,
    Test
}

/// <summary>待发送或实时发送的通知负载。</summary>
public sealed record AlertNotificationMessage(
    string Id,
    AlertEventKind EventKind,
    string Title,
    string Body,
    string? Ssid,
    DateTimeOffset CreatedAtUtc,
    int AttemptCount = 0);

/// <summary>远程告警通知配置。</summary>
public sealed class NotificationSettings
{
    public bool Enabled { get; set; }
    public NotificationChannelType Channel { get; set; } = NotificationChannelType.GenericWebhook;
    public string WebhookUrl { get; set; } = string.Empty;
    public bool NotifyOnOffline { get; set; } = true;
    public bool NotifyOnRecovered { get; set; } = true;
    public bool NotifyOnRecoveryFailed { get; set; } = true;
    public bool NotifyOnHighRisk { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 8;
    public int MaxQueuedItems { get; set; } = 20;

    public void Normalize()
    {
        if (!Enum.IsDefined(Channel))
        {
            Channel = NotificationChannelType.GenericWebhook;
        }

        WebhookUrl = (WebhookUrl ?? string.Empty).Trim();
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 3, 30);
        MaxQueuedItems = Math.Clamp(MaxQueuedItems, 5, 50);
    }

    public bool IsConfigured()
    {
        return Enabled
            && Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    public bool ShouldNotify(AlertEventKind kind)
    {
        if (kind == AlertEventKind.Test)
        {
            return true;
        }

        if (!IsConfigured())
        {
            return false;
        }

        return kind switch
        {
            AlertEventKind.OfflineDetected => NotifyOnOffline,
            AlertEventKind.RecoverySucceeded or AlertEventKind.PrimaryFailbackSucceeded => NotifyOnRecovered,
            AlertEventKind.RecoveryFailed => NotifyOnRecoveryFailed,
            AlertEventKind.HighRiskActionTriggered => NotifyOnHighRisk,
            _ => false
        };
    }

    /// <summary>对 Webhook URL 中的路径密钥段、query 参数和 userinfo 进行脱敏。</summary>
    public static string RedactWebhookUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return string.Empty;
        }

        var trimmed = rawUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return MaskSecretToken(trimmed);
        }

        var sb = new StringBuilder();
        sb.Append(uri.Scheme).Append("://");
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            sb.Append("***@");
        }

        sb.Append(uri.Host);
        if (!uri.IsDefaultPort)
        {
            sb.Append(':').Append(uri.Port);
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0)
        {
            for (var i = 0; i < segments.Length; i++)
            {
                sb.Append('/');
                var seg = segments[i];
                // 对最后一段或疑似随机令牌长串（>= 16 个字符或位于 hook/bot/v2 之后）做掩码。
                var isLastSecret = i == segments.Length - 1 && seg.Length >= 8;
                var isLongToken = seg.Length >= 16;
                sb.Append(isLastSecret || isLongToken ? MaskSecretToken(seg) : seg);
            }
        }
        else
        {
            sb.Append('/');
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            var rawQuery = uri.Query.TrimStart('?');
            var pairs = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
            sb.Append('?');
            for (var i = 0; i < pairs.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append('&');
                }

                var eqIdx = pairs[i].IndexOf('=');
                if (eqIdx < 0)
                {
                    sb.Append(pairs[i]);
                    continue;
                }

                var key = pairs[i][..eqIdx];
                var val = pairs[i][(eqIdx + 1)..];
                if (IsSensitiveQueryParam(key))
                {
                    sb.Append(key).Append('=').Append(MaskSecretToken(val));
                }
                else
                {
                    sb.Append(key).Append('=').Append(val);
                }
            }
        }

        return sb.ToString();
    }

    private static bool IsSensitiveQueryParam(string key)
    {
        return key.Contains("key", StringComparison.OrdinalIgnoreCase)
            || key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("sign", StringComparison.OrdinalIgnoreCase)
            || key.Contains("auth", StringComparison.OrdinalIgnoreCase);
    }

    private static string MaskSecretToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return "***";
        }
        if (token.Length <= 6)
        {
            return "***";
        }
        return $"{token[..2]}***{token[^2..]}";
    }
}
