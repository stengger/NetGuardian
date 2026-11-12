using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class FeedbackPresentationTests
{
    [Theory]
    [InlineData("Dark", "Success")]
    [InlineData("Dark", "Info")]
    [InlineData("Dark", "Warning")]
    [InlineData("Dark", "Danger")]
    [InlineData("Light", "Success")]
    [InlineData("Light", "Info")]
    [InlineData("Light", "Warning")]
    [InlineData("Light", "Danger")]
    [InlineData("HighContrast", "Success")]
    [InlineData("HighContrast", "Info")]
    [InlineData("HighContrast", "Warning")]
    [InlineData("HighContrast", "Danger")]
    public void 提示使用中性卡片并且不覆盖页面操作(string theme, string severity)
    {
        DesktopFixture.Run(window =>
        {
            var mode = Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme);
            window.Resources.MergedDictionaries.Add(NetGuardian.App.Presentation.AppThemePolicy.CreatePalette(mode));
            var host = (FrameworkElement)window.FindName("RootSnackbar");
            var show = host.GetType().GetMethod("ShowMessage");
            Assert.NotNull(show);
            show.Invoke(host, new object?[] { "连接", "没有找到可连接的接入点。" + new string('长', 120), severity, null, null, null });
            DesktopFixture.ShowPage(window, "WifiPanel");
            DesktopFixture.Layout(window, 1040, 660);
            var card = DesktopFixture.Descendants(host).OfType<Border>().Single(b => b.Name == "FeedbackCard");
            Assert.Equal(((SolidColorBrush)window.FindResource("SurfaceCardBrush")).Color, ((SolidColorBrush)card.Background).Color);
            var actions = (FrameworkElement)window.FindName("PageActionBar");
            var cardBounds = card.TransformToAncestor(window).TransformBounds(new Rect(card.RenderSize));
            var actionBounds = actions.TransformToAncestor(window).TransformBounds(new Rect(actions.RenderSize));
            Assert.False(cardBounds.IntersectsWith(actionBounds));
            Assert.InRange(card.ActualWidth, 320, 420);
            var body = DesktopFixture.Descendants(card).OfType<TextBlock>().Single(t => t.Name == "FeedbackBody");
            Assert.Equal(TextWrapping.Wrap, body.TextWrapping);
            var close = DesktopFixture.Descendants(card).OfType<Button>().Single(b => b.Name == "FeedbackClose");
            Assert.True(close.MinWidth >= 32);
            DesktopFixture.Save(window, $"feedback-{theme}-{severity}.png");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
    }
}
