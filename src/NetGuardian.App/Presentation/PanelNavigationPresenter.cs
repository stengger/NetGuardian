using System.Collections.Generic;
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

    private static readonly IReadOnlyDictionary<string, string> PageActions = new Dictionary<string, string>
    {
        ["WifiPanel"] = "WifiPageActions",
        ["QualityPanel"] = "QualityPageActions",
        ["ToolsPanel"] = "ToolsPageActions",
        ["DnsPanel"] = "DnsPageActions",
        ["NetworkPanel"] = "NetworkPageActions",
        ["PolicyPanel"] = "PolicyPageActions",
        ["LogPanel"] = "LogPageActions"
    };

    private static readonly IReadOnlyDictionary<string, string> ToolActions = new Dictionary<string, string>
    {
        ["QuickToolsTab"] = "ToolsPageActions",
        ["SpeedToolsTab"] = "SpeedPageActions",
        ["WebsiteToolsTab"] = "WebsitePageActions",
        ["IpInfoToolsTab"] = "IpInfoPageActions",
        ["ReportToolsTab"] = "ReportPageActions"
    };

    public static void Show(Window window, string? tag, Localizer localizer)
    {
        var target = Panels.FirstOrDefault(panel => panel.Name == tag);
        if (target.Name is null) target = Panels[0];
        foreach (var panel in Panels)
            ((UIElement)window.FindName(panel.Name)).Visibility = panel.Name == target.Name ? Visibility.Visible : Visibility.Collapsed;
        foreach (var actionName in PageActions.Values.Concat(ToolActions.Values).Distinct())
            ((UIElement)window.FindName(actionName)).Visibility = Visibility.Collapsed;
        var actionBar = (FrameworkElement)window.FindName("PageActionBar");
        PageActions.TryGetValue(target.Name, out var actionNameForPage);
        if (target.Name == "ToolsPanel" && window.FindName("ToolsTabControl") is TabControl tools)
        {
            var toolTabName = (tools.SelectedIndex < 0 ? 0 : tools.SelectedIndex) switch
            {
                0 => "QuickToolsTab",
                1 => "SpeedToolsTab",
                2 => "WebsiteToolsTab",
                3 => "IpInfoToolsTab",
                4 => "ReportToolsTab",
                _ => ""
            };
            actionNameForPage = ToolActions.TryGetValue(toolTabName, out var toolAction) ? toolAction : null;
        }
        actionBar.Visibility = actionNameForPage is not null ? Visibility.Visible : Visibility.Collapsed;
        if (actionNameForPage is not null)
            ((UIElement)window.FindName(actionNameForPage)).Visibility = Visibility.Visible;
        ((Button)window.FindName("CheckNowButton")).Style = (Style)window.FindResource(
            target.Name == "StatusPanel" ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        ((TextBlock)window.FindName("PageTitleText")).Text = localizer.T(target.TitleKey);
        ((TextBlock)window.FindName("PageSubtitleText")).Text = target.Name == "StatusPanel" ? localizer.T("Summary") : "";
        ResponsiveLayout.ApplyWifi(window);
    }
}
