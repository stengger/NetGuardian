using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

// 显式设置输出目录才生成审查矩阵；不加载主窗口业务代码、不访问网络或用户配置。
[Collection("WPF 桌面")]
public sealed class UiAuditCaptureTests
{
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("HighContrast")]
    public void 生成弹窗提示和交互状态审查图(string theme)
    {
        var directory = Environment.GetEnvironmentVariable("NETGUARDIAN_UI_AUDIT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        DesktopFixture.Run(window =>
        {
            if (theme == "HighContrast")
                window.Resources.MergedDictionaries[0] = new ResourceDictionary
                { Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Theme/HCBlack.xaml") };
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme)));
            PopulateLabels(window);
            Populate(window, 36);
            DesktopFixture.ShowPage(window, "WifiPanel");
            DesktopFixture.Layout(window, 1180, 758);
            var host = (FrameworkElement)window.FindName("RootSnackbar");
            foreach (var severity in new[] { "Success", "Info", "Warning", "Danger" })
            {
                host.GetType().GetMethod("ShowMessage")!.Invoke(host, new object?[]
                    { "审查提示", "审查摘要\n" + string.Join("\n", Enumerable.Repeat("完整详情用于检查多行滚动和键盘可达性。", 10)), severity, null, null, null });
                DesktopFixture.Layout(window, 1180, 758);
                var card = DesktopFixture.Descendants(host).OfType<Border>().Single(b => b.Name == "FeedbackCard");
                // 截取稳定终态，避免把入场动画第一帧误报为文字对比度缺陷。
                card.BeginAnimation(UIElement.OpacityProperty, null);
                card.Opacity = 1;
                card.RenderTransform = Transform.Identity;
                foreach (var expanded in new[] { false, true })
                {
                    DesktopFixture.Descendants(card).OfType<Expander>().Single().IsExpanded = expanded;
                    DesktopFixture.Layout(window, 1180, 758);
                    Save(window, directory, $"Feedback-{theme}-{severity}-{expanded}.png", 1);
                }
                DesktopFixture.Descendants(card).OfType<Button>().Single(b => b.Name == "FeedbackClose")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            var localizer = new Localizer();
            foreach (var kind in new[] { "Form", "Confirm", "Danger", "ThreeActions" })
            {
                var dialog = kind == "Form" ? (FluentDialogWindow)new AddWifiDialog(localizer,
                    new WifiNetwork("审查网络-ABCDEFGHIJKLMNOPQRSTUVWXYZ", 80, "WPA2-Personal", "CCMP", true, true)) { Owner = window }
                    : DialogPresentation.Create(window, "审查确认", string.Join("\n", Enumerable.Repeat("这是隔离审查内容，不执行网络操作。", 8)),
                        localizer.T("Save"), localizer.T("Cancel"), kind == "ThreeActions" ? localizer.T("Close") : null,
                        dangerous: kind == "Danger", defaultCancel: kind == "Danger");
                foreach (var dictionary in window.Resources.MergedDictionaries) dialog.Resources.MergedDictionaries.Add(dictionary);
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -20000;
                dialog.ShowActivated = false;
                try
                {
                    dialog.Show(); dialog.UpdateLayout();
                    foreach (var scale in new[] { 1d, 1.25d, 1.5d }) Save(dialog, directory, $"Dialog-{kind}-{theme}-{scale * 100:0}.png", scale);
                }
                finally { dialog.Close(); }
            }
        }, theme == "Light" ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }

    public static IEnumerable<object[]> Cases =>
        from theme in new[] { "Dark", "Light", "HighContrast" }
        from size in new[] { (1040d, 660d), (1180d, 758d), (1600d, 1000d) }
        from count in new[] { 0, 3, 36 }
        select new object[] { theme, size.Item1, size.Item2, count };

    [Theory]
    [MemberData(nameof(Cases))]
    public void 生成隔离审查矩阵(string theme, double width, double height, int count)
    {
        var directory = Environment.GetEnvironmentVariable("NETGUARDIAN_UI_AUDIT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        DesktopFixture.Run(window =>
        {
            if (theme == "HighContrast")
                window.Resources.MergedDictionaries[0] = new ResourceDictionary
                { Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Theme/HCBlack.xaml") };
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme)));
            ((Panel)window.Content).Background = (Brush)window.FindResource("ApplicationBackgroundBrush");
            PopulateLabels(window);
            Populate(window, count);
            foreach (var page in new[] { "StatusPanel", "WifiPanel", "QualityPanel", "ToolsPanel", "DnsPanel", "NetworkPanel", "PolicyPanel", "LogPanel", "SettingsPanel" })
            {
                DesktopFixture.ShowPage(window, page);
                DesktopFixture.Layout(window, width, height);
                foreach (var scale in new[] { 1d, 1.25d, 1.5d })
                    Save(window, directory, $"{page}-{theme}-{width}x{height}-n{count}-{scale * 100:0}.png", scale);
                if (page == "ToolsPanel" && count == 3)
                {
                    var tabs = (TabControl)window.FindName("ToolsTabControl");
                    for (var i = 1; i < tabs.Items.Count; i++)
                    {
                        tabs.SelectedIndex = i;
                        DesktopFixture.ShowPage(window, page);
                        DesktopFixture.Layout(window, width, height);
                        Save(window, directory, $"ToolsTab{i}-{theme}-{width}x{height}.png", 1);
                    }
                    tabs.SelectedIndex = 0;
                }
            }
        }, theme == "Light" ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }

    private static void PopulateLabels(Window window)
    {
        var localizer = new Localizer();
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/NetGuardian.App"));
        // 只读取现有本地化赋值，不执行窗口构造器、事件或业务刷新。
        foreach (var source in Directory.EnumerateFiles(root, "MainWindow*.cs"))
        foreach (Match match in Regex.Matches(File.ReadAllText(source), "(\\w+)\\.(Text|Content|Header) = _localizer\\.T\\(\"([^\"]+)\"\\);"))
        {
            var target = window.FindName(match.Groups[1].Value);
            var value = localizer.T(match.Groups[3].Value);
            if (target is TextBlock text && match.Groups[2].Value == "Text") text.Text = value;
            else if (target is HeaderedContentControl header && match.Groups[2].Value == "Header") header.Header = value;
            else if (target is ContentControl content && match.Groups[2].Value == "Content") content.Content = value;
            else if (target is DataGridColumn column && match.Groups[2].Value == "Header") column.Header = value;
        }
        ((NetGuardian.App.Views.SpeedTestView)window.FindName("SpeedView")).Update(new NetGuardian.Core.Network.SpeedTestSession(), localizer, TimeSpan.Zero);
        ((TextBlock)window.FindName("HealthStateText")).Text = localizer.T("NotChecked");
        ((TextBlock)window.FindName("CurrentWifiNameText")).Text = localizer.T("NotChecked");
        ((TextBlock)window.FindName("CurrentWifiSelectionText")).Text = localizer.T("SelectWifiFirst");
    }

    private static void Populate(Window window, int count)
    {
        ((DataGrid)window.FindName("NearbyGrid")).ItemsSource = Enumerable.Range(1, count).Select(i => new
        {
            Ssid = $"审查网络-{i:D2}-长名称-ABCDEFGHIJKLMNOPQRSTUVWXYZ", SignalPercent = 85 - i,
            Authentication = "WPA2-Personal", PriorityBadge = ""
        }).ToArray();
        ((DataGrid)window.FindName("ProfileGrid")).ItemsSource = Enumerable.Range(1, count).Select(i => new WifiProfile
        { Ssid = $"审查网络-{i:D2}-长名称-ABCDEFGHIJKLMNOPQRSTUVWXYZ", Enabled = i % 3 != 0, Priority = i }).ToArray();
        foreach (var name in new[] { "NearbyEmptyText", "ProfileEmptyText" })
            if (window.FindName(name) is UIElement empty) empty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var logs = new EventLogStore(new Localizer());
        for (var i = 0; i < count * 10; i++) logs.Add($"审查记录 {i}：2001:0db8:abcd:1234:5678:90ab:cdef:1234\n保留完整诊断详情。", UiLogLevel.Error, UiLogSource.Network);
        ((DataGrid)window.FindName("LogGrid")).ItemsSource = logs.Entries;
        EventLogPresentation.UpdateEmptyActions(window, count * 10, count * 10);
        foreach (var name in new[] { "DnsGrid", "AdapterGrid", "IpInfoGrid" })
            if (window.FindName(name) is DataGrid grid)
                grid.ItemsSource = Enumerable.Range(1, count).Select(i => new
                {
                    Name = $"审查适配器 {i}", Adapter = $"审查适配器 {i}", Type = "Wi-Fi", Status = "已连接", LinkState = "Up",
                    Ipv4 = "192.0.2.100", Gateway = "192.0.2.1", Servers = "2001:0db8:abcd:1234:5678:90ab:cdef:1234", DnsServers = "2001:0db8:abcd:1234:5678:90ab:cdef:1234", Source = "DHCP",
                    Value = "2001:0db8:abcd:1234:5678:90ab:cdef:1234"
                }).ToArray();
        foreach (var name in new[] { "DnsEmptyText", "AdapterEmptyText", "IpInfoEmptyText" })
            ((UIElement)window.FindName(name)).Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Save(Window window, string directory, string name, double scale)
    {
        Directory.CreateDirectory(directory);
        // 输出像素密度模拟，不宣称改变系统 DPI 或验证跨屏 WM_DPICHANGED。
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale),
            (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name));
        encoder.Save(file);
        Assert.True(file.Length > 0);
    }
}
