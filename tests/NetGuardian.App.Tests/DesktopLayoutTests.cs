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

public sealed class DesktopLayoutTests
{
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
            ["ToolsTitle"] = "SystemNetworkTools"
        };
        foreach (var (name, key) in labels)
        {
            var element = window.FindName(name);
            if (element is TextBlock text) text.Text = localizer.T(key);
            else if (element is ContentControl content) content.Content = localizer.T(key);
        }
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
