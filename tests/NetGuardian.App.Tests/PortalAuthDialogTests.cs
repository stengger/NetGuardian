using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;
using NetGuardian.Core.Portal;
using Xunit;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using PasswordBox = Wpf.Ui.Controls.PasswordBox;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class PortalAuthDialogTests
{
    [Fact]
    public void 非Https入口未确认信任时阻止保存且保存待验证不标记就绪()
    {
        DesktopFixture.Run(owner =>
        {
            var localizer = new Localizer();
            var dialog = CreateDialog(owner, localizer);
            Exception? failure = null;
            dialog.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var origin = DesktopFixture.Descendants(dialog).OfType<TextBox>().Single(b => b.Name == "PortalOriginInput");
                    var account = DesktopFixture.Descendants(dialog).OfType<TextBox>().Single(b => b.Name == "PortalAccountInput");
                    var password = DesktopFixture.Descendants(dialog).OfType<PasswordBox>().Single(b => b.Name == "PortalPasswordInput");
                    var allowHttp = DesktopFixture.Descendants(dialog).OfType<CheckBox>().Single(b => b.Name == "PortalAllowHttpCheck");
                    var savePending = DesktopFixture.Descendants(dialog).OfType<Button>().Single(b => b.Name == "PortalSavePendingButton");

                    origin.Text = "http://10.0.0.1";
                    account.Text = "student";
                    password.Password = "secret";

                    savePending.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(dialog.IsVisible);

                    allowHttp.IsChecked = true;
                    savePending.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex)
                {
                    failure = ex;
                    if (dialog.IsVisible) dialog.Close();
                }
            }, DispatcherPriority.ContextIdle);

            var result = TestDesktopWindow.ShowDialog(dialog);
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();

            Assert.True(result);
            Assert.NotNull(dialog.ResultConfig);
            Assert.False(dialog.ResultConfig!.Ready);
            Assert.False(dialog.ResultConfig.CanAutoAuthenticate);
            Assert.Equal("http://10.0.0.1", dialog.ResultConfig.AllowedOrigin);
            Assert.Equal("student", dialog.ResultCredential?.Username);
        });
    }

    [Fact]
    public void 检测按钮自动填充候选项且试登验证通过后才标记为就绪()
    {
        DesktopFixture.Run(owner =>
        {
            var localizer = new Localizer();
            var testShouldSucceed = false;
            var inspection = new PortalInspectionResult(
                true,
                "已识别",
                "https://portal.campus.invalid",
                "/eportal/index.jsp",
                "https://portal.campus.invalid",
                "/eportal/InterFace.do",
                true,
                [new PortalDiscoveredControl("#username", "学号", "username")],
                [new PortalDiscoveredControl("#pwd", "密码", "password")],
                [new PortalDiscoveredControl("#isp", "运营商", "select", [new PortalSelectOption("campus", "校园网")])],
                [new PortalDiscoveredControl("#loginBtn", "登录", "submit")]);

            var dialog = new PortalAuthDialog(
                localizer,
                "CampusNet",
                detectHandler: _ => Task.FromResult(new PortalDetectionFeedback(true, "已发现门户", new Uri("https://portal.campus.invalid/eportal/index.jsp?s=1"), inspection)),
                testHandler: (cfg, cred, entry, _) => Task.FromResult(testShouldSucceed
                    ? new PortalTestFeedback(true, "成功", cfg)
                    : new PortalTestFeedback(false, "公网仍不可达")))
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000
            };
            foreach (var dictionary in owner.Resources.MergedDictionaries) dialog.Resources.MergedDictionaries.Add(dictionary);

            Exception? failure = null;
            dialog.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    var detect = DesktopFixture.Descendants(dialog).OfType<Button>().Single(b => b.Name == "PortalDetectButton");
                    var test = DesktopFixture.Descendants(dialog).OfType<Button>().Single(b => b.Name == "PortalTestButton");
                    var account = DesktopFixture.Descendants(dialog).OfType<TextBox>().Single(b => b.Name == "PortalAccountInput");
                    var password = DesktopFixture.Descendants(dialog).OfType<PasswordBox>().Single(b => b.Name == "PortalPasswordInput");

                    detect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(50);

                    var origin = DesktopFixture.Descendants(dialog).OfType<TextBox>().Single(b => b.Name == "PortalOriginInput");
                    Assert.Equal("https://portal.campus.invalid", origin.Text);

                    account.Text = "2026001";
                    password.Password = "pass123";

                    // 试登失败时弹窗保持打开且不标记就绪。
                    test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(50);
                    Assert.True(dialog.IsVisible);
                    Assert.Null(dialog.ResultConfig);

                    testShouldSucceed = true;
                    test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex)
                {
                    failure = ex;
                    if (dialog.IsVisible) dialog.Close();
                }
            }, DispatcherPriority.ContextIdle);

            var result = TestDesktopWindow.ShowDialog(dialog);
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();

            Assert.True(result);
            Assert.True(dialog.ResultConfig?.CanAutoAuthenticate);
            Assert.Equal("/eportal/InterFace.do", dialog.ResultConfig?.AllowedSubmitPath);
        });
    }

    [Fact]
    public void 单按钮放行无需凭据且支持清除已保存配置()
    {
        DesktopFixture.Run(owner =>
        {
            var localizer = new Localizer();
            var existing = new PortalAuthConfig
            {
                Enabled = true,
                Ready = true,
                Mode = PortalAuthMode.OneClickConsent,
                AllowedOrigin = "https://guest.portal.invalid",
                SubmitSelector = "#accept"
            };
            var dialog = CreateDialog(owner, localizer, existing);
            Exception? failure = null;
            dialog.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var inputs = DesktopFixture.Descendants(dialog).OfType<Control>()
                        .Where(c => !string.IsNullOrEmpty(c.Name) && c.Name.StartsWith("Portal", StringComparison.Ordinal) && c.IsVisible && c is TextBox or ComboBox or PasswordBox);
                    foreach (var input in inputs)
                    {
                        Assert.False(string.IsNullOrWhiteSpace((AutomationProperties.GetLabeledBy(input) as TextBlock)?.Text), input.Name);
                    }

                    var clear = DesktopFixture.Descendants(dialog).OfType<Button>().Single(b => b.Name == "PortalClearButton");
                    Assert.True(clear.IsVisible);
                    clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex)
                {
                    failure = ex;
                    if (dialog.IsVisible) dialog.Close();
                }
            }, DispatcherPriority.ContextIdle);

            var result = TestDesktopWindow.ShowDialog(dialog);
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();

            Assert.True(result);
            Assert.True(dialog.ClearRequested);
            Assert.Null(dialog.ResultConfig);
        });
    }

    [Fact]
    public void 行详情控件在未配置时折叠而在已配置时展示脱敏状态()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var details = new WifiPortalDetails();
            Assert.Equal(Visibility.Collapsed, details.Visibility);

            details.PortalAuth = new PortalAuthConfig
            {
                Enabled = true,
                Ready = true,
                Mode = PortalAuthMode.OneClickConsent,
                AllowedOrigin = "https://portal.example.edu",
                SubmitSelector = "#btn"
            };
            Assert.Equal(Visibility.Visible, details.Visibility);

            details.PortalAuth = new PortalAuthConfig
            {
                Enabled = true,
                Ready = true,
                Paused = true,
                PauseReason = "密码错误",
                Mode = PortalAuthMode.OneClickConsent,
                AllowedOrigin = "https://portal.example.edu",
                SubmitSelector = "#btn"
            };
            var texts = DesktopFixture.Descendants(details).OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains(texts, t => t.Contains("密码错误", StringComparison.Ordinal));
        });
    }

    private static PortalAuthDialog CreateDialog(Window owner, Localizer localizer, PortalAuthConfig? existing = null)
    {
        var dialog = new PortalAuthDialog(localizer, "CampusNet", existing)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000
        };
        foreach (var dictionary in owner.Resources.MergedDictionaries) dialog.Resources.MergedDictionaries.Add(dictionary);
        return dialog;
    }
}
