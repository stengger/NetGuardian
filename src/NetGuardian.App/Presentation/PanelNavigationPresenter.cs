using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

internal static class PanelNavigationPresenter
{
    private static readonly (string Name, string Title, string? Subtitle)[] Pages =
    [
        ("StatusPanel", "Status", "HomeWorkspaceHint"), ("WifiPanel", "Wifi", null),
        ("ToolsPanel", "DiagnosticsWorkspace", "DiagnosticsWorkspaceHint"),
        ("ConfigurationWorkspace", "ConfigurationWorkspace", "ConfigurationWorkspaceHint"),
        ("PolicyPanel", "RecoveryWorkspace", "RecoveryWorkspaceHint"),
        ("LogPanel", "Log", null), ("SettingsPanel", "Settings", null)
    ];

    private static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>
    {
        ["WifiPanel"] = "WifiPageActions", ["PolicyPanel"] = "PolicyPageActions", ["LogPanel"] = "LogPageActions",
        ["AdapterConfigTab"] = "NetworkPageActions", ["DnsConfigTab"] = "DnsPageActions",
        ["QualityToolsTab"] = "QualityPageActions", ["QuickToolsTab"] = "ToolsPageActions",
        ["SpeedToolsTab"] = "SpeedPageActions", ["WebsiteToolsTab"] = "WebsitePageActions",
        ["IpInfoToolsTab"] = "IpInfoPageActions", ["ReportToolsTab"] = "ReportPageActions"
    };

    public static void Show(Window window, string? tag, Localizer localizer)
    {
        var tools = (TabControl)window.FindName("ToolsTabControl");
        var config = (TabControl)window.FindName("ConfigurationTabs");
        switch (tag)
        {
            case "QualityPanel":
                tools.SelectedItem = window.FindName("QualityToolsTab");
                tag = "ToolsPanel";
                break;
            case "NetworkPanel": case "DnsPanel":
                config.SelectedItem = window.FindName(tag == "NetworkPanel" ? "AdapterConfigTab" : "DnsConfigTab");
                tag = "ConfigurationWorkspace";
                break;
        }
        var target = Pages.FirstOrDefault(page => page.Name == tag);
        if (target.Name is null) target = Pages[0];
        foreach (var page in Pages)
            ((UIElement)window.FindName(page.Name)).Visibility = page.Name == target.Name ? Visibility.Visible : Visibility.Collapsed;

        var context = target.Name switch
        {
            "ToolsPanel" => (tools.SelectedItem as TabItem)?.Name ?? "QualityToolsTab",
            "ConfigurationWorkspace" => (config.SelectedItem as TabItem)?.Name ?? "AdapterConfigTab",
            _ => target.Name
        };
        Actions.TryGetValue(context, out var selectedAction);
        foreach (var action in Actions.Values)
            ((UIElement)window.FindName(action)).Visibility = action == selectedAction ? Visibility.Visible : Visibility.Collapsed;
        var bar = (FrameworkElement)window.FindName("PageActionBar");
        var inline = selectedAction is "QualityPageActions" or "IpInfoPageActions" or "ReportPageActions"
            or "NetworkPageActions" or "DnsPageActions" or "PolicyPageActions" or "LogPageActions";
        Grid.SetRow(bar, inline ? 0 : 1);
        Grid.SetColumn(bar, inline ? 1 : 0);
        Grid.SetColumnSpan(bar, inline ? 1 : 2);
        bar.Visibility = selectedAction is null ? Visibility.Collapsed : Visibility.Visible;
        ((UIElement)window.FindName("HomeMonitorActions")).Visibility = target.Name == "StatusPanel" ? Visibility.Visible : Visibility.Collapsed;
        ((TextBlock)window.FindName("PageTitleText")).Text = localizer.T(target.Title);
        var subtitle = (TextBlock)window.FindName("PageSubtitleText");
        subtitle.Text = target.Subtitle is null ? "" : localizer.T(target.Subtitle);
        subtitle.Visibility = target.Subtitle is null ? Visibility.Collapsed : Visibility.Visible;
        ResponsiveLayout.ApplyWifi(window);
    }
}
