using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using System.IO;
using NetGuardian.App.Presentation;
using NetGuardian.App.Views;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Runtime;
using Xunit;
using Forms = System.Windows.Forms;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class TrayContextMenuTests
{
    [Theory]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Dark, "zh-CN")]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Light, "en-US")]
    public void 托盘菜单主题布局和后台禁用原因保持一致(Wpf.Ui.Appearance.ApplicationTheme theme, string language)
    {
        DesktopFixture.Run(window =>
        {
            var menu = new TrayContextMenu();
            foreach (var dictionary in window.Resources.MergedDictionaries) menu.Resources.MergedDictionaries.Add(dictionary);
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            using var tray = new Forms.NotifyIcon();
            var now = DateTimeOffset.Now;
            var background = new GuardianStatus(1, now.AddHours(-1), now, "Running", false);
            var state = MonitorStatusPresentation.Create(false, false, background, now, localizer);
            TrayMonitorPresenter.Apply(tray, menu, state, localizer);
            menu.ApplyTemplate();
            menu.Measure(new Size(320, 600));
            menu.Arrange(new Rect(0, 0, menu.DesiredSize.Width, menu.DesiredSize.Height));
            menu.UpdateLayout();
            Assert.Equal(state.ActivityText, menu.StatusText);
            Assert.False(menu.GetCommand("StartMonitor").IsEnabled);
            Assert.Contains(state.Hint, menu.GetCommand("Stop").ToolTip!.ToString());
            Assert.Equal(localizer.T("ShowWindow"), menu.GetCommand("ShowWindow").Header);
            Assert.InRange(menu.ActualWidth, 220, 320);
            var surface = (Border)menu.Template.FindName("MenuSurface", menu);
            Assert.Equal(new CornerRadius(8), surface.CornerRadius);
            Assert.Equal(((SolidColorBrush)window.FindResource("SurfaceCardBrush")).Color, ((SolidColorBrush)surface.Background).Color);
            foreach (var key in new[] { "ShowWindow", "CheckNow", "StartMonitor", "Stop", "Exit" })
                Assert.True(menu.GetCommand(key).ActualHeight >= 34);
            var directory = Environment.GetEnvironmentVariable("NETGUARDIAN_DESKTOP_PREVIEW_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(menu);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, $"tray-menu-{theme}-{language}.png"));
                encoder.Save(file);
            }
        }, theme);
    }

    [Fact]
    public void 菜单点击只分发可用命令且先关闭菜单()
    {
        DesktopFixture.Run(_ =>
        {
            var menu = new TrayContextMenu();
            var commands = new List<string>();
            menu.CommandRequested += command => { Assert.False(menu.IsOpen); commands.Add(command); };
            menu.GetCommand("StartMonitor").IsEnabled = false;
            menu.GetCommand("StartMonitor").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Empty(commands);
            menu.GetCommand("ShowWindow").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(new[] { "ShowWindow" }, commands);
        });
    }

    [Fact]
    public void 退出释放后不再打开菜单且清除窗口引用()
    {
        DesktopFixture.Run(window =>
        {
            var menu = new TrayContextMenu();
            var host = new TrayMenuHost(window, menu);
            host.Dispose();
            host.Dispose();
            host.ShowAtCursor();
            Assert.False(menu.IsOpen);
            Assert.Null(menu.PlacementTarget);
        });
    }

    [Fact]
    public void 隐藏窗口时真实弹出菜单可关闭并重复打开()
    {
        DesktopFixture.Run(window =>
        {
            var menu = new TrayContextMenu();
            foreach (var dictionary in window.Resources.MergedDictionaries) menu.Resources.MergedDictionaries.Add(dictionary);
            foreach (var key in new[] { "ShowWindow", "CheckNow", "StartMonitor", "Stop", "Exit" }) menu.GetCommand(key).Header = key;
            using var host = new TrayMenuHost(window, menu);
            window.Hide();
            host.ShowAtCursor();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(menu.IsOpen);
            Assert.False(window.IsVisible);
            Assert.NotNull(PresentationSource.FromVisual(menu));
            var commands = new List<string>();
            menu.CommandRequested += command => { Assert.False(menu.IsOpen); commands.Add(command); };
            menu.GetCommand("ShowWindow").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(new[] { "ShowWindow" }, commands);
            host.ShowAtCursor();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(menu.IsOpen);
            var keyEvent = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(menu)!, Environment.TickCount, Key.Escape)
            { RoutedEvent = Keyboard.KeyDownEvent };
            menu.RaiseEvent(keyEvent);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.False(menu.IsOpen);
            host.ShowAtCursor();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(menu.IsOpen);
            host.Dispose();
            Assert.False(menu.IsOpen);
        });
    }
}
