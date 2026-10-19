using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Network;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class DesktopLayoutTests
{
    [Theory]
    [InlineData(1180, "zh-CN")]
    [InlineData(1040, "zh-CN")]
    [InlineData(1040, "en-US")]
    public void 质量检查分组指标与取消操作在长文案下仍可读取(double width, string language)
    {
        DesktopFixture.Run(window =>
        {
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            DesktopFixture.ShowPage(window, "QualityPanel");
            foreach (var (name, key) in new[] { ("QualityToolsTitle", "QualityCheckTitle"), ("QualityScopeText", "QualityCheckScope"), ("QualityStateText", "Checking"), ("QualityPassedText", "QualityNoCurrentResult"), ("QualityGatewayLabel", "QualityGatewayTiming"), ("QualityDnsLabel", "QualityDnsTiming"), ("QualityHttpsLabel", "QualityHttpsTiming"), ("QualityStatusText", "QualityCheckRunning") })
                ((TextBlock)window.FindName(name)).Text = localizer.T(key);
            foreach (var name in new[] { "QualityGatewayValue", "QualityDnsValue", "QualityHttpsValue" })
                ((TextBlock)window.FindName(name)).Text = "—";
            foreach (var name in new[] { "QualityGatewaySamples", "QualityDnsSamples", "QualityHttpsSamples" })
                ((TextBlock)window.FindName(name)).Text = localizer.T("QualityNotMeasured");
            foreach (var (name, key) in new[] { ("QualityProbeColumn", "Probe"), ("QualityResultColumn", "Result"), ("QualityLatencyColumn", "ProbeElapsed"), ("QualityDetailColumn", "Detail") })
                ((DataGridColumn)window.FindName(name)).Header = localizer.T(key);
            foreach (var (name, key) in new[] { ("QualityRunButton", "RunQualityCheck"), ("QualityCancelButton", "Cancel"), ("QualityOpenSettingsButton", "OpenNetworkSettings") })
                ((Button)window.FindName(name)).Content = localizer.T(key);
            ((Button)window.FindName("QualityCancelButton")).Visibility = Visibility.Visible;
            ((ProgressBar)window.FindName("QualityActivityBar")).Visibility = Visibility.Visible;
            DesktopFixture.Layout(window, width, 660);
            foreach (var name in new[] { "QualityGatewayValue", "QualityDnsValue", "QualityHttpsValue", "QualityCancelButton", "QualityProbeGrid" })
            {
                var element = (FrameworkElement)window.FindName(name);
                var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= 640 && bounds.Height >= (name == "QualityProbeGrid" ? 100 : 25), $"{name} 不可达：{bounds}");
            }
            Assert.Null(window.FindName("QualityScoreText"));
            Assert.Null(window.FindName("QualityLatencyText"));
            DesktopFixture.Save(window, $"quality-running-{width}-{language}.png");
            ((Button)window.FindName("QualityCancelButton")).Visibility = Visibility.Collapsed;
            ((ProgressBar)window.FindName("QualityActivityBar")).Visibility = Visibility.Collapsed;
            ((TextBlock)window.FindName("QualityStateText")).Text = localizer.T("Healthy");
            ((TextBlock)window.FindName("QualityPassedText")).Text = string.Format(localizer.T("QualityCheckCounts"), 7, 8, "实验室_北楼_5GHz_备用网络", "2026-10-20 00:50:00");
            ((TextBlock)window.FindName("QualityStatusText")).Text = localizer.T("QualityCheckScope");
            foreach (var name in new[] { "QualityGatewayValue", "QualityDnsValue", "QualityHttpsValue" })
                ((TextBlock)window.FindName(name)).Text = "12345.6 ms";
            foreach (var name in new[] { "QualityGatewaySamples", "QualityDnsSamples", "QualityHttpsSamples" })
                ((TextBlock)window.FindName(name)).Text = string.Format(localizer.T("QualityTimingSamples"), 2, 3, 2);
            ((DataGrid)window.FindName("QualityProbeGrid")).ItemsSource = Enumerable.Range(1, 12).Select(i => new { Name = $"HTTPS test-{i}.example", ResultText = localizer.T("Pass"), LatencyText = "12345.6 ms", Detail = "HTTP 200" }).ToList();
            DesktopFixture.Layout(window, width, 660);
            var completedGrid = (DataGrid)window.FindName("QualityProbeGrid");
            Assert.True(completedGrid.ActualHeight >= 100);
            DesktopFixture.Save(window, $"quality-complete-{width}-{language}.png");
        });
    }

    [Theory]
    [InlineData(1180, 760)]
    [InlineData(1040, 660)]
    public void 网络配置页的输入标签操作与表格均位于可用视口(double width, double height)
    {
        DesktopFixture.Run(window =>
        {
            ((ComboBox)window.FindName("DnsAdapterComboBox")).Items.Add("实验室无线网络接口（完整目标名称）");
            ((ComboBox)window.FindName("DnsAdapterComboBox")).SelectedIndex = 0;
            ((ComboBox)window.FindName("DnsPresetComboBox")).SelectedIndex = 0;
            ((Button)window.FindName("DnsApplyButton")).IsEnabled = false;
            ((TextBlock)window.FindName("DnsCurrentText")).Text = "当前系统 DNS 地址：223.5.5.5, 223.6.6.6, fec0:0:0:ffff::1。自动 / 手动来源未确认；IPv6 地址仅供查看。";
            ((ComboBox)window.FindName("AdapterTargetComboBox")).Items.Add("实验室 WLAN 2");
            ((ComboBox)window.FindName("AdapterTargetComboBox")).SelectedIndex = 0;
            ((DataGrid)window.FindName("DnsGrid")).ItemsSource = Enumerable.Range(1, 25).Select(i => new { Adapter = $"实验室网络接口 {i}", Servers = "223.5.5.5, 223.6.6.6", Source = "未确认", Status = "链路已连接" }).ToList();
            ((DataGrid)window.FindName("AdapterGrid")).ItemsSource = Enumerable.Range(1, 25).Select(i => new { Name = $"实验室网络接口 {i}", Type = "Wireless80211", Status = "已连接", Ipv4 = "192.168.31.89", Gateway = "192.168.31.1", DnsServers = "223.5.5.5, 223.6.6.6" }).ToList();
            foreach (var page in new[] { "DnsPanel", "NetworkPanel" })
            {
                DesktopFixture.ShowPage(window, page);
                DesktopFixture.Layout(window, width, height);
                var grid = (DataGrid)window.FindName(page == "DnsPanel" ? "DnsGrid" : "AdapterGrid");
                var bounds = grid.TransformToAncestor(window).TransformBounds(new Rect(grid.RenderSize));
                Assert.True(bounds.Height >= 100 && bounds.Bottom <= height - 20, $"{page} 表格超出视口：{bounds}");
                if (page == "NetworkPanel")
                {
                    Assert.True(grid.Columns[0].ActualWidth >= 180, $"适配器名称列仅 {grid.Columns[0].ActualWidth} DIP");
                    Assert.True(grid.Columns[3].ActualWidth >= 140, $"IPv4 列仅 {grid.Columns[3].ActualWidth} DIP；各列：{string.Join(", ", grid.Columns.Select(c => $"{c.Width}/{c.ActualWidth:F1}"))}");
                }
                foreach (var name in page == "DnsPanel"
                    ? new[] { "DnsAdapterComboBox", "DnsPresetComboBox", "DnsPrimaryTextBox", "DnsSecondaryTextBox", "DnsApplyButton" }
                    : new[] { "AdapterTargetComboBox", "AdapterRenewDhcpButton" })
                {
                    var control = (FrameworkElement)window.FindName(name);
                    var controlBounds = control.TransformToAncestor(window).TransformBounds(new Rect(control.RenderSize));
                    Assert.True(controlBounds.Height >= 25 && controlBounds.Bottom <= height - 20, $"{name} 不可达：{controlBounds}");
                }
                DesktopFixture.Save(window, $"{page}-{width}.png");
            }
            Assert.NotNull(window.FindName("DnsPrimaryLabel"));
            Assert.NotNull(window.FindName("DnsSecondaryLabel"));
            Assert.NotNull(window.FindName("DnsCurrentText"));
            Assert.NotNull(window.FindName("AdapterTargetComboBox"));
        });
    }

    [Theory]
    [InlineData(1180, 760)]
    [InlineData(1040, 660)]
    public void 策略保存区在实际主窗口中始终可达(double width, double height)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "PolicyPanel");
            DesktopFixture.Layout(window, width, height);
            DesktopFixture.Save(window, $"policy-{width}.png");
            var button = (Button)window.FindName("SavePolicyButton");
            var bounds = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
            Assert.True(bounds.Height >= 30 && bounds.Bottom <= height - 12, $"保存按钮超出窗口：{bounds}");
        });
    }

    [Theory]
    [InlineData(1180)]
    [InlineData(1040)]
    public void 无线网络名称不会被辅助列挤成无法辨认的前缀(double width)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var nearby = (DataGrid)window.FindName("NearbyGrid");
            nearby.ItemsSource = new[] { new { Ssid = "实验室_北楼_5GHz_备用网络", SignalPercent = 82, Authentication = "WPA2-Personal", PriorityBadge = "已加入" } };
            var profiles = (DataGrid)window.FindName("ProfileGrid");
            profiles.ItemsSource = new[] { new WifiProfile { Ssid = "实验室_北楼_5GHz_备用网络", Priority = 1, CredentialSaved = true, Enabled = true } };
            DesktopFixture.Layout(window, width, 760);
            DesktopFixture.Save(window, $"wifi-{width}.png");
            Assert.True(nearby.Columns[0].ActualWidth >= 180, $"周围网络名称列仅 {nearby.Columns[0].ActualWidth} DIP");
            Assert.True(profiles.Columns[1].ActualWidth >= 180, $"恢复列表名称列仅 {profiles.Columns[1].ActualWidth} DIP");
        });
    }

    [Fact]
    public void 实际工具页滚动说明后仍可读取测速摘要()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "ToolsPanel");
            ((TabControl)window.FindName("ToolsTabControl")).SelectedIndex = 1;
            var view = (NetGuardian.App.Views.SpeedTestView)window.FindName("SpeedView");
            var session = new SpeedTestSession();
            var id = session.Begin();
            session.Complete(id, new(128.4, 24.8, 92, 5_000_000, 1_000_000, DateTimeOffset.Now, 2));
            view.Update(session, new Localizer(), TimeSpan.FromSeconds(8));
            ((Expander)view.FindName("MethodExpander")).IsExpanded = true;
            DesktopFixture.Layout(window, 1180, 760);
            var scroll = DesktopFixture.Descendants(view).OfType<ScrollViewer>().First();
            scroll.ScrollToBottom();
            window.UpdateLayout();
            foreach (var name in new[] { "DownloadValue", "UploadValue", "ResponseValue" })
            {
                var metric = (FrameworkElement)view.FindName(name);
                var bounds = metric.TransformToAncestor(view).TransformBounds(new Rect(metric.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= view.ActualHeight, $"{name} 滚出视口：{bounds}");
            }
            DesktopFixture.Save(window, "speed-details.png");
        });
    }
}

