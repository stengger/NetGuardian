using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetGuardian.App.Presentation;
using NetGuardian.App.Views;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Network;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class ChineseTypographyTests
{
    [Fact]
    public void 四种界面外壳明确采用简体中文回退字体()
    {
        DesktopFixture.Run(window =>
        {
            var dialog = new AddWifiDialog(new Localizer(), null);
            var menu = new TrayContextMenu();
            var speed = new SpeedTestView();
            foreach (var control in new Control[] { dialog, menu, speed })
                foreach (var dictionary in window.Resources.MergedDictionaries) control.Resources.MergedDictionaries.Add(dictionary);
            foreach (var control in new Control[] { window, dialog, menu, speed })
            {
                Assert.Contains("Microsoft YaHei UI", control.FontFamily.Source);
                // 刻意使用英文语言，确保中文网络名称不再落入日文/繁体回退。
                var group = new DrawingGroup();
                using (var context = group.Open()) context.DrawText(new FormattedText("网络诊断连接测速恢复设置验证",
                    CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight, new Typeface(control.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    14, Brushes.Black, 1), new Point());
                var fonts = GlyphFonts(group).ToArray();
                Assert.NotEmpty(fonts);
                Assert.All(fonts, font => Assert.Contains("MSYH", font.ToUpperInvariant()));
            }
        });
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 独立窗口与测速和托盘跟随实际界面语言(string language)
    {
        DesktopFixture.Run(window =>
        {
            var localizer = new Localizer(); localizer.SetLanguage(language);
            var dialog = new AddWifiDialog(localizer, null);
            var speed = new SpeedTestView();
            speed.Update(new SpeedTestSession(), localizer, TimeSpan.Zero);
            var menu = new TrayContextMenu();
            using var icon = new System.Windows.Forms.NotifyIcon();
            var presentation = MonitorStatusPresentation.Create(false, false, null, DateTimeOffset.Now, localizer);
            TrayMonitorPresenter.Apply(icon, menu, presentation, localizer);
            foreach (var element in new FrameworkElement[] { dialog, speed, menu })
                Assert.Equal(language.ToLowerInvariant(), element.Language.IetfLanguageTag.ToLowerInvariant());
        });
    }

    private static IEnumerable<string> GlyphFonts(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun.GlyphTypeface.FontUri.ToString();
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) foreach (var font in GlyphFonts(child)) yield return font;
    }
}
