using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Light, "zh-CN")]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Light, "en-US")]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Dark, "zh-CN")]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Dark, "en-US")]
    public void 明暗主题下设置长状态可以滚动至末尾且保留语言主题选择(Wpf.Ui.Appearance.ApplicationTheme theme, string language)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "SettingsPanel");
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            foreach (var (name, key) in new[] { ("LanguageLabel", "Language"), ("ThemeLabel", "ThemeMode"), ("TrayTitle", "Tray"), ("CloseBehaviorLabel", "CloseBehavior") })
                ((TextBlock)window.FindName(name)).Text = localizer.T(key);
            foreach (var (name, key) in new[] { ("StartMonitoringOnLaunchCheckBox", "StartMonitoringOnLaunch"), ("StartMinimizedToTrayCheckBox", "StartMinimizedToTray"), ("ScheduledStartupCheckBox", "ScheduledStartup") })
                ((ContentControl)window.FindName(name)).Content = localizer.T(key);
            var status = (TextBlock)window.FindName("ScheduledStartupStatusText");
            status.Text = string.Join("\n", Enumerable.Repeat(localizer.T("DiagnosticFailed") + " 计划任务权限错误原文。", 40));
            DesktopFixture.Layout(window, 1040, 660);
            var scroll = (ScrollViewer)window.FindName("SettingsScrollViewer");
            var surface = ((SolidColorBrush)window.FindResource("SurfaceCardBrush")).Color;
            foreach (var name in new[] { "ScheduledStartupStatusText", "HighRiskTipText", "ProbeTargetsTipText", "NearbyEmptyText", "SidebarUploadUnitText" })
            {
                var brush = Assert.IsType<SolidColorBrush>(((TextBlock)window.FindName(name)).Foreground);
                Assert.True(TextContrast(brush, surface) >= 4.5, $"{theme} {name} 对比度不足：{TextContrast(brush, surface):F2}:1");
            }
            Assert.True(scroll.ScrollableHeight > 0);
            foreach (var name in new[] { "LanguageComboBox", "ThemeComboBox" })
            {
                var control = (FrameworkElement)window.FindName(name);
                Assert.True(control.TransformToAncestor(window).TransformBounds(new Rect(control.RenderSize)).Bottom < 635);
            }
            scroll.ScrollToEnd();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            Assert.True(scroll.VerticalOffset >= scroll.ScrollableHeight - 1);
            var bounds = status.TransformToAncestor(window).TransformBounds(new Rect(status.RenderSize));
            Assert.True(bounds.Bottom < 635, $"状态末尾不可达：{bounds}");
            DesktopFixture.Save(window, $"settings-long-{theme}-{language}.png");
        }, theme);
    }

    private static double TextContrast(SolidColorBrush foreground, Color background)
    {
        static double Linear(double channel) => channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        static double Luminance(double r, double g, double b) => 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
        var alpha = foreground.Color.A / 255d * foreground.Opacity;
        var front = Luminance((foreground.Color.R * alpha + background.R * (1 - alpha)) / 255,
            (foreground.Color.G * alpha + background.G * (1 - alpha)) / 255,
            (foreground.Color.B * alpha + background.B * (1 - alpha)) / 255);
        var back = Luminance(background.R / 255d, background.G / 255d, background.B / 255d);
        return (Math.Max(front, back) + 0.05) / (Math.Min(front, back) + 0.05);
    }

    [Theory]
    [InlineData("Website", "zh-CN")]
    [InlineData("Website", "en-US")]
    [InlineData("IpInfo", "zh-CN")]
    [InlineData("IpInfo", "en-US")]
    [InlineData("Report", "zh-CN")]
    [InlineData("Report", "en-US")]
    public void 诊断工具取消和长错误状态下仍保留结果表格(string tool, string language)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "ToolsPanel");
            ((TabControl)window.FindName("ToolsTabControl")).SelectedItem = window.FindName(tool + "ToolsTab");
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            ((TextBlock)window.FindName(tool + "StatusText")).Text = localizer.T("DiagnosticFailed") + "\n" + string.Join("\n", Enumerable.Repeat("保留完整错误原文以便排查。", 20));
            var cancel = (Button)window.FindName(tool + "CancelButton");
            cancel.Visibility = Visibility.Visible;
            cancel.Content = localizer.T("Cancel");
            ((ProgressBar)window.FindName(tool + "Activity")).Visibility = Visibility.Visible;
            if (tool == "Website")
            {
                ((TextBlock)window.FindName("WebsiteHistoryHintText")).Text = localizer.T("WebsiteHistoryHint");
                ((Button)window.FindName("WebsiteRunButton")).Content = localizer.T("Test");
                foreach (var site in new[] { "Apple", "GitHub", "Google", "YouTube" }) ((Button)window.FindName("Website" + site + "Button")).Content = site;
                foreach (var (name, key) in new[] { ("WebsiteTimeColumn", "LogTime"), ("WebsiteNameColumn", "Website"), ("WebsiteStatusColumn", "StatusColumn"), ("WebsiteDnsColumn", "WebsiteDnsTime"), ("WebsiteLatencyColumn", "WebsiteHttpTime"), ("WebsiteDetailColumn", "Detail") })
                    ((DataGridColumn)window.FindName(name)).Header = localizer.T(key);
                ((DataGridColumn)window.FindName("WebsiteHttpColumn")).Header = "HTTP";
                ((DataGridColumn)window.FindName("WebsiteAddressColumn")).Header = "IP";
            }
            DesktopFixture.Layout(window, 1040, 660);
            var grid = (DataGrid)window.FindName(tool + "Grid");
            Assert.True(grid.ActualHeight >= 150, $"{tool} 结果表仅 {grid.ActualHeight} DIP");
            var bounds = cancel.TransformToAncestor(window).TransformBounds(new Rect(cancel.RenderSize));
            Assert.True(bounds.Bottom < 635 && bounds.Top > 0, $"取消按钮不可达：{bounds}");
            DesktopFixture.Save(window, $"diagnostic-{tool}-{language}.png");
        });
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 侧栏流量范围和二进制单位在紧凑窗口内可读(string language)
    {
        DesktopFixture.Run(window =>
        {
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            ((TextBlock)window.FindName("SidebarSpeedTitle")).Text = localizer.T("InterfaceTraffic");
            ((TextBlock)window.FindName("SidebarSpeedScopeText")).Text = localizer.T("InterfaceTrafficSummary");
            ((TextBlock)window.FindName("SidebarUploadSpeedText")).Text = "12.3";
            ((TextBlock)window.FindName("SidebarUploadUnitText")).Text = "MiB/s";
            ((TextBlock)window.FindName("SidebarDownloadSpeedText")).Text = "512";
            ((TextBlock)window.FindName("SidebarDownloadUnitText")).Text = "KiB/s";
            DesktopFixture.Layout(window, 1040, 660);
            foreach (var name in new[] { "SidebarSpeedScopeText", "SidebarUploadUnitText", "SidebarDownloadUnitText" })
            {
                var control = (FrameworkElement)window.FindName(name);
                var bounds = control.TransformToAncestor(window).TransformBounds(new Rect(control.RenderSize));
                Assert.InRange(bounds.Left, 0, 224);
                Assert.InRange(bounds.Bottom, 0, 610);
            }
            DesktopFixture.Save(window, $"sidebar-traffic-{language}.png");
        });
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 扫描错误长文本不会挤掉周围网络表格(string language)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            ((DataGrid)window.FindName("NearbyGrid")).ItemsSource = Enumerable.Range(1, 6).Select(i => new { Ssid = $"上次扫描保留的网络-{i}", SignalPercent = 80, Authentication = "WPA2", PriorityBadge = "" }).ToArray();
            ((TextBlock)window.FindName("WifiScanStatusText")).Text = localizer.T("WifiScanFailed") + "\n" + string.Join("\n", Enumerable.Repeat("访问被拒绝。请检查位置权限。", 20));
            DesktopFixture.Layout(window, 1040, 660);
            var grid = (DataGrid)window.FindName("NearbyGrid");
            Assert.True(grid.ActualHeight >= grid.ColumnHeaderHeight + 6 * 38, $"扫描错误后结果未完整测量：{grid.ActualHeight} DIP");
            var page = (ScrollViewer)window.FindName("WifiContentScrollViewer");
            var last = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(5);
            last.BringIntoView(); window.UpdateLayout();
            var bounds = last.TransformToAncestor(page).TransformBounds(new Rect(last.RenderSize));
            Assert.True(bounds.Top >= 0 && bounds.Bottom <= page.ActualHeight, $"长错误后第六行不可达：{bounds}");
            DesktopFixture.Save(window, $"wifi-scan-failed-{language}.png");
        });
    }

    [Theory]
    [InlineData("zh-CN", 1040, 660)]
    [InlineData("en-US", 1040, 660)]
    [InlineData("zh-CN", 960, 500)]
    [InlineData("en-US", 960, 500)]
    public void 日志筛选和多行详情展开后仍保留可用记录列表(string language, double width, double height)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "LogPanel");
            window.MinWidth = 960; window.MinHeight = 500;
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            foreach (var (name, key) in new[] { ("LogSearchLabel", "LogSearch"), ("LogLevelLabel", "LogLevel"), ("LogSourceLabel", "LogSource"), ("LogEmptyText", "LogNoMatches") })
                ((TextBlock)window.FindName(name)).Text = localizer.T(key);
            ((TextBlock)window.FindName("LogScopeText")).Text = string.Format(localizer.T("LogSessionScope"), 500);
            ((TextBlock)window.FindName("LogCountText")).Text = string.Format(localizer.T("LogVisibleCount"), 100, 100);
            ((TextBlock)window.FindName("LogEmptyText")).Visibility = Visibility.Collapsed;
            ((CheckBox)window.FindName("LogFollowCheckBox")).Content = localizer.T("LogFollowLatest");
            ((CheckBox)window.FindName("LogFollowCheckBox")).IsChecked = false;
            ((Button)window.FindName("ExportLogsButton")).Content = localizer.T("ExportLogs");
            foreach (var (name, key) in new[] { ("LogTimeColumn", "LogTime"), ("LogLevelColumn", "LogLevel"), ("LogSourceColumn", "LogSource"), ("LogMessageColumn", "LogMessage") })
                ((DataGridColumn)window.FindName(name)).Header = localizer.T(key);
            foreach (ComboBoxItem item in ((ComboBox)window.FindName("LogLevelComboBox")).Items)
                item.Content = localizer.T(item.Tag?.ToString() == "all" ? "LogAllLevels" : $"LogLevel{item.Tag}");
            foreach (ComboBoxItem item in ((ComboBox)window.FindName("LogSourceComboBox")).Items)
                item.Content = localizer.T(item.Tag?.ToString() == "all" ? "LogAllSources" : $"LogSource{item.Tag}");
            var store = new NetGuardian.App.Presentation.EventLogStore(localizer);
            for (var i = 0; i < 100; i++) store.Add($"DNS 检查记录 {i}\n" + string.Join("\n", Enumerable.Repeat("完整诊断内容与异常原文，不截断记录。", 12)), NetGuardian.App.Presentation.UiLogLevel.Error, NetGuardian.App.Presentation.UiLogSource.Network);
            var grid = (DataGrid)window.FindName("LogGrid");
            grid.ItemsSource = store.Entries;
            grid.SelectedIndex = 0;
            var details = (Expander)window.FindName("LogDetailsExpander");
            details.Header = localizer.T("LogSelectedDetails");
            details.IsExpanded = true;
            DesktopFixture.Layout(window, width, height);
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            window.UpdateLayout();
            var original = (TextBox)window.FindName("LogDetailsTextBox");
            Assert.StartsWith("DNS 检查记录 0", original.Text);
            Assert.Equal(0, original.VerticalOffset);
            Assert.True(grid.ActualHeight >= DesktopFixture.Descendants(grid).OfType<System.Windows.Controls.Primitives.DataGridColumnHeadersPresenter>().First().ActualHeight + 38, $"日志视口不足一条完整记录：{grid.ActualHeight} DIP");
            foreach (var name in new[] { "LogSearchTextBox", "ExportLogsButton", "LogFollowCheckBox" })
            {
                var control = (FrameworkElement)window.FindName(name);
                control.BringIntoView(); window.UpdateLayout();
                var bounds = control.TransformToAncestor(window).TransformBounds(new Rect(control.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom < height - 15, $"{name} 不可达：{bounds}");
            }
            DesktopFixture.Save(window, $"logs-expanded-{width}-{height}-{language}.png");
        });
    }

    [Fact]
    public void 首页显示检查来源与时间后保留探针区域()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "StatusPanel");
            var localizer = new Localizer();
            foreach (var (name, key) in new[] { ("HealthStateLabel", "HealthState"), ("HealthStateText", "InternetIssue"), ("MonitorStateLabel", "MonitorStatus"), ("MonitorStateText", "BackgroundMonitoring"), ("NextCheckLabel", "NextCheck"), ("CurrentNetworkLabel", "CurrentNetwork"), ("ProbeTitleText", "CheckNow"), ("RecentEventsTitle", "RecentEvents") })
                ((TextBlock)window.FindName(name)).Text = localizer.T(key);
            ((TextBlock)window.FindName("SummaryText")).Text = "公网 HTTPS 探针未达到通过数量，请检查上游网络或代理设置。";
            ((TextBlock)window.FindName("CurrentSsidText")).Text = "实验室_北楼_5GHz_备用网络";
            ((TextBlock)window.FindName("ReportContextText")).Text = "手动检查 · 2026-10-20 01:00:00";
            ((TextBlock)window.FindName("ReportContextText")).Visibility = Visibility.Visible;
            DesktopFixture.Layout(window, 1040, 660);
            var probe = (DataGrid)window.FindName("ProbeGrid");
            Assert.True(probe.ActualHeight >= 100);
            var context = (FrameworkElement)window.FindName("ReportContextText");
            var bounds = context.TransformToAncestor(window).TransformBounds(new Rect(context.RenderSize));
            Assert.True(bounds.Bottom < 640 && bounds.Height > 10);
            DesktopFixture.Save(window, "home-report-context-1040.png");
        });
    }

    [Theory]
    [InlineData(1180, 660, "zh-CN")]
    [InlineData(1040, 660, "zh-CN")]
    [InlineData(1040, 660, "en-US")]
    [InlineData(960, 500, "zh-CN")]
    [InlineData(960, 500, "en-US")]
    public void 质量检查分组指标与取消操作在长文案下仍可读取(double width, double height, string language)
    {
        DesktopFixture.Run(window =>
        {
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            window.MinWidth = 960; window.MinHeight = 500;
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
            DesktopFixture.Layout(window, width, height);
            foreach (var name in new[] { "QualityGatewayValue", "QualityDnsValue", "QualityHttpsValue", "QualityCancelButton", "QualityProbeGrid" })
            {
                var element = (FrameworkElement)window.FindName(name);
                element.BringIntoView(); window.UpdateLayout();
                var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= height - 20 && bounds.Height >= (name == "QualityProbeGrid" ? 100 : 25), $"{name} 不可达：{bounds}");
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
            DesktopFixture.Layout(window, width, height);
            var completedGrid = (DataGrid)window.FindName("QualityProbeGrid");
            Assert.True(completedGrid.ActualHeight >= 100);
            var body = (ScrollViewer)window.FindName("QualityBodyScrollViewer");
            body.ScrollToTop(); window.UpdateLayout();
            DesktopFixture.Save(window, $"quality-complete-initial-{width}-{height}-{language}.png");
            var inner = DesktopFixture.Descendants(completedGrid).OfType<ScrollViewer>().Single(viewer => viewer.Name == "DG_ScrollViewer");
            Assert.Equal(0, inner.ScrollableHeight);
            Assert.True(completedGrid.ActualHeight >= completedGrid.Items.Count * 38);
            body.ScrollToEnd(); window.UpdateLayout();
            Assert.True(body.VerticalOffset > 0, "结果应随正文滚动到末尾。");
            Assert.Equal(0, inner.VerticalOffset);
            body.ScrollToTop(); window.UpdateLayout();
            foreach (var index in new[] { 0, completedGrid.Items.Count - 1 })
            {
                completedGrid.ScrollIntoView(completedGrid.Items[index]); window.UpdateLayout();
                var row = (DataGridRow)completedGrid.ItemContainerGenerator.ContainerFromIndex(index);
                row.BringIntoView(); window.UpdateLayout();
                var bounds = row.TransformToAncestor(body).TransformBounds(new Rect(row.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= body.ActualHeight,
                    $"第 {index + 1} 条探针结果无法滚入正文视口：{bounds}；正文高度={body.ActualHeight}");
            }
            ((Button)window.FindName("QualityCancelButton")).Visibility = Visibility.Visible;
            window.UpdateLayout();
            var cancel = (FrameworkElement)window.FindName("QualityCancelButton");
            var cancelBounds = cancel.TransformToAncestor(window).TransformBounds(new Rect(cancel.RenderSize));
            Assert.True(cancelBounds.Top >= 0 && cancelBounds.Bottom <= height - 20,
                $"阅读结果后取消操作不可达：{cancelBounds}");
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
            ((DataGrid)window.FindName("AdapterGrid")).ItemsSource = Enumerable.Range(1, 25).Select(i => new { Name = $"实验室网络接口 {i}", Type = "无线网络", Status = "链路已连接", Ipv4 = "192.168.31.89", Gateway = "192.168.31.1", DnsServers = "223.5.5.5, 2001:4860:4860::8888" }).ToList();
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
    public static void Run(Action<Window> action, Wpf.Ui.Appearance.ApplicationTheme theme = Wpf.Ui.Appearance.ApplicationTheme.Dark)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            var previousContext = SynchronizationContext.Current;
            try
            {
                // 独立 STA 窗口也需要真实 UI 同步上下文，库的异步滚动状态才能回到所属线程。
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(
                    System.Windows.Threading.Dispatcher.CurrentDispatcher));
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
                // 附加属性同样保留展开后的命名空间，须与元素一起指向真实程序集。
                foreach (var attribute in document.Descendants().Attributes()
                    .Where(a => a.Name.NamespaceName == "clr-namespace:NetGuardian.App.Presentation").ToList())
                {
                    attribute.Parent!.SetAttributeValue(XName.Get(attribute.Name.LocalName,
                        "clr-namespace:NetGuardian.App.Presentation;assembly=NetGuardian"), attribute.Value);
                    attribute.Remove();
                }
                var events = new HashSet<string> { "Closing", "StateChanged", "Click", "TextChanged", "SelectionChanged", "Checked", "Unchecked", "PreviewMouseLeftButtonUp", "PreviewMouseLeftButtonDown", "PreviewMouseWheel", "PreviewKeyDown", "MouseMove", "MouseDoubleClick", "Drop" };
                foreach (var attribute in document.Descendants().Attributes().Where(a => events.Contains(a.Name.LocalName)).ToList()) attribute.Remove();
                window = (Window)XamlReader.Parse(document.ToString(), new ParserContext
                {
                    BaseUri = new Uri("pack://application:,,,/NetGuardian;component/MainWindow.xaml")
                });
                window.Resources.MergedDictionaries.Insert(0, new Wpf.Ui.Markup.ThemesDictionary { Theme = theme });
                window.Resources.MergedDictionaries.Insert(1, new Wpf.Ui.Markup.ControlsDictionary());
                window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/NetGuardian;component/Themes/Tokens.Common.xaml") });
                window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/NetGuardian;component/Themes/Palette.{(theme == Wpf.Ui.Appearance.ApplicationTheme.Light ? "Light" : "Dark")}.xaml") });
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
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "主窗口布局验证超时。");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public static void ShowPage(Window window, string name)
    {
        NetGuardian.App.Presentation.PanelNavigationPresenter.Show(window, name, new Localizer());
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
            ["WeeklyPeriodText"] = "UiLastSevenDays", ["WeeklyOutagesLabel"] = "UiOutages", ["WeeklyRateLabel"] = "UiRecoveryRate",
            ["HomeGatewayLabel"] = "QualityGatewayTiming", ["HomeDnsLabel"] = "QualityDnsTiming", ["HomeHttpsLabel"] = "QualityHttpsTiming",
            ["HomeGatewaySamples"] = "QualityNotMeasured", ["HomeDnsSamples"] = "QualityNotMeasured", ["HomeHttpsSamples"] = "QualityNotMeasured",
            ["SidebarSpeedTitle"] = "InterfaceTraffic", ["SidebarSpeedScopeText"] = "InterfaceTrafficSummary",
            ["LogResetFilterButton"] = "UiResetFilters", ["LogEmptyText"] = "LogNoEntries",
            ["HighRiskText"] = "HighRiskReserved", ["ToolsCheckButton"] = "CheckNow", ["ToolsScanButton"] = "Scan", ["ToolsNetworkSettingsButton"] = "OpenNetworkSettings",
            ["PolicyDetectionTitle"] = "UiPolicyDetection", ["PolicyActionsTitle"] = "UiPolicyActions",
            ["PolicyTargetsTitle"] = "UiPolicyTargets", ["SavePolicyButton"] = "Save", ["PolicyTitle"] = "Policy", ["IntervalLabel"] = "IntervalValue",
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
        foreach (var (name, key) in new[] { ("DnsAdapterColumn", "Adapter"), ("DnsServersColumn", "DnsServersAll"), ("DnsSourceColumn", "Source"), ("DnsStatusColumn", "StatusColumn"), ("AdapterNameColumn", "Adapter"), ("AdapterTypeColumn", "Type"), ("AdapterStatusColumn", "StatusColumn"), ("AdapterIpv4Column", "IPv4"), ("AdapterGatewayColumn", "Gateway"), ("AdapterDnsColumn", "DnsServersAll") })
            ((DataGridColumn)window.FindName(name)).Header = localizer.T(key);
        foreach (var (name, key) in new[] { ("NearbySsidColumn", "Ssid"), ("NearbySignalColumn", "Signal"), ("NearbySecurityColumn", "Security"), ("NearbyPriorityColumn", "PriorityState"), ("ProfileSsidColumn", "Ssid"), ("ProfilePriorityColumn", "Priority"), ("ProfileCredentialColumn", "CredentialSaved"), ("ProfileEnabledColumn", "Enabled"), ("ProfileFailuresColumn", "Failures") })
            ((DataGridColumn)window.FindName(name)).Header = localizer.T(key);
        foreach (var (name, key) in new[] { ("StatusNavButton", "Status"), ("WifiNavButton", "Wifi"), ("QualityNavButton", "QualityTools"), ("ToolsNavButton", "Tools"), ("DnsNavButton", "DnsTools"), ("AdapterNavButton", "AdapterTools"), ("PolicyNavButton", "Policy"), ("LogNavButton", "Log"), ("SettingsNavButton", "Settings"), ("CheckNowButton", "CheckNow"), ("StartMonitorButton", "StartMonitor"), ("StopButton", "Stop"), ("QuickToolsTab", "QuickTools"), ("SpeedToolsTab", "SpeedTools"), ("WebsiteToolsTab", "WebsiteTools"), ("IpInfoToolsTab", "IpInfoTools"), ("ReportToolsTab", "ReportTools") })
        {
            if (window.FindName(name) is HeaderedContentControl header) header.Header = localizer.T(key);
            else if (window.FindName(name) is ContentControl content) content.Content = localizer.T(key);
        }
        foreach (var name in new[] { "IntervalUnitComboBox", "CooldownUnitComboBox" })
        {
            var combo = (ComboBox)window.FindName(name);
            foreach (ComboBoxItem item in combo.Items) item.Content = localizer.T(item.Tag?.ToString() switch { "60" => "Minutes", "3600" => "Hours", _ => "Seconds" });
            combo.SelectedIndex = 0;
        }
        ((Wpf.Ui.Controls.NumberBox)window.FindName("IntervalTextBox")).Value = 30;
        ((Wpf.Ui.Controls.NumberBox)window.FindName("FailuresTextBox")).Value = 3;
        ((Wpf.Ui.Controls.NumberBox)window.FindName("CooldownTextBox")).Value = 5;
        ((TextBox)window.FindName("DnsProbeHostsTextBox")).Text = "www.microsoft.com, www.baidu.com";
        ((TextBox)window.FindName("HttpsProbeHostsTextBox")).Text = "www.microsoft.com, www.baidu.com, www.cloudflare.com";
    }
}