// 加载产品真实主窗口 XAML，仅移除事件接线；不执行主窗口构造中的监控、网络或配置操作。
internal static class DesktopFixture
{
    public static void Run(Action<Window> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                _ = new Grid();
                using var stream = typeof(DesktopFixture).Assembly.GetManifestResourceStream("MainWindow.xaml")!;
                var document = XDocument.Load(stream);
                XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                document.Root!.Attribute(xaml + "Class")!.Remove();
                document.Root.SetAttributeValue(XNamespace.Xmlns + "views", "clr-namespace:NetGuardian.App.Views;assembly=NetGuardian");
                document.Root.SetAttributeValue(XNamespace.Xmlns + "presentation", "clr-namespace:NetGuardian.App.Presentation;assembly=NetGuardian");
                // LINQ to XML 的元素名保留展开后的命名空间，改 xmlns 不会重命名元素。
                foreach (var element in document.Descendants().Where(e => e.Name.NamespaceName == "clr-namespace:NetGuardian.App.Views"))
                    element.Name = XName.Get(element.Name.LocalName, "clr-namespace:NetGuardian.App.Views;assembly=NetGuardian");
                foreach (var element in document.Descendants().Where(e => e.Name.NamespaceName == "clr-namespace:NetGuardian.App.Presentation"))
                    element.Name = XName.Get(element.Name.LocalName, "clr-namespace:NetGuardian.App.Presentation;assembly=NetGuardian");
                var events = new HashSet<string> { "Closing", "StateChanged", "Click", "SelectionChanged", "Checked", "Unchecked", "PreviewMouseLeftButtonUp", "PreviewMouseLeftButtonDown", "MouseMove", "MouseDoubleClick", "Drop" };
                foreach (var attribute in document.Descendants().Attributes().Where(a => events.Contains(a.Name.LocalName)).ToList()) attribute.Remove();
                window = (Window)XamlReader.Parse(document.ToString(), new ParserContext
                {
                    BaseUri = new Uri("pack://application:,,,/NetGuardian;component/MainWindow.xaml")
                });
                window.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
                window.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/NetGuardian;component/Themes/Tokens.Common.xaml") });
                window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/NetGuardian;component/Themes/Palette.Dark.xaml") });
                // 原窗口使用原生 Mica；离屏位图需以同主题实色衬底合成半透明控件。
                ((Panel)window.Content).Background = (Brush)window.FindResource("ApplicationBackgroundBrush");
                SetLabels(window);
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -20000;
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.Show();
                action(window);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                window?.Close();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "主窗口布局验证超时。");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public static void ShowPage(Window window, string name)
    {
        foreach (var page in new[] { "StatusPanel", "WifiPanel", "PolicyPanel", "ToolsPanel", "DnsPanel", "QualityPanel", "NetworkPanel", "LogPanel", "SettingsPanel" })
            ((UIElement)window.FindName(page)).Visibility = page == name ? Visibility.Visible : Visibility.Collapsed;
    }

