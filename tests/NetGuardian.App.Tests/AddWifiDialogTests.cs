using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class AddWifiDialogTests
{
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
            try
            {
                var fluent = Assert.IsAssignableFrom<FluentWindow>(dialog);
                Assert.Equal(WindowCornerPreference.Round, fluent.WindowCornerPreference);
                Assert.True(fluent.ExtendsContentIntoTitleBar);
                Assert.False(dialog.AllowsTransparency);
                dialog.Show();
                dialog.UpdateLayout();
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
                foreach (var button in buttons)
                {
                    var bounds = button.TransformToAncestor(dialog).TransformBounds(new Rect(button.RenderSize));
                    Assert.True(bounds.Top >= 0 && bounds.Bottom <= dialog.ActualHeight, $"按钮超出弹窗：{bounds}");
                }
            }
            finally { dialog.Close(); }
        }, theme);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
