using NetGuardian.Core.Localization;
using NetGuardian.App.Views;
using System.Windows.Automation;
using Forms = System.Windows.Forms;

namespace NetGuardian.App.Presentation;

internal static class TrayMonitorPresenter
{
    public static void Apply(Forms.NotifyIcon tray, TrayContextMenu menu, MonitorStatusPresentation state, Localizer localizer)
    {
        var text = $"NetGuardian - {state.ActivityText}";
        if (tray.Text != text) tray.Text = text;
        menu.StatusText = state.ActivityText;
        AutomationProperties.SetHelpText(menu, state.Hint);
        foreach (var key in new[] { "ShowWindow", "CheckNow", "StartMonitor", "Stop", "Exit" })
            menu.GetCommand(key).Header = localizer.T(key);
        var start = menu.GetCommand("StartMonitor");
        start.IsEnabled = state.CanStart;
        start.ToolTip = state.Hint;
        AutomationProperties.SetHelpText(start, state.Hint);
        var stop = menu.GetCommand("Stop");
        stop.IsEnabled = state.CanStop;
        stop.ToolTip = state.Hint;
        AutomationProperties.SetHelpText(stop, state.Hint);
        var check = menu.GetCommand("CheckNow");
        check.IsEnabled = state.CanCheck;
        check.ToolTip = localizer.T(state.CanCheck ? "CheckNowTip" : "Checking");
    }
}
