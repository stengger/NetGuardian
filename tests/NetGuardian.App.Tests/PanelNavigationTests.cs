using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.IO;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Wpf.Ui.Controls;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class PanelNavigationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 辅助技术选择现有面板不进入页面实例导航(bool expanded)
    {
        DesktopFixture.Run(window =>
        {
            var navigation = (NavigationView)window.FindName("RootNavigation");
            navigation.IsPaneOpen = expanded;
            var item = (NavigationViewItem)window.FindName("WifiNavButton");
            var peer = UIElementAutomationPeer.CreatePeerForElement(item)!;
            var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(peer.GetPattern(PatternInterface.SelectionItem));
            selection.Select();
            Assert.Same(item, navigation.SelectedItem);
            Assert.True(selection.IsSelected);
            Assert.True(item.IsActive);
        });
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void 键盘激活现有面板更新导航选择(Key key)
    {
        DesktopFixture.Run(window =>
        {
            var navigation = (NavigationView)window.FindName("RootNavigation");
            var item = (NavigationViewItem)window.FindName("SettingsNavButton");
            Assert.True(ReferenceEquals(navigation, ((PanelNavigationItem)item).PanelOwner),
                $"导航尚未完成归属接线：窗口Loaded={window.IsLoaded}，导航Loaded={navigation.IsLoaded}，项目Loaded={item.IsLoaded}");
            item.Focus();
            Assert.True(item.IsFocused, "激活前须取得导航项的逻辑焦点。");
            var source = PresentationSource.FromVisual(item)!;
            item.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
            item.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyUpEvent });
            Assert.Same(item, navigation.SelectedItem);
            Assert.True(item.IsActive);
            Assert.True(item.IsFocused, "激活后焦点应保留在导航项。");
        });
    }

    [Fact]
    public void 导航向辅助技术提供完整的单选容器()
    {
        DesktopFixture.Run(window =>
        {
            var navigation = (PanelNavigationView)window.FindName("RootNavigation");
            Assert.True(navigation.SelectPanel("StatusPanel"));
            // 仅向本测试创建的窗口请求 UIA 根，建立 provider 所需的 HWND 连接。
            var nativeResult = SendMessage(new WindowInteropHelper(window).Handle, 0x003D, IntPtr.Zero, new IntPtr(-25));
            var rootPeer = UIElementAutomationPeer.CreatePeerForElement(window)!;
            var tree = new System.Text.StringBuilder($"原生结果：{nativeResult}\n");
            void Visit(AutomationPeer node, int depth)
            {
                tree.AppendLine($"{new string(' ', depth)}{node.GetType().Name} / {node.GetName()}");
                if (depth > 8) return;
                foreach (var child in node.GetChildren() ?? []) Visit(child, depth + 1);
            }
            Visit(rootPeer, 0);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("TEMP")!, "navigation-uia-tree.txt"), tree.ToString());
            var peer = UIElementAutomationPeer.CreatePeerForElement(navigation);
            Assert.NotNull(peer);
            var container = Assert.IsAssignableFrom<ISelectionProvider>(peer.GetPattern(PatternInterface.Selection));
            Assert.True(container.IsSelectionRequired);
            Assert.False(container.CanSelectMultiple);
            var selectedProvider = Assert.Single(container.GetSelection());
            Assert.NotNull(selectedProvider);
            Assert.IsAssignableFrom<ISelectionItemProvider>(selectedProvider.GetPatternProvider(10010));
            var itemPeer = UIElementAutomationPeer.CreatePeerForElement((NavigationViewItem)window.FindName("StatusNavButton"))!;
            var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(itemPeer.GetPattern(PatternInterface.SelectionItem));
            Assert.NotNull(selection.SelectionContainer);
            Assert.IsAssignableFrom<ISelectionProvider>(selection.SelectionContainer.GetPatternProvider(10001));
            Assert.Throws<InvalidOperationException>(selection.RemoveFromSelection);
            var actualItems = peer.GetChildren()!.Where(child => child.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider).ToArray();
            Assert.Equal(9, actualItems.Length);
            Assert.Equal(9, actualItems.Select(child => child.GetName()).Distinct().Count());
        });
    }

    [Theory]
    [InlineData(true, "zh-CN")]
    [InlineData(false, "en-US")]
    public void 全部导航项保持唯一选择且拒绝未知或禁用目标(bool expanded, string language)
    {
        DesktopFixture.Run(window =>
        {
            var navigation = (PanelNavigationView)window.FindName("RootNavigation");
            navigation.IsPaneOpen = expanded;
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            var items = navigation.MenuItems.Cast<object>().Concat(navigation.FooterMenuItems.Cast<object>()).OfType<PanelNavigationItem>().ToArray();
            Assert.Equal(9, items.Length);
            var changes = 0;
            navigation.SelectionChanged += (_, _) => changes++;
            navigation.SelectionChanged += (_, _) => PanelNavigationPresenter.Show(window, (navigation.SelectedItem as NavigationViewItem)?.Tag?.ToString(), localizer);
            foreach (var (name, key) in new[] { ("LanguageLabel", "Language"), ("ThemeLabel", "ThemeMode"), ("TrayTitle", "Tray"), ("CloseBehaviorLabel", "CloseBehavior") })
                ((System.Windows.Controls.TextBlock)window.FindName(name)).Text = localizer.T(key);
            foreach (var (name, key) in new[] { ("StartMonitoringOnLaunchCheckBox", "StartMonitoringOnLaunch"), ("StartMinimizedToTrayCheckBox", "StartMinimizedToTray"), ("ScheduledStartupCheckBox", "ScheduledStartup") })
                ((System.Windows.Controls.ContentControl)window.FindName(name)).Content = localizer.T(key);
            var actionMap = new[]
            {
                ("StatusPanel", (string?)null), ("WifiPanel", "WifiPageActions"),
                ("QualityPanel", "QualityPageActions"), ("ToolsPanel", "ToolsPageActions"),
                ("DnsPanel", "DnsPageActions"), ("NetworkPanel", "NetworkPageActions"),
                ("PolicyPanel", "PolicyPageActions"), ("LogPanel", "LogPageActions"),
                ("SettingsPanel", (string?)null)
            };
            foreach (var (name, key) in new[] { ("StatusNavButton", "Status"), ("WifiNavButton", "Wifi"), ("QualityNavButton", "QualityTools"), ("ToolsNavButton", "Tools"), ("DnsNavButton", "DnsTools"), ("AdapterNavButton", "AdapterTools"), ("PolicyNavButton", "Policy"), ("LogNavButton", "Log"), ("SettingsNavButton", "Settings") })
            {
                var item = (PanelNavigationItem)window.FindName(name);
                item.Content = localizer.T(key);
                Assert.NotNull(window.FindName((string)item.Tag));
                var peer = UIElementAutomationPeer.CreatePeerForElement(item)!;
                Assert.Equal(localizer.T(key), peer.GetName());
                ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                Assert.Equal(1, items.Count(candidate => candidate.IsActive));
                Assert.Same(item, navigation.SelectedItem);
                var panel = (FrameworkElement)window.FindName((string)item.Tag);
                Assert.Equal(Visibility.Visible, panel.Visibility);
                Assert.Equal(1, new[] { "StatusPanel", "WifiPanel", "QualityPanel", "ToolsPanel", "DnsPanel", "NetworkPanel", "PolicyPanel", "LogPanel", "SettingsPanel" }
                    .Count(tag => ((FrameworkElement)window.FindName(tag)).Visibility == Visibility.Visible));
                var expectedAction = actionMap.Single(action => action.Item1 == (string)item.Tag);
                Assert.Equal(expectedAction.Item2 is not null, ((FrameworkElement)window.FindName("PageActionBar")).Visibility == Visibility.Visible);
                foreach (var action in actionMap.Where(action => action.Item2 is not null))
                    Assert.Equal(action.Item2 == expectedAction.Item2, ((FrameworkElement)window.FindName(action.Item2!)).Visibility == Visibility.Visible);
                Assert.Equal(localizer.T(key), ((System.Windows.Controls.TextBlock)window.FindName("PageTitleText")).Text);
                Assert.True(navigation.SelectPanel((string)item.Tag));
            }
            Assert.Equal(9, changes);
            Assert.False(navigation.SelectPanel("Unknown"));
            var disabled = items[0];
            disabled.IsEnabled = false;
            Assert.False(navigation.SelectPanel((string)disabled.Tag));
            var disabledPeer = UIElementAutomationPeer.CreatePeerForElement(disabled)!;
            var disabledSelection = (ISelectionItemProvider)disabledPeer.GetPattern(PatternInterface.SelectionItem);
            Assert.Throws<System.Windows.Automation.ElementNotEnabledException>(disabledSelection.Select);
            Assert.Equal(9, changes);
            DesktopFixture.Layout(window, 1040, 660);
            navigation.IsPaneOpen = expanded;
            window.UpdateLayout();
            if (!expanded)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                timer.Tick += (_, _) => frame.Continue = false;
                timer.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                timer.Stop();
                window.UpdateLayout();
                Assert.True(navigation.ActualWidth < 80, $"收起后仍占用展开宽度：{navigation.ActualWidth}");
            }
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            Assert.Equal(expanded, navigation.IsPaneOpen);
            Assert.All(items, item => Assert.Equal(expanded, item.IsPaneOpen));
            Assert.True(navigation.ActualWidth >= 48);
            Assert.NotNull(PresentationSource.FromVisual(items[^1]));
            Assert.DoesNotContain(DesktopFixture.Descendants(navigation).OfType<System.Windows.Controls.Frame>(), frame => frame.IsVisible);
            DesktopFixture.Save(window, $"navigation-{expanded}-{language}.png");
        });
    }

    [Fact]
    public void 按钮点击入口与语言重绘保持当前面板一致()
    {
        DesktopFixture.Run(window =>
        {
            var navigation = (PanelNavigationView)window.FindName("RootNavigation");
            var localizer = new Localizer();
            navigation.SelectionChanged += (_, _) => PanelNavigationPresenter.Show(window, (navigation.SelectedItem as NavigationViewItem)?.Tag?.ToString(), localizer);
            var item = (PanelNavigationItem)window.FindName("WifiNavButton");
            // 调用按钮的虚方法入口，不伪造硬件鼠标输入或操作其他窗口。
            typeof(PanelNavigationItem).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(item, null);
            Assert.Same(item, navigation.SelectedItem);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("WifiPanel")).Visibility);
            localizer.SetLanguage("en-US");
            PanelNavigationPresenter.Show(window, (navigation.SelectedItem as NavigationViewItem)?.Tag?.ToString(), localizer);
            Assert.Equal(localizer.T("Wifi"), ((System.Windows.Controls.TextBlock)window.FindName("PageTitleText")).Text);
            Assert.Same(item, navigation.SelectedItem);
        });
    }

    [Fact]
    public void 诊断标签切换同步标题下操作组()
    {
        DesktopFixture.Run(window =>
        {
            var localizer = new Localizer();
            DesktopFixture.ShowPage(window, "ToolsPanel");
            var tabs = (System.Windows.Controls.TabControl)window.FindName("ToolsTabControl");
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ToolsPageActions")).Visibility);
            tabs.SelectedItem = window.FindName("WebsiteToolsTab");
            PanelNavigationPresenter.Show(window, "ToolsPanel", localizer);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("WebsitePageActions")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("ToolsPageActions")).Visibility);
            tabs.SelectedItem = window.FindName("IpInfoToolsTab");
            PanelNavigationPresenter.Show(window, "ToolsPanel", localizer);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("IpInfoPageActions")).Visibility);
            tabs.SelectedItem = window.FindName("ReportToolsTab");
            PanelNavigationPresenter.Show(window, "ToolsPanel", localizer);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ReportPageActions")).Visibility);
            PanelNavigationPresenter.Show(window, "ToolsPanel", localizer);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ReportPageActions")).Visibility);
        });
    }

    [Theory]
    [InlineData("WebsiteToolsTab", "WebsitePageActions")]
    [InlineData("IpInfoToolsTab", "IpInfoPageActions")]
    [InlineData("ReportToolsTab", "ReportPageActions")]
    public void 离开诊断子标签后其他页面不残留诊断操作组(string tabName, string actionName)
    {
        DesktopFixture.Run(window =>
        {
            var localizer = new Localizer();
            var tabs = (System.Windows.Controls.TabControl)window.FindName("ToolsTabControl");
            var actionBar = (System.Windows.Controls.Panel)window.FindName("PageActionBar");
            foreach (var (page, expectedAction) in new[]
            {
                ("StatusPanel", (string?)null), ("WifiPanel", "WifiPageActions"),
                ("QualityPanel", "QualityPageActions"), ("DnsPanel", "DnsPageActions"),
                ("NetworkPanel", "NetworkPageActions"), ("PolicyPanel", "PolicyPageActions"),
                ("LogPanel", "LogPageActions"), ("SettingsPanel", (string?)null)
            })
            {
                PanelNavigationPresenter.Show(window, "ToolsPanel", localizer);
                tabs.SelectedItem = window.FindName(tabName);
                PanelNavigationPresenter.Show(window, "ToolsPanel", localizer);
                Assert.Equal(Visibility.Visible, ((UIElement)window.FindName(actionName)).Visibility);

                PanelNavigationPresenter.Show(window, page, localizer);
                foreach (FrameworkElement action in actionBar.Children)
                    Assert.Equal(action.Name == expectedAction ? Visibility.Visible : Visibility.Collapsed, action.Visibility);
                Assert.Equal(expectedAction is null ? Visibility.Collapsed : Visibility.Visible, actionBar.Visibility);
            }
            PanelNavigationPresenter.Show(window, "ToolsPanel", localizer);
            Assert.Equal(Visibility.Visible, ((UIElement)window.FindName(actionName)).Visibility);
        });
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
