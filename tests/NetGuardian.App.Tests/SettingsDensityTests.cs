using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class SettingsDensityTests
{
    [Theory]
    [InlineData(1178, 758, Wpf.Ui.Appearance.ApplicationTheme.Dark, "zh-CN")]
    [InlineData(1040, 660, Wpf.Ui.Appearance.ApplicationTheme.Light, "en-US")]
    public void 偏好控件横向对齐且启动说明与更新操作在同屏可达(double width, double height,
        Wpf.Ui.Appearance.ApplicationTheme theme, string language)
    {
        DesktopFixture.Run(window =>
        {
            var text = new Localizer();
            text.SetLanguage(language);
            ((PanelNavigationView)window.FindName("RootNavigation")).SelectPanel("SettingsPanel");
            PanelNavigationPresenter.Show(window, "SettingsPanel", text);
            FrameworkElement Find(string name) => (FrameworkElement)window.FindName(name);
            Rect Bounds(string name)
            {
                var element = Find(name);
                return element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
            }
            foreach (var (name, key) in new[] {
                ("LanguageLabel", "Language"), ("ThemeLabel", "ThemeMode"), ("CloseBehaviorLabel", "CloseBehavior"),
                ("SettingsTitle", "SettingsAppearance"), ("TrayTitle", "SettingsStartup"), ("UpdateTitleText", "UpdateTitle"),
                ("UserLogonStartupStatusText", "UserLogonStartupEnabled"), ("ScheduledStartupStatusText", "ScheduledStartupEnabledStatus"),
                ("UpdateStatusText", "UpdateNoUpdate"), ("HeaderStateText", "BackgroundMonitoring"),
                ("SidebarSpeedTitle", "InterfaceTraffic"), ("SidebarSpeedScopeText", "InterfaceTrafficSummary") })
                ((TextBlock)Find(name)).Text = text.T(key);
            foreach (var (name, key) in new[] { ("StatusNavButton", "Status"), ("WifiNavButton", "Wifi"),
                ("ToolsNavButton", "DiagnosticsWorkspace"), ("AdapterNavButton", "ConfigurationWorkspace"),
                ("PolicyNavButton", "RecoveryWorkspace"), ("SettingsNavButton", "Settings") })
                ((PanelNavigationItem)Find(name)).Content = text.T(key);
            ((TextBlock)Find("SidebarUploadLegend")).Text = "↑ " + text.T("UploadSpeed");
            ((TextBlock)Find("SidebarDownloadLegend")).Text = "↓ " + text.T("DownloadSpeed");
            foreach (var (name, key, index) in new[] { ("LanguageComboBox", language == "zh-CN" ? "Chinese" : "English", language == "zh-CN" ? 0 : 1),
                ("ThemeComboBox", "ThemeAuto", 0), ("CloseBehaviorComboBox", "CloseTray", 2) })
            {
                var box = (ComboBox)Find(name);
                ((ComboBoxItem)box.Items[index]).Content = text.T(key);
                box.SelectedIndex = index;
            }
            foreach (var (name, key) in new[] { ("StartOnUserLogonCheckBox", "StartOnUserLogon"),
                ("StartMonitoringOnLaunchCheckBox", "StartMonitoringOnLaunch"), ("StartMinimizedToTrayCheckBox", "StartMinimizedToTray"),
                ("ScheduledStartupCheckBox", "ScheduledStartup") })
            {
                var toggle = (Wpf.Ui.Controls.ToggleSwitch)Find(name);
                toggle.Content = text.T(key);
                toggle.IsEnabled = true;
                toggle.IsChecked = true;
            }
            ((TextBlock)Find("UpdateVersionText")).Text = string.Format(text.T("UpdateCurrentVersion"), "1.1.14");
            ((Button)Find("UpdateCheckButton")).Content = text.T("UpdateCheck");
            var previous = (TextBlock)Find("UpdatePreviousResultText");
            previous.Text = string.Format(text.T("UpdatePreviousResult"), text.T("UpdatePreviousSucceeded"),
                @"C:\ProgramData\NetGuardian\updates\offline-1.1.13\update.log", "1.1.13", new DateTime(2026, 11, 21));
            previous.Visibility = Visibility.Visible;
            DesktopFixture.Layout(window, width, height);
            DesktopFixture.Save(window, $"settings-{language}-{(int)width}.png");

            var fields = new[] { Bounds("LanguageComboBox"), Bounds("ThemeComboBox"), Bounds("CloseBehaviorComboBox") };
            var labels = new[] { Bounds("LanguageLabel"), Bounds("ThemeLabel"), Bounds("CloseBehaviorLabel") };
            for (var i = 0; i < fields.Length; i++)
            {
                Assert.InRange(Math.Abs(fields[i].Top - fields[0].Top), 0, 1);
                Assert.InRange(Math.Abs(fields[i].Left - labels[i].Left), 0, 1);
                Assert.InRange(fields[i].Top - labels[i].Bottom, 2, 8);
                if (i > 0) Assert.InRange(fields[i].Left - fields[i - 1].Right, 8, 24);
            }
            var logon = Bounds("StartOnUserLogonCheckBox");
            var guardian = Bounds("ScheduledStartupCheckBox");
            Assert.InRange(Math.Abs(logon.Top - guardian.Top), 0, 1);
            Assert.True(guardian.Left >= logon.Right + 8);
            foreach (var (toggle, status) in new[] { (logon, Bounds("UserLogonStartupStatusText")), (guardian, Bounds("ScheduledStartupStatusText")) })
                Assert.InRange(status.Top - toggle.Bottom, 0, 8);
            Assert.InRange(Math.Abs(Bounds("UpdateCheckButton").Right - fields[^1].Right), 0, 2);
            var scroll = (ScrollViewer)Find("SettingsScrollViewer");
            Assert.InRange(scroll.ScrollableWidth, 0, 1);
            Assert.True(Bounds("UpdateCheckButton").Bottom <= Bounds("SettingsScrollViewer").Bottom);
            if (width >= 1178) Assert.True(Bounds("UpdatePreviousResultText").Bottom <= Bounds("SettingsScrollViewer").Bottom);
        }, theme);
    }
}
