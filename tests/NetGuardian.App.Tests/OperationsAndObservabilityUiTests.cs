using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using NetGuardian.App.Presentation;
using NetGuardian.App.Views;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Models;
using NetGuardian.Core.Network;
using NetGuardian.Core.Notifications;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class OperationsAndObservabilityUiTests
{
    [Fact]
    public void CurrentWifiPresentation_AppendsRadioBandChannelRateAndBssid()
    {
        var info = new WifiConnectionInfo(
            ReadSucceeded: true,
            Connected: true,
            Ssid: "Campus-5G",
            AdapterName: "WLAN",
            SignalPercent: 92,
            Authentication: "WPA2-Personal",
            Encryption: "CCMP",
            Ipv4: "10.0.0.15",
            Bssid: "aa:bb:cc:11:22:33",
            RadioType: "802.11ax",
            Band: "5 GHz",
            Channel: 149,
            ReceiveRateMbps: 866.7,
            TransmitRateMbps: 780);

        var view = CurrentWifiPresentation.Create(info, new Localizer());
        Assert.Equal("Campus-5G", view.Name);
        Assert.Contains("5 GHz Ch149", view.Details);
        Assert.Contains("802.11ax", view.Details);
        Assert.Contains("866.7/780 Mbps", view.Details);
        Assert.Contains("aa:bb:cc:11:22:33", view.Details);
    }

    [Fact]
    public void SettingsPanel_PlacesOperationsSupportCardAfterUpdateCard_AndDialogSaves()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "SettingsPanel");
            DesktopFixture.Layout(window, 1178, 758);

            var updateCard = (FrameworkElement)window.FindName("UpdateCard");
            var supportCard = (FrameworkElement)window.FindName("OperationsSupportCard");
            var updateBounds = updateCard.TransformToAncestor(window).TransformBounds(new Rect(updateCard.RenderSize));
            var supportBounds = supportCard.TransformToAncestor(window).TransformBounds(new Rect(supportCard.RenderSize));
            Assert.True(supportBounds.Top >= updateBounds.Bottom,
                $"OperationsSupportCard 必须在 UpdateCard 之后：Update={updateBounds}, Support={supportBounds}");

            var localizer = new Localizer();
            var svc = new AlertNotificationService(System.IO.Path.GetTempPath());
            var initial = new NotificationSettings { Channel = NotificationChannelType.GenericWebhook };
            var dialog = new NotificationSettingsDialog(initial, localizer, svc, "Lab-WiFi")
            {
                Owner = window,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000
            };
            foreach (var dictionary in window.Resources.MergedDictionaries)
            {
                dialog.Resources.MergedDictionaries.Add(dictionary);
            }

            Exception? failure = null;
            dialog.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    dialog.EnableCheckBox.IsChecked = true;
                    dialog.ChannelComboBox.SelectedIndex = 2; // Feishu
                    dialog.WebhookUrlTextBox.Text = "https://open.feishu.cn/open-apis/bot/v2/hook/test-1234";
                    dialog.SaveButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                }
                catch (Exception ex)
                {
                    failure = ex;
                    dialog.DialogResult = false;
                }
            });

            var ok = TestDesktopWindow.ShowDialog(dialog);
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();

            Assert.True(ok);
            Assert.True(dialog.ResultSettings.Enabled);
            Assert.Equal(NotificationChannelType.Feishu, dialog.ResultSettings.Channel);
            Assert.Contains("hook/test-1234", dialog.ResultSettings.WebhookUrl);
        });
    }

    [Fact]
    public void IncidentTimelineView_RendersSlaMetricsAndLatencyPolyline()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "IncidentPanel");
            var host = (ContentControl)window.FindName("IncidentViewHost");
            var view = new IncidentTimelineView();
            host.Content = view;
            DesktopFixture.Layout(window, 1180, 760);

            var now = DateTimeOffset.Now;
            var sla7 = new NetworkSlaSummary(7, 2, 2, 99.55, 8.5, 12.0, 100.0,
                [new SsidStabilityStat("Dorm-5G", 2, 2, 8.5)]);
            var sla30 = new NetworkSlaSummary(30, 3, 3, 99.82, 9.0, 12.0, 100.0, []);
            var trend = new QualityTrendSummary(3, 38.0, 95.0, 88.0,
            [
                new QualityTrendPoint(now.AddMinutes(-10), 32, 8, 2, 86, "Dorm-5G", true),
                new QualityTrendPoint(now.AddMinutes(-5), 38, 10, 3, 88, "Dorm-5G", true),
                new QualityTrendPoint(now, 98, 14, 4, 90, "Dorm-5G", true)
            ]);

            view.ShowIncidents([]);
            view.ShowObservability(sla7, sla30, trend);
            view.UpdateLayout();

            var obs7d = (TextBlock)view.FindName("Obs7dValue");
            var obs30d = (TextBlock)view.FindName("Obs30dValue");
            var mttr = (TextBlock)view.FindName("ObsMttrValue");
            var ranking = (TextBlock)view.FindName("ObsWifiRankingText");
            var polyline = (Polyline)view.FindName("LatencyTrendLine");

            Assert.Equal("99.55%", obs7d.Text);
            Assert.Equal("99.82%", obs30d.Text);
            Assert.Contains("8.5", mttr.Text);
            Assert.Contains("Dorm-5G", ranking.Text);
            Assert.Equal(3, polyline.Points.Count);
        });
    }

    [Fact]
    public void MainWindow_FormatsProxyVpnBannerAndSavesOpsPolicy()
    {
        DesktopFixture.Run(window =>
        {
            var snap = new ProxyAndVpnSnapshot(
                SystemProxyEnabled: true,
                SystemProxyServer: "127.0.0.1:7890",
                SystemProxyOverride: "localhost",
                AutoConfigUrl: null,
                LocalProxyReachable: true,
                LocalProxyEndpoint: "127.0.0.1:7890",
                EnvironmentProxies: [],
                ActiveVpnInterfaces: [new DetectedVpnInterface("Clash", "Clash Tunnel", "Unknown", ["198.18.0.1"])],
                CapturedAtUtc: DateTimeOffset.UtcNow);

            var localizer = new Localizer();
            var bannerText = ProxyVpnPresentation.FormatBanner(snap, "WLAN", localizer);
            Assert.Contains("127.0.0.1:7890", bannerText);
            Assert.Contains("Clash", bannerText);
            Assert.Contains("WLAN", bannerText);

            var detail = new AdapterExtendedDetail(
                InterfaceId: "if-1",
                Name: "WLAN",
                IsDefaultIpv4Route: true,
                Ipv4Metric: 25,
                MacAddress: "AA:BB:CC:11:22:33",
                LinkSpeedMbps: 866.7,
                Mtu: 1500,
                Ipv6Addresses: ["240e::1"],
                DhcpEnabled: true,
                DhcpServer: "192.168.1.1",
                DhcpLeaseRemaining: TimeSpan.FromHours(12));
            var detailText = ProxyVpnPresentation.FormatAdapterDetail(detail, localizer);
            Assert.Contains("AA:BB:CC:11:22:33", detailText);
            Assert.Contains("1500", detailText);
            Assert.Contains("192.168.1.1", detailText);

            var adapterBanner = (TextBlock)window.FindName("AdapterProxyVpnText");
            var ipBanner = (TextBlock)window.FindName("IpProxyVpnText");
            var adapterDetail = (TextBlock)window.FindName("AdapterDetailText");
            adapterBanner.Text = bannerText;
            ipBanner.Text = bannerText;
            adapterDetail.Text = detailText;

            DesktopFixture.ShowPage(window, "PolicyPanel");
            DesktopFixture.Layout(window, 1180, 760);
            Assert.NotNull(window.FindName("PolicyOpsCard"));
            Assert.NotNull(window.FindName("PauseRecoveryButton"));
            Assert.NotNull(window.FindName("EnablePrimaryFailbackCheckBox"));
        });
    }
}
