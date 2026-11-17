using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class QuickToolsLayoutTests
{
    [Theory]
    [InlineData(1180, 760, "zh-CN", "Dark")]
    [InlineData(960, 500, "zh-CN", "Dark")]
    [InlineData(960, 500, "en-US", "Dark")]
    [InlineData(960, 500, "zh-CN", "Light")]
    [InlineData(960, 500, "en-US", "Light")]
    [InlineData(960, 500, "zh-CN", "HighContrast")]
    public void 未操作时用途说明完整显示在风险提示下方(double width, double height, string language, string theme)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960;
            window.MinHeight = 500;
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(
                Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme)));
            var hint = 准备快捷页(window, language);
            DesktopFixture.Layout(window, width, height);

            Assert.True(hint.IsVisible, "尚无操作结果时应显示用途说明。");
            Assert.Equal(TextWrapping.Wrap, hint.TextWrapping);
            var warning = (TextBlock)window.FindName("HighRiskText");
            var surface = DesktopFixture.Descendants((TabControl)window.FindName("ToolsTabControl"))
                .OfType<Border>().Single(element => element.Name == "ToolsContentSurface");
            var warningBounds = warning.TransformToAncestor(surface).TransformBounds(new Rect(warning.RenderSize));
            var hintBounds = hint.TransformToAncestor(surface).TransformBounds(new Rect(hint.RenderSize));
            Assert.True(hintBounds.Top >= warningBounds.Bottom,
                $"用途说明与风险提示重叠：说明={hintBounds}，风险提示={warningBounds}");
            Assert.True(hintBounds.Left >= 0 && hintBounds.Right <= surface.ActualWidth + 1
                && hintBounds.Bottom <= surface.ActualHeight + 1,
                $"用途说明超出正文区域：说明={hintBounds}，正文={surface.RenderSize}");
            Assert.True(hint.ActualHeight >= hint.DesiredSize.Height - hint.Margin.Top - hint.Margin.Bottom - 1,
                "用途说明不能被截断。");
            DesktopFixture.Save(window, $"quick-tools-empty-{language}-{theme}-{width}.png");
        }, theme == "Light" ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }

    [Theory]
    [InlineData("zh-CN", "en-US")]
    [InlineData("en-US", "zh-CN")]
    public void 结果出现后隐藏用途说明且更换说明文案不覆盖结果(string language, string nextLanguage)
    {
        DesktopFixture.Run(window =>
        {
            var hint = 准备快捷页(window, language);
            var output = (TextBlock)window.FindName("ToolsOutputText");
            DesktopFixture.Layout(window, 1040, 660);
            Assert.Equal(Visibility.Visible, hint.Visibility);

            output.Text = "检测完成：网络可用。";
            DesktopFixture.Layout(window, 1040, 660);
            Assert.Equal(Visibility.Collapsed, hint.Visibility);
            DesktopFixture.Save(window, $"quick-tools-result-{language}.png");

            准备快捷页(window, nextLanguage);
            Assert.StartsWith(nextLanguage == "zh-CN" ? "选择上方操作开始：" : "Choose an action above to begin:", hint.Text);
            DesktopFixture.Layout(window, 1040, 660);
            Assert.Equal("检测完成：网络可用。", output.Text);
            Assert.Equal(Visibility.Collapsed, hint.Visibility);

            output.Text = string.Empty;
            DesktopFixture.Layout(window, 1040, 660);
            Assert.Equal(Visibility.Visible, hint.Visibility);
        });
    }

    private static TextBlock 准备快捷页(Window window, string language)
    {
        var localizer = new Localizer();
        localizer.SetLanguage(language);
        var hint = Assert.IsType<TextBlock>(window.FindName("QuickToolsHintText"));
        hint.Text = localizer.T("QuickToolsHint");
        Assert.NotEqual("QuickToolsHint", hint.Text);
        ((TextBlock)window.FindName("HighRiskText")).Text = localizer.T("HighRiskReserved");
        foreach (var (name, key) in new[]
        {
            ("ToolsCheckButton", "CheckNow"), ("ToolsScanButton", "Scan"),
            ("ToolsNetworkSettingsButton", "OpenNetworkSettings")
        })
            ((Button)window.FindName(name)).Content = localizer.T(key);
        foreach (var (name, key) in new[]
        {
            ("QuickToolsTab", "QuickTools"), ("SpeedToolsTab", "SpeedTools"),
            ("WebsiteToolsTab", "WebsiteTools"), ("IpInfoToolsTab", "IpInfoTools"),
            ("ReportToolsTab", "ReportTools")
        })
            ((TabItem)window.FindName(name)).Header = localizer.T(key);
        ((TabControl)window.FindName("ToolsTabControl")).SelectedItem = window.FindName("QuickToolsTab");
        PanelNavigationPresenter.Show(window, "ToolsPanel", localizer);
        return hint;
    }
}
