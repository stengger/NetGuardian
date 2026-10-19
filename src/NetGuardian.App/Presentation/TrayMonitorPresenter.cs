using NetGuardian.Core.Localization;
using Forms = System.Windows.Forms;

namespace NetGuardian.App.Presentation;

internal static class TrayMonitorPresenter
{
    public static void Apply(Forms.NotifyIcon tray, MonitorStatusPresentation state, Localizer localizer)
    {
        var text = $"NetGuardian - {state.ActivityText}";
        if (tray.Text != text) tray.Text = text;
        if (tray.ContextMenuStrip is not { } menu) return;
        foreach (var key in new[] { "ShowWindow", "CheckNow", "StartMonitor", "Stop", "Exit" })
            if (menu.Items[key] is { } item) item.Text = localizer.T(key);
        if (menu.Items["StartMonitor"] is { } start) { start.Enabled = state.CanStart; start.ToolTipText = state.Hint; }
        if (menu.Items["Stop"] is { } stop) { stop.Enabled = state.CanStop; stop.ToolTipText = state.Hint; }
        if (menu.Items["CheckNow"] is { } check) check.Enabled = state.CanCheck;
    }
}
