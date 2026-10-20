using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class AccessibleResultsTests
{
    [Fact]
    public void 结果区域的辅助技术名称跟随现有标题与语言()
    {
        DesktopFixture.Run(window =>
        {
            var localizer = new Localizer();
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                localizer.SetLanguage(language);
                foreach (var (controlName, labelName, key) in new[]
                {
                    ("ProbeGrid", "ProbeTitleText", "CheckNow"), ("RecentEventsList", "RecentEventsTitle", "RecentEvents"),
                    ("NearbyGrid", "NearbyNetworksTitle", "NearbyNetworks"), ("ProfileGrid", "PriorityListTitle", "PriorityList"),
                    ("DnsGrid", "DnsToolsTitle", "DnsTools"), ("QualityProbeGrid", "QualityToolsTitle", "QualityCheckTitle"),
                    ("AdapterGrid", "AdapterToolsTitle", "AdapterTools"), ("WebsiteGrid", "WebsiteToolsTab", "WebsiteTools"),
                    ("IpInfoGrid", "IpInfoToolsTab", "IpInfoTools"), ("ReportGrid", "ReportToolsTab", "ReportTools"),
                    ("LogGrid", "LogNavButton", "Log"), ("LogDetailsTextBox", "LogDetailsExpander", "LogSelectedDetails")
                })
                {
                    var label = window.FindName(labelName);
                    var expected = localizer.T(key);
                    Assert.NotEqual(key, expected);
                    if (label is TextBlock text) text.Text = expected;
                    else if (label is HeaderedContentControl header) header.Header = expected;
                    else ((ContentControl)label).Content = expected;
                    window.UpdateLayout();
                    var peer = UIElementAutomationPeer.CreatePeerForElement((UIElement)window.FindName(controlName));
                    Assert.NotNull(peer);
                    Assert.Equal(expected, peer.GetName());
                }
            }
        });
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 无线配置开关可区分完整网络名称和启用状态(string language)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            var grid = (DataGrid)window.FindName("ProfileGrid");
            var profiles = new[]
            {
                new WifiProfile { Ssid = "工作网络-很长的网络名称-5GHz", Priority = 1, Enabled = true },
                new WifiProfile { Ssid = "备用网络-2.4GHz", Priority = 2, Enabled = false }
            };
            grid.ItemsSource = profiles;
            ((DataGridColumn)window.FindName("ProfileEnabledColumn")).Header = localizer.T("Enabled");
            DesktopFixture.Layout(window, 1040, 660);
            var toggles = DesktopFixture.Descendants(grid).OfType<Wpf.Ui.Controls.ToggleSwitch>().ToArray();
            Assert.Equal(2, toggles.Length);
            foreach (var profile in profiles)
            {
                var toggle = toggles.Single(item => ReferenceEquals(item.DataContext, profile));
                var peer = UIElementAutomationPeer.CreatePeerForElement(toggle)!;
                Assert.Equal(profile.Ssid, peer.GetName());
                Assert.Equal(localizer.T("Enabled"), peer.GetHelpText());
                Assert.Equal(profile.Enabled, toggle.IsChecked);
                var provider = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
                Assert.Equal(profile.Enabled ? ToggleState.On : ToggleState.Off, provider.ToggleState);
            }
        });
    }
}
