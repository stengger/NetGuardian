using System;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Runtime;

namespace NetGuardian.App.Presentation;

internal sealed record MonitorStatusPresentation(string ActivityText, string OwnerText, string Hint, string BrushKey,
    bool CanStart, bool CanStop, bool HasBackground)
{
    public static MonitorStatusPresentation Create(bool monitoring, bool checking, GuardianStatus? background, DateTimeOffset now, Localizer localizer)
    {
        string T(string key) => localizer.T(key);
        var age = background is null ? TimeSpan.MaxValue : now - background.HeartbeatAt;
        if (!monitoring && background is { State: not "Stopped" } && age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(30))
            return new(T(background.IsChecking ? "BackgroundChecking" : "BackgroundMonitoring"), T("BackgroundMonitoring"),
                T("BackgroundControlHint"), background.IsChecking ? "StatusWarningBrush" : "StatusHealthyBrush", false, false, true);
        return new(T(checking ? "Checking" : monitoring ? "Monitoring" : "Stopped"), T(monitoring ? "Monitoring" : "Stopped"),
            T(monitoring ? "ForegroundMonitoringHint" : "MonitoringIdleHint"),
            checking ? "StatusWarningBrush" : monitoring ? "StatusHealthyBrush" : "TextFillColorSecondaryBrush", !monitoring && !checking, monitoring, false);
    }
}
