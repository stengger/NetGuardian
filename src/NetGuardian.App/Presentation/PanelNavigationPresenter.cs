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

    public static void Show(Window window, string? tag, Localizer localizer)
    {
        var target = Panels.FirstOrDefault(panel => panel.Name == tag);
        if (target.Name is null) target = Panels[0];
        foreach (var panel in Panels)
            ((UIElement)window.FindName(panel.Name)).Visibility = panel.Name == target.Name ? Visibility.Visible : Visibility.Collapsed;
        foreach (var actionName in PageActions.Values)
            ((UIElement)window.FindName(actionName)).Visibility = Visibility.Collapsed;
        var actionBar = (FrameworkElement)window.FindName("PageActionBar");
        actionBar.Visibility = PageActions.TryGetValue(target.Name, out var actionNameForPage)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (actionNameForPage is not null)
            ((UIElement)window.FindName(actionNameForPage)).Visibility = Visibility.Visible;
        ((TextBlock)window.FindName("PageTitleText")).Text = localizer.T(target.TitleKey);
        ((TextBlock)window.FindName("PageSubtitleText")).Text = target.Name == "StatusPanel" ? localizer.T("Summary") : "";
    }
}
