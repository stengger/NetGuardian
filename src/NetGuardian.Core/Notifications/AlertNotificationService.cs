using System.Net.Http;
using System.Text;
using System.Text.Json;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Runtime;

namespace NetGuardian.Core.Notifications;

/// <summary>单次通知发送结果。</summary>
public sealed record NotificationDispatchResult(
    bool Succeeded,
    bool QueuedForRetry,
    string? ErrorMessage);

/// <summary>负责构建通知负载、发送远程 Webhook 以及维护磁盘离线重试队列。</summary>
public sealed class AlertNotificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _queueFilePath;
    private readonly ILogSink? _log;
    private readonly Func<string, string, int, CancellationToken, Task<(bool Ok, string? Error)>> _transport;
    private readonly object _queueLock = new();

    public AlertNotificationService(
        string? dataDirectory = null,
        ILogSink? log = null,
        HttpMessageHandler? httpHandler = null,
        Func<string, string, int, CancellationToken, Task<(bool Ok, string? Error)>>? transport = null)
    {
        var baseDir = string.IsNullOrWhiteSpace(dataDirectory) ? GuardianPaths.DefaultDirectory : dataDirectory;
        _queueFilePath = Path.Combine(baseDir, "alerts", "pending-notifications.json");
        _log = log;
        _transport = transport ?? ((url, json, timeoutSec, ct) => PostJsonAsync(url, json, timeoutSec, httpHandler, ct));
    }

    public string QueueFilePath => _queueFilePath;

    /// <summary>读取当前持久化等待补发的通知队列快照。</summary>
    public IReadOnlyList<AlertNotificationMessage> GetPendingSnapshot()
    {
        lock (_queueLock)
        {
            return LoadQueueUnsafe();
        }
    }

    /// <summary>构建符合各渠道约定的 JSON 负载文本。</summary>
    public static string BuildPayloadJson(NotificationChannelType channel, AlertNotificationMessage message)
    {
        var timeText = message.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        var ssidLine = string.IsNullOrWhiteSpace(message.Ssid) ? "-" : message.Ssid;

        return channel switch
        {
            NotificationChannelType.WeCom => JsonSerializer.Serialize(new
            {
                msgtype = "markdown",
                markdown = new
                {
                    content = $"**{message.Title}**\n> 事件: {message.EventKind}\n> SSID: {ssidLine}\n> 时间: {timeText}\n\n{message.Body}"
                }
            }),
            NotificationChannelType.Feishu => JsonSerializer.Serialize(new
            {
                msg_type = "text",
                content = new
                {
                    text = $"[NetGuardian] {message.Title}\n事件: {message.EventKind}\nSSID: {ssidLine}\n时间: {timeText}\n{message.Body}"
                }
            }),
            NotificationChannelType.DingTalk => JsonSerializer.Serialize(new
            {
                msgtype = "text",
                text = new
                {
                    content = $"[NetGuardian] {message.Title}\n事件: {message.EventKind}\nSSID: {ssidLine}\n时间: {timeText}\n{message.Body}"
                }
            }),
            _ => JsonSerializer.Serialize(new
            {
                app = "NetGuardian",
                id = message.Id,
                @event = message.EventKind.ToString(),
                title = message.Title,
                message = message.Body,
                ssid = message.Ssid,
                timestampUtc = message.CreatedAtUtc
            })
        };
    }

    /// <summary>发送业务告警通知；如果发送失败且非测试消息，则持久化写入离线队列等待联网后补发。</summary>
    public async Task<NotificationDispatchResult> NotifyAsync(
        NotificationSettings settings,
        AlertNotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        settings.Normalize();
        if (!settings.ShouldNotify(message.EventKind))
        {
            return new NotificationDispatchResult(false, false, "Notification rule disabled or unconfigured.");
        }

        if (!settings.IsConfigured())
        {
            return new NotificationDispatchResult(false, false, "Webhook URL is missing or invalid.");
        }

        var payload = BuildPayloadJson(settings.Channel, message);
        var (ok, error) = await _transport(settings.WebhookUrl, payload, settings.TimeoutSeconds, cancellationToken);
        if (ok)
        {
            _log?.Write($"[Notify] Sent {message.EventKind} via {settings.Channel} ({NotificationSettings.RedactWebhookUrl(settings.WebhookUrl)}).");
            return new NotificationDispatchResult(true, false, null);
        }

        var safeError = SanitizeError(error, settings.WebhookUrl);
        if (message.EventKind != AlertEventKind.Test)
        {
            EnqueueUnsafeWithLock(message with { AttemptCount = message.AttemptCount + 1 }, settings.MaxQueuedItems);
            _log?.Write($"[Notify] Send failed ({safeError}); queued {message.EventKind} for retry.");
            return new NotificationDispatchResult(false, true, safeError);
        }

        return new NotificationDispatchResult(false, false, safeError);
    }

    /// <summary>专门用于 UI“测试发送”按钮：不限制 Enabled 开关，只校验 URL 有效性，不写入重试队列。</summary>
    public async Task<NotificationDispatchResult> SendTestAsync(
        NotificationSettings settings,
        string? currentSsid = null,
        CancellationToken cancellationToken = default)
    {
        settings.Normalize();
        if (!Uri.TryCreate(settings.WebhookUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return new NotificationDispatchResult(false, false, "Invalid http/https Webhook URL.");
        }

        var testMsg = new AlertNotificationMessage(
            Guid.NewGuid().ToString("N"),
            AlertEventKind.Test,
            "NetGuardian 测试告警 / Test Alert",
            "这是一条来自 NetGuardian 的远程告警连通性测试消息。",
            currentSsid,
            DateTimeOffset.UtcNow);

        var payload = BuildPayloadJson(settings.Channel, testMsg);
        var (ok, error) = await _transport(settings.WebhookUrl, payload, settings.TimeoutSeconds, cancellationToken);
        return ok
            ? new NotificationDispatchResult(true, false, null)
            : new NotificationDispatchResult(false, false, SanitizeError(error, settings.WebhookUrl));
    }

    /// <summary>在网络恢复在线后补发离线期间积压的告警。</summary>
    public async Task<int> FlushPendingAsync(
        NotificationSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings.Normalize();
        if (!settings.IsConfigured())
        {
            return 0;
        }

        List<AlertNotificationMessage> snapshot;
        lock (_queueLock)
        {
            snapshot = LoadQueueUnsafe();
        }

        if (snapshot.Count == 0)
        {
            return 0;
        }

        var deliveredIds = new HashSet<string>(StringComparer.Ordinal);
        var updatedAttempts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in snapshot)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var payload = BuildPayloadJson(settings.Channel, item);
            var (ok, _) = await _transport(settings.WebhookUrl, payload, settings.TimeoutSeconds, cancellationToken);
            if (ok)
            {
                deliveredIds.Add(item.Id);
            }
            else
            {
                updatedAttempts[item.Id] = item.AttemptCount + 1;
                // 如果刚刚首条补发即失败，说明链路尚未完全畅通，直接中止本次补发避免卡住主循环。
                break;
            }
        }

        if (deliveredIds.Count == 0 && updatedAttempts.Count == 0)
        {
            return 0;
        }

        lock (_queueLock)
        {
            var latest = LoadQueueUnsafe();
            var remaining = new List<AlertNotificationMessage>(latest.Count);
            foreach (var item in latest)
            {
                if (deliveredIds.Contains(item.Id))
                {
                    continue;
                }
                if (updatedAttempts.TryGetValue(item.Id, out var attempts))
                {
                    remaining.Add(item with { AttemptCount = attempts });
                }
                else
                {
                    remaining.Add(item);
                }
            }
            SaveQueueUnsafe(remaining);
        }

        if (deliveredIds.Count > 0)
        {
            _log?.Write($"[Notify] Flushed {deliveredIds.Count} queued offline alert(s).");
        }
        return deliveredIds.Count;
    }

    private void EnqueueUnsafeWithLock(AlertNotificationMessage message, int maxItems)
    {
        lock (_queueLock)
        {
            var items = LoadQueueUnsafe();
            items.Add(message);
            if (items.Count > maxItems)
            {
                items.RemoveRange(0, items.Count - maxItems);
            }
            SaveQueueUnsafe(items);
        }
    }

    private List<AlertNotificationMessage> LoadQueueUnsafe()
    {
        if (!File.Exists(_queueFilePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_queueFilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<AlertNotificationMessage>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveQueueUnsafe(List<AlertNotificationMessage> items)
    {
        var dir = Path.GetDirectoryName(_queueFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (items.Count == 0)
        {
            if (File.Exists(_queueFilePath))
            {
                File.Delete(_queueFilePath);
            }
            return;
        }

        var tmp = _queueFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(items, JsonOptions), Encoding.UTF8);
            File.Move(tmp, _queueFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }

    private static string SanitizeError(string? error, string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "Unknown error";
        }
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return error;
        }
        return error.Replace(rawUrl, NotificationSettings.RedactWebhookUrl(rawUrl), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(bool Ok, string? Error)> PostJsonAsync(
        string url,
        string jsonPayload,
        int timeoutSeconds,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = handler is null
                ? new HttpClient()
                : new HttpClient(handler, disposeHandler: false);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 3, 30));
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            return (false, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return (false, ex.Message);
        }
    }
}
