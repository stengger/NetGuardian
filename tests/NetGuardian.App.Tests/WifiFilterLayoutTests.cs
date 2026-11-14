using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using Wpf.Ui.Appearance;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WifiFilterLayoutTests
{
    [Theory]
    [InlineData(1040, 660, "Dark")]
    [InlineData(1180, 758, "Dark")]
    [InlineData(1040, 660, "Light")]
    [InlineData(1180, 758, "Light")]
    [InlineData(1040, 660, "HighContrast")]
    [InlineData(1180, 758, "HighContrast")]
    public void 筛选文字与数量徽标居中且输入状态不改变行高(double width, double height, string theme)
    {
        DesktopFixture.Run(window =>
        {
            if (theme == "HighContrast")
                window.Resources.MergedDictionaries[0] = new ResourceDictionary
                { Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Theme/HCBlack.xaml") };
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(Enum.Parse<ApplicationTheme>(theme)));
            DesktopFixture.ShowPage(window, "WifiPanel");
            var nearby = (DataGrid)window.FindName("NearbyGrid");
            nearby.ItemsSource = Enumerable.Range(1, 36).Select(i => new
            { Ssid = $"实验室网络-{i}", SignalPercent = 80, Authentication = "WPA2", PriorityBadge = "可添加" }).ToArray();
            DesktopFixture.Layout(window, width, height);
            var search = (Wpf.Ui.Controls.TextBox)window.FindName("NearbySearchTextBox");
            var sort = (ComboBox)window.FindName("NearbySortComboBox");
            var placeholder = DesktopFixture.Descendants(search).OfType<TextBlock>().Single(e => e.Name == "PlaceholderTextBox");
            var host = DesktopFixture.Descendants(search).OfType<ScrollViewer>().Single(e => e.Name == "PART_ContentHost");
            var badge = (Border)window.FindName("NearbyCountBadge");
            var badgeText = Assert.IsType<TextBlock>(badge.Child);
            var rowHeight = ((FrameworkElement)search.Parent).ActualHeight;
            var initialHeight = search.ActualHeight;
            DesktopFixture.Save(window, $"filter-empty-{theme}-{width}.png");
            AssertCentered(placeholder, search);
            AssertCentered(host, search);
            Assert.InRange(badge.ActualHeight, 16, 32);
            Assert.True(placeholder.ActualWidth >= 48, "搜索占位文字必须完整显示。");
            Assert.Equal(badgeText.ActualHeight + badge.Padding.Top + badge.Padding.Bottom, badge.ActualHeight, 1);
            var selection = DesktopFixture.Descendants(sort).OfType<ContentPresenter>()
                .First(e => e.Content == sort.SelectionBoxItem && e.ActualHeight > 0);
            AssertCentered(selection, sort);
            search.Text = "实验室网络-长名称-012345678901234567890";
            search.Focus();
            window.UpdateLayout();
            DesktopFixture.Save(window, $"filter-text-{theme}-{width}.png");
            AssertCentered(host, search);
            Assert.Equal(Visibility.Collapsed, placeholder.Visibility);
            Assert.Equal(initialHeight, search.ActualHeight);
            Assert.Equal(rowHeight, ((FrameworkElement)search.Parent).ActualHeight);
            search.IsEnabled = false;
            window.UpdateLayout();
            AssertCentered(host, search);
            Assert.Equal(initialHeight, search.ActualHeight);
            DesktopFixture.Save(window, $"filter-disabled-{theme}-{width}.png");
        }, theme == "Light" ? ApplicationTheme.Light : ApplicationTheme.Dark);
    }

    private static void AssertCentered(FrameworkElement content, FrameworkElement control)
    {
        var bounds = content.TransformToAncestor(control).TransformBounds(new Rect(content.RenderSize));
        // 像素取整允许上下留白相差最多 2 DIP，同时保证内容没有越界。
        Assert.InRange(Math.Abs(bounds.Top - (control.ActualHeight - bounds.Bottom)), 0, 2);
        Assert.True(bounds.Top >= 0 && bounds.Bottom <= control.ActualHeight);
    }
}
