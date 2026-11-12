using System;
using System.Collections.Generic;
using System.Linq;

namespace NetGuardian.App.Presentation;

// 队列只管理展示生命周期；计时由呈现器提供，不触及网络操作。
internal sealed class FeedbackQueue(int capacity, TimeSpan normalTimeout, TimeSpan warningTimeout)
{
    private readonly List<FeedbackMessage> _messages = [];
    internal event Action? Changed;
    internal IReadOnlyList<FeedbackMessage> Visible => _messages.Take(capacity).ToArray();

    internal FeedbackMessage Enqueue(string title, string body, string severity, string? actionText, Action? action, string? identity)
    {
        var timeout = severity is "Danger" or "Error" ? (TimeSpan?)null
            : severity == "Warning" ? warningTimeout : normalTimeout;
        // 没有稳定操作身份的回调不合并，避免错误执行另一条提示的操作。
        var existing = _messages.FirstOrDefault(m => m.Title == title && m.Body == body && m.Severity == severity
            && m.ActionText == actionText && m.Identity == identity && (action is null && m.Action is null || identity is not null));
        if (existing is not null)
        {
            existing.Count++;
            existing.Remaining = timeout;
            Changed?.Invoke();
            return existing;
        }
        var message = new FeedbackMessage(title, body, severity, actionText, action, identity, timeout);
        _messages.Add(message);
        Changed?.Invoke();
        return message;
    }

    internal void Dismiss(FeedbackMessage message)
    {
        if (_messages.Remove(message)) Changed?.Invoke();
    }

    internal void Advance(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero) return;
        var expired = new List<FeedbackMessage>();
        foreach (var message in Visible)
        {
            if (message.Paused || message.Remaining is null) continue;
            message.Remaining -= elapsed;
            if (message.Remaining <= TimeSpan.Zero) expired.Add(message);
        }
        if (expired.Count == 0) return;
        foreach (var message in expired) _messages.Remove(message);
        Changed?.Invoke();
    }
}

internal sealed class FeedbackMessage(string title, string body, string severity, string? actionText,
    Action? action, string? identity, TimeSpan? remaining)
{
    internal string Title { get; } = title;
    internal string Body { get; } = body;
    internal string Severity { get; } = severity;
    internal string? ActionText { get; } = actionText;
    internal Action? Action { get; } = action;
    internal string? Identity { get; } = identity;
    internal TimeSpan? Remaining { get; set; } = remaining;
    internal int Count { get; set; } = 1;
    internal bool Paused { get; set; }
}
