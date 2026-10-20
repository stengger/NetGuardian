using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

internal static class PanelNavigationPresenter
{
    private static readonly (string Name, string TitleKey)[] Panels =
    [
        ("StatusPanel", "Status"), ("WifiPanel", "Wifi"), ("QualityPanel", "QualityTools"),
        ("ToolsPanel", "Tools"), ("DnsPanel", "DnsTools"), ("NetworkPanel", "AdapterTools"),
        ("PolicyPanel", "Policy"), ("LogPanel", "Log"), ("SettingsPanel", "Settings")
    ];

    public static void Show(Window window, string? tag, Localizer localizer)
    {
        var target = Panels.FirstOrDefault(panel => panel.Name == tag);
        if (target.Name is null) target = Panels[0];
        foreach (var panel in Panels)
            ((UIElement)window.FindName(panel.Name)).Visibility = panel.Name == target.Name ? Visibility.Visible : Visibility.Collapsed;
        ((TextBlock)window.FindName("PageTitleText")).Text = localizer.T(target.TitleKey);
        ((TextBlock)window.FindName("PageSubtitleText")).Text = target.Name == "StatusPanel" ? localizer.T("Summary") : "";
    }
}