    public static void Layout(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        window.UpdateLayout();
        NetGuardian.App.Presentation.ResponsiveLayout.ApplyWifi(window);
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    public static void Save(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("NETGUARDIAN_DESKTOP_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name));
        encoder.Save(file);
    }

    private static void SetLabels(Window window)
    {
        var localizer = new Localizer();
        var labels = new Dictionary<string, string>
        {
            ["SavePolicyButton"] = "Save", ["PolicyTitle"] = "Policy", ["IntervalLabel"] = "IntervalValue",
            ["FailuresBeforeRecoveryLabel"] = "FailuresBeforeRecovery", ["CooldownLabel"] = "CooldownValue",
            ["AutoSwitchCheckBox"] = "AutoSwitch", ["SoftReconnectCheckBox"] = "SoftReconnect",
            ["WifiAutoEnableCheckBox"] = "WifiAutoEnable", ["DnsFlushCheckBox"] = "DnsFlush",
            ["HighRiskCheckBox"] = "HighRiskRecovery", ["HighRiskTipText"] = "HighRiskTip",
            ["DnsProbeHostsLabel"] = "DnsProbeHostsLabel", ["HttpsProbeHostsLabel"] = "HttpsProbeHostsLabel",
            ["ProbeTargetsTipText"] = "ProbeTargetsTip", ["NearbyNetworksTitle"] = "NearbyNetworks",
            ["PriorityListTitle"] = "PriorityList", ["PageTitleText"] = "AppTitle", ["HeaderStateText"] = "Ready",
            ["ScanButton"] = "Scan", ["AddSelectedButton"] = "AddSelected", ["SetPasswordButton"] = "SetPassword",
            ["ImportPasswordButton"] = "ImportPassword", ["AddManualButton"] = "AddManual", ["MoveUpButton"] = "MoveUp",
            ["MoveDownButton"] = "MoveDown", ["RemoveButton"] = "Remove", ["ConnectButton"] = "Connect",
            ["WifiSelectionText"] = "SelectWifiFirst", ["DragHintText"] = "DoubleClickWifiHint",
            ["ToolsTitle"] = "SystemNetworkTools",
            ["DnsTargetLabel"] = "DnsTargetLabel", ["DnsPlanLabel"] = "DnsPlanLabel",
            ["DnsPrimaryLabel"] = "DnsPrimaryLabel", ["DnsSecondaryLabel"] = "DnsSecondaryLabel",
            ["DnsRefreshButton"] = "RefreshStatus", ["DnsFlushButton"] = "FlushDns",
            ["DnsApplyButton"] = "ApplyDns", ["DnsToolsHint"] = "DnsToolsHint",
            ["DnsStatusText"] = "SelectDnsPlan", ["AdapterTargetLabel"] = "AdapterTargetLabel",
            ["AdapterRefreshButton"] = "RefreshStatus", ["AdapterSettingsButton"] = "OpenNetworkSettings",
            ["AdapterRenewDhcpButton"] = "RenewDhcp", ["AdapterToolsHint"] = "AdapterToolsHint",
            ["AdapterStatusText"] = "SelectDhcpAdapter"
        };
        foreach (var (name, key) in labels)
        {
            var element = window.FindName(name);
            if (element is TextBlock text) text.Text = localizer.T(key);
            else if (element is ContentControl content) content.Content = localizer.T(key);
        }
        ((ComboBoxItem)((ComboBox)window.FindName("DnsPresetComboBox")).Items[0]).Content = localizer.T("SelectDnsPlan");
        foreach (var (name, key) in new[] { ("DnsAdapterColumn", "Adapter"), ("DnsServersColumn", "DnsServers"), ("DnsSourceColumn", "Source"), ("DnsStatusColumn", "StatusColumn"), ("AdapterNameColumn", "Adapter"), ("AdapterTypeColumn", "Type"), ("AdapterStatusColumn", "StatusColumn"), ("AdapterIpv4Column", "IPv4"), ("AdapterGatewayColumn", "Gateway"), ("AdapterDnsColumn", "DnsServers") })
            ((DataGridColumn)window.FindName(name)).Header = localizer.T(key);
        foreach (var (name, key) in new[] { ("NearbySsidColumn", "Ssid"), ("NearbySignalColumn", "Signal"), ("NearbySecurityColumn", "Security"), ("NearbyPriorityColumn", "PriorityState"), ("ProfileSsidColumn", "Ssid"), ("ProfilePriorityColumn", "Priority"), ("ProfileCredentialColumn", "CredentialSaved"), ("ProfileEnabledColumn", "Enabled"), ("ProfileFailuresColumn", "Failures") })
            ((DataGridColumn)window.FindName(name)).Header = localizer.T(key);
        foreach (var (name, key) in new[] { ("StatusNavButton", "Status"), ("WifiNavButton", "Wifi"), ("QualityNavButton", "QualityTools"), ("ToolsNavButton", "Tools"), ("DnsNavButton", "DnsTools"), ("AdapterNavButton", "AdapterTools"), ("PolicyNavButton", "Policy"), ("LogNavButton", "Log"), ("SettingsNavButton", "Settings"), ("CheckNowButton", "CheckNow"), ("StartMonitorButton", "StartMonitor"), ("StopButton", "Stop"), ("QuickToolsTab", "QuickTools"), ("SpeedToolsTab", "SpeedTools"), ("WebsiteToolsTab", "WebsiteTools"), ("IpInfoToolsTab", "IpInfoTools"), ("ReportToolsTab", "ReportTools") })
        {
            if (window.FindName(name) is HeaderedContentControl header) header.Header = localizer.T(key);
            else if (window.FindName(name) is ContentControl content) content.Content = localizer.T(key);
        }
        ((Wpf.Ui.Controls.NumberBox)window.FindName("IntervalTextBox")).Value = 30;
        ((Wpf.Ui.Controls.NumberBox)window.FindName("FailuresTextBox")).Value = 3;
        ((Wpf.Ui.Controls.NumberBox)window.FindName("CooldownTextBox")).Value = 5;
        ((TextBox)window.FindName("DnsProbeHostsTextBox")).Text = "www.microsoft.com, www.baidu.com";
        ((TextBox)window.FindName("HttpsProbeHostsTextBox")).Text = "www.microsoft.com, www.baidu.com, www.cloudflare.com";
    }
}
