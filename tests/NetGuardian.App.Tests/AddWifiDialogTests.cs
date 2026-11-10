using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using System.Windows.Media;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using PasswordBox = Wpf.Ui.Controls.PasswordBox;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class AddWifiDialogTests
{
    [Fact]
    public void 空名称禁用保存并说明原因且认证仍可保留未知原值()
    {
        DesktopFixture.Run(owner =>
        {
            var localizer = new Localizer();
            var network = new WifiNetwork("", 80, "厂商原始认证值", "AES", true, true);
            var dialog = new AddWifiDialog(localizer, network) { Owner = owner, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000 };
            foreach (var dictionary in owner.Resources.MergedDictionaries) dialog.Resources.MergedDictionaries.Add(dictionary);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var save = DesktopFixture.Descendants(dialog).OfType<Button>().Single(button => button.IsDefault);
                Assert.False(save.IsEnabled);
                Assert.Equal(localizer.T("SsidRequired"), save.ToolTip);
                Assert.True(ToolTipService.GetShowOnDisabled(save));
                var authentication = Assert.Single(DesktopFixture.Descendants(dialog).OfType<ComboBox>());
                Assert.True(authentication.IsEditable);
                Assert.Equal("厂商原始认证值", dialog.Authentication);
                authentication.Text = "原样输入值";
                Assert.Equal("原样输入值", dialog.Authentication);
                var ssid = DesktopFixture.Descendants(dialog).OfType<TextBox>().First();
                ssid.Text = "测试网络";
                Assert.True(save.IsEnabled);
                Assert.Null(save.ToolTip);
                ssid.Text = "  ";
                Assert.False(save.IsEnabled);
            }
            finally { dialog.Close(); }
        });
    }

    [Theory]
    [InlineData("zh-CN", 14)]
    [InlineData("en-US", 14)]
    [InlineData("zh-CN", 20)]
    [InlineData("en-US", 20)]
    public void 添加无线表单正文可滚动且输入具有可访问标签(string language, double fontSize)
    {
        DesktopFixture.Run(owner =>
        {
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            var dialog = CreateDialog(owner, localizer);
            dialog.FontSize = fontSize;
            dialog.SizeToContent = SizeToContent.Manual;
            dialog.Height = 430;
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var body = Assert.Single(DesktopFixture.Descendants(dialog).OfType<ScrollViewer>(),
                    viewer => viewer.Content is Panel panel && DesktopFixture.Descendants(panel).OfType<PasswordBox>().Any());
                var password = Assert.Single(DesktopFixture.Descendants(dialog).OfType<PasswordBox>());
                var inputs = DesktopFixture.Descendants(dialog).OfType<Control>()
                    .Where(input => input.Name is "SsidInput" or "AuthenticationInput" or "PasswordInput").ToArray();
                Assert.Equal(3, inputs.Length);
                foreach (var input in inputs)
                    Assert.False(string.IsNullOrWhiteSpace((AutomationProperties.GetLabeledBy(input) as TextBlock)?.Text));
                Assert.Contains(localizer.T("PasswordHint"), AutomationProperties.GetHelpText(password));
                Assert.True(body.ScrollableHeight > 0, "紧凑弹窗正文必须可以滚动以读取末尾说明。");
                body.ScrollToEnd();
                dialog.UpdateLayout();
                var hint = DesktopFixture.Descendants(dialog).OfType<TextBlock>().Single(text => text.Text == localizer.T("PasswordHint"));
                var hintBounds = hint.TransformToAncestor(dialog).TransformBounds(new Rect(hint.RenderSize));
                var bodyBounds = body.TransformToAncestor(dialog).TransformBounds(new Rect(body.RenderSize));
                Assert.True(hintBounds.Left >= bodyBounds.Left && hintBounds.Right <= bodyBounds.Right, $"说明横向超出正文：{hintBounds} / {bodyBounds}");
                var save = DesktopFixture.Descendants(dialog).OfType<Button>().Single(button => button.IsDefault);
                var saveBounds = save.TransformToAncestor(dialog).TransformBounds(new Rect(save.RenderSize));
                Assert.True(hintBounds.Bottom <= saveBounds.Top, $"正文说明与操作区重叠：{hintBounds} / {saveBounds}");
                Assert.True(saveBounds.Bottom <= dialog.ActualHeight);
                var saveContent = DesktopFixture.Descendants(save).OfType<ContentPresenter>().First();
                Assert.True(saveContent.ActualHeight >= saveContent.DesiredSize.Height, "操作按钮文字高度不足。");
                DesktopFixture.Save(dialog, $"add-wifi-compact-{language}-{fontSize}.png");
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void 密码显隐不改变返回值且认证候选仅使用已有观测值()
    {
        DesktopFixture.Run(owner =>
        {
            var localizer = new Localizer();
            var network = new WifiNetwork("测试网络", 80, "原有认证", "AES", true, true);
            var dialog = new AddWifiDialog(localizer, network, "Test-only-password", new[] { "观测认证", "观测认证", "" })
            {
                Owner = owner, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000
            };
            foreach (var dictionary in owner.Resources.MergedDictionaries) dialog.Resources.MergedDictionaries.Add(dictionary);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var password = Assert.Single(DesktopFixture.Descendants(dialog).OfType<PasswordBox>());
                Assert.True(password.RevealButtonEnabled);
                Assert.False(password.IsPasswordRevealed);
                password.Focus();
                dialog.UpdateLayout();
                var reveal = DesktopFixture.Descendants(password).OfType<Button>().Single(button => button.Name == "RevealButton");
                Assert.True(reveal.IsTabStop);
                Assert.Equal(localizer.T("Password"), AutomationProperties.GetName(reveal));
                var invoke = (IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(reveal)!.GetPattern(PatternInterface.Invoke)!;
                invoke.Invoke();
                dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.True(password.IsPasswordRevealed);
                Assert.Equal("Test-only-password", dialog.Password);
                invoke.Invoke();
                dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.False(password.IsPasswordRevealed);
                Assert.Equal("Test-only-password", dialog.Password);
                password.Password = "";
                Assert.Null(dialog.Password);
                var authentication = Assert.Single(DesktopFixture.Descendants(dialog).OfType<ComboBox>());
                Assert.Equal(new[] { "观测认证", "原有认证" }, authentication.Items.Cast<string>());
                authentication.SelectedItem = "观测认证";
                Assert.Equal("观测认证", dialog.Authentication);
            }
            finally { dialog.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 添加无线模态校验聚焦与保存取消完整闭环(bool save)
    {
        DesktopFixture.Run(owner =>
        {
            var localizer = new Localizer();
            var dialog = CreateDialog(owner, localizer);
            dialog.SizeToContent = SizeToContent.Manual;
            dialog.Height = 430;
            Exception? failure = null;
            dialog.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var ssid = DesktopFixture.Descendants(dialog).OfType<TextBox>().First();
                    Assert.True(ssid.IsKeyboardFocused || ssid.IsFocused, "初始焦点应落在 SSID 输入框。");
                    var ok = DesktopFixture.Descendants(dialog).OfType<Button>().Single(button => button.IsDefault);
                    var body = Assert.Single(DesktopFixture.Descendants(dialog).OfType<ScrollViewer>(),
                        viewer => viewer.Content is Panel panel && DesktopFixture.Descendants(panel).OfType<PasswordBox>().Any());
                    body.ScrollToEnd();
                    dialog.UpdateLayout();
                    ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(dialog.IsVisible);
                    var error = DesktopFixture.Descendants(dialog).OfType<TextBlock>().Single(text => text.Text == localizer.T("SsidRequired"));
                    Assert.True(error.IsVisible);
                    Assert.NotNull(UIElementAutomationPeer.FromElement(error));
                    Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(error));
                    Assert.True(ssid.IsKeyboardFocused || ssid.IsFocused);
                    dialog.UpdateLayout();
                    var errorBounds = error.TransformToAncestor(body).TransformBounds(new Rect(error.RenderSize));
                    Assert.True(errorBounds.Top >= 0 && errorBounds.Bottom <= body.ActualHeight, $"SSID 校验提示被正文裁剪：{errorBounds} / {body.ActualHeight}");
                    ssid.Text = "测试网络";
                    Assert.Equal(Visibility.Collapsed, error.Visibility);
                    Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(ssid)));
                    if (save) ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else DesktopFixture.Descendants(dialog).OfType<Button>().Single(button => button.IsCancel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception) { failure = exception; }
                finally { if (dialog.IsVisible) dialog.Close(); }
            }, DispatcherPriority.ContextIdle);
            var result = dialog.ShowDialog();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.Equal(save, result);
            Assert.Equal("测试网络", dialog.Ssid);
            Assert.True(dialog.SaveToWindows);
        });
    }

    private static AddWifiDialog CreateDialog(Window owner, Localizer localizer)
    {
        var dialog = new AddWifiDialog(localizer, null)
        {
            Owner = owner, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000, Top = 0, ShowInTaskbar = false
        };
        foreach (var dictionary in owner.Resources.MergedDictionaries) dialog.Resources.MergedDictionaries.Add(dictionary);
        SetPreviewAccent(dialog, Wpf.Ui.Appearance.ApplicationTheme.Dark);
        return dialog;
    }

    private static void SetPreviewAccent(Window dialog, Wpf.Ui.Appearance.ApplicationTheme theme)
    {
        // 夹具没有 Application 启动过程，显式提供 AccentColorManager 正常生成的颜色键。
        // 采用可重复的蓝色样本，不冒充当前用户的系统强调色。
        var color = (Color)ColorConverter.ConvertFromString(theme == Wpf.Ui.Appearance.ApplicationTheme.Dark ? "#6EA8FF" : "#2563EB");
        dialog.Resources["AccentFillColorDefault"] = color;
        dialog.Resources["AccentFillColorSecondary"] = Color.FromArgb(229, color.R, color.G, color.B);
        dialog.Resources["AccentFillColorTertiary"] = Color.FromArgb(204, color.R, color.G, color.B);
        dialog.Resources["SystemAccentColorPrimary"] = color;
        // 共享主题画刷内部的颜色引用依赖 Application；无 Application 夹具提供最终画刷样本。
        dialog.Resources["AccentButtonBackground"] = new SolidColorBrush(color);
        dialog.Resources["AccentButtonBackgroundPointerOver"] = new SolidColorBrush(Color.FromArgb(229, color.R, color.G, color.B));
        dialog.Resources["AccentButtonBackgroundPressed"] = new SolidColorBrush(Color.FromArgb(204, color.R, color.G, color.B));
    }

    [Theory]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Dark, "zh-CN")]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Dark, "en-US")]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Light, "zh-CN")]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Light, "en-US")]
    public void 添加无线弹窗使用原生圆角窗口且保留输入与操作(Wpf.Ui.Appearance.ApplicationTheme theme, string language)
    {
        DesktopFixture.Run(owner =>
        {
            var network = new WifiNetwork("长名称 Wi-Fi 5 GHz", 80, "WPA2-PSK", "AES", true, true);
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            var dialog = new AddWifiDialog(localizer, network)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = 0,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            // Owner 不会继承资源；产品从 Application 获取样式，夹具需显式提供同一主题资源。
            foreach (var dictionary in owner.Resources.MergedDictionaries)
                dialog.Resources.MergedDictionaries.Add(dictionary);
            SetPreviewAccent(dialog, theme);
            try
            {
                var fluent = Assert.IsAssignableFrom<FluentWindow>(dialog);
                Assert.Equal(WindowCornerPreference.Round, fluent.WindowCornerPreference);
                Assert.True(fluent.ExtendsContentIntoTitleBar);
                Assert.False(dialog.AllowsTransparency);
                dialog.Show();
                dialog.UpdateLayout();
                Assert.Equal(((SolidColorBrush)owner.FindResource("SurfaceCardBrush")).Color, ((SolidColorBrush)dialog.Background).Color);
                DesktopFixture.Save(dialog, $"add-wifi-{theme}-{language}-top.png");
                Assert.Equal(WindowStyle.SingleBorderWindow, dialog.WindowStyle);
                Assert.NotNull(WindowChrome.GetWindowChrome(dialog));
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                {
                    var result = DwmGetWindowAttribute(new WindowInteropHelper(dialog).Handle, 33, out var preference, sizeof(int));
                    Assert.Equal(0, result);
                    Assert.Equal(2, preference);
                }
                Assert.Equal(network.Ssid, dialog.Ssid);
                Assert.Equal(network.Authentication, dialog.Authentication);
                Assert.True(dialog.SaveToWindows);
                var buttons = DesktopFixture.Descendants(dialog).OfType<Button>().Where(button => button.IsDefault || button.IsCancel).ToArray();
                Assert.Equal(2, buttons.Length);
                var primary = buttons.Single(button => button.IsDefault);
                var foreground = ((SolidColorBrush)primary.Foreground).Color;
                var background = ((SolidColorBrush)primary.Background).Color;
                Assert.True(Contrast(foreground, background) >= 4.5, $"主要操作对比度不足：{foreground} / {background}");
                foreach (var button in buttons)
                {
                    var bounds = button.TransformToAncestor(dialog).TransformBounds(new Rect(button.RenderSize));
                    Assert.True(bounds.Top >= 0 && bounds.Bottom <= dialog.ActualHeight, $"按钮超出弹窗：{bounds}");
                }
                var body = Assert.Single(DesktopFixture.Descendants(dialog).OfType<ScrollViewer>(),
                    viewer => viewer.Content is Panel panel && DesktopFixture.Descendants(panel).OfType<PasswordBox>().Any());
                Assert.Equal(0, body.ScrollableHeight);
                body.ScrollToEnd();
                dialog.UpdateLayout();
                DesktopFixture.Save(dialog, $"add-wifi-{theme}-{language}-bottom.png");
                var nextTheme = theme == Wpf.Ui.Appearance.ApplicationTheme.Light ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light;
                // 保留控件模板和尺寸令牌；产品切换主题也不会清空全部资源字典。
                dialog.Resources.MergedDictionaries[0] = new Wpf.Ui.Markup.ThemesDictionary { Theme = nextTheme };
                dialog.Resources.MergedDictionaries[^1] = new ResourceDictionary { Source = new Uri($"pack://application:,,,/NetGuardian;component/Themes/Palette.{nextTheme}.xaml") };
                SetPreviewAccent(dialog, nextTheme);
                dialog.UpdateLayout();
                Assert.Equal(((SolidColorBrush)dialog.FindResource("SurfaceCardBrush")).Color, ((SolidColorBrush)dialog.Background).Color);
                var password = Assert.Single(DesktopFixture.Descendants(dialog).OfType<PasswordBox>());
                Assert.Equal(((SolidColorBrush)dialog.FindResource("ControlFillColorDefaultBrush")).Color, ((SolidColorBrush)password.Background).Color);
            }
            finally { dialog.Close(); }
        }, theme);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    private static double Contrast(Color foreground, Color background)
    {
        static double Linear(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
        static double Light(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        return (Math.Max(Light(foreground), Light(background)) + 0.05) / (Math.Min(Light(foreground), Light(background)) + 0.05);
    }
}
