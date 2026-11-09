using System;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Runtime;

namespace NetGuardian.App.Presentation;

internal sealed record MonitorStatusPresentation(string ActivityText, string OwnerText, string Hint, string BrushKey,
    bool CanStart, bool CanStop, bool HasBackground, bool CanCheck)
{
    public static MonitorStatusPresentation Create(bool monitoring, bool checking, GuardianStatus? background, DateTimeOffset now,
        Localizer localizer, bool manualOperation = false)
    {
        string T(string key) => localizer.T(key);
        if (manualOperation)
            return new(T("ManualNetworkRunning"), T("ManualNetworkRunning"), T("ManualNetworkControlHint"),
                "StatusWarningBrush", false, false, false, false);
        var age = background is null ? TimeSpan.MaxValue : now - background.HeartbeatAt;
        if (!monitoring && background?.State == "ManualControl" && age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(30))
            return new(T("BackgroundManualControl"), T("BackgroundManualControl"), T("ManualNetworkControlHint"),
                "StatusWarningBrush", false, false, true, false);
        if (!monitoring && background is { State: not "Stopped" } && age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(30))
            return new(T(background.IsChecking ? "BackgroundChecking" : "BackgroundMonitoring"), T("BackgroundMonitoring"),
                T("BackgroundControlHint"), background.IsChecking ? "StatusWarningBrush" : "StatusHealthyBrush", false, false, true, !checking && !background.IsChecking);
        return new(T(checking ? "Checking" : monitoring ? "Monitoring" : "Stopped"), T(monitoring ? "Monitoring" : "Stopped"),
            T(monitoring ? "ForegroundMonitoringHint" : "MonitoringIdleHint"),
            checking ? "StatusWarningBrush" : monitoring ? "StatusHealthyBrush" : "TextFillColorSecondaryBrush", !monitoring && !checking, monitoring, false, !checking);
    }
}
