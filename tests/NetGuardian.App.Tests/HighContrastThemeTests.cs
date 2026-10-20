using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetGuardian.App.Presentation;
using Wpf.Ui.Appearance;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class HighContrastThemeTests
{
    [Fact]
    public void 已显示的状态画刷在主题更换后立即重解析()
    {
        DesktopFixture.Run(window =>
        {
            var text = (TextBlock)window.FindName("HealthStateText");
            AppThemePolicy.BindBrush(text, TextBlock.ForegroundProperty, "StatusWarningBrush");
            var oldBrush = text.Foreground;
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(ApplicationTheme.HighContrast));
            window.UpdateLayout();
            Assert.NotSame(oldBrush, text.Foreground);
            Assert.Equal(SystemColors.WindowTextColor, Assert.IsType<SolidColorBrush>(text.Foreground).Color);
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(ApplicationTheme.Light));
            window.UpdateLayout();
            Assert.Equal((Color)ColorConverter.ConvertFromString("#A16207"), Assert.IsType<SolidColorBrush>(text.Foreground).Color);
        });
    }

    [Theory]
    [InlineData("light", ApplicationTheme.Light)]
    [InlineData("dark", ApplicationTheme.Dark)]
    [InlineData("auto", ApplicationTheme.Unknown)]
    public void 高对比优先且退出后恢复原主题选择(string mode, ApplicationTheme expected)
    {
        Assert.Equal(expected, AppThemePolicy.Resolve(mode, false));
        var applied = new List<ApplicationTheme>();
        foreach (var highContrast in new[] { false, true, false })
            AppThemePolicy.Apply(mode, highContrast, applied.Add, () => applied.Add(ApplicationTheme.Unknown));
        Assert.Equal(new[] { expected, ApplicationTheme.HighContrast, expected }, applied);
        Assert.Equal(ApplicationTheme.HighContrast, AppThemePolicy.Resolve(mode, true));
        Assert.Equal(expected, AppThemePolicy.Resolve(mode, false));
    }

    [Fact]
    public void 自定义高对比资源覆盖全部语义颜色并动态跟随系统色()
    {
        DesktopFixture.Run(window =>
        {
            var normal = AppThemePolicy.CreatePalette(ApplicationTheme.Dark);
            var contrast = AppThemePolicy.CreatePalette(ApplicationTheme.HighContrast);
            foreach (var key in normal.Keys.Cast<object>()) Assert.True(contrast.Contains(key), $"缺少 {key}");
            window.Resources.MergedDictionaries.Add(contrast);
            var brush = Assert.IsType<SolidColorBrush>(window.FindResource("SurfaceCardBrush"));
            Assert.Equal(SystemColors.WindowColor, brush.Color);
            Assert.Equal(SystemColors.WindowTextColor, ((SolidColorBrush)window.FindResource("StatusDangerBrush")).Color);
            Assert.Equal(SystemColors.WindowTextColor, ((SolidColorBrush)window.FindResource("RowSelectionStrokeBrush")).Color);
            // 模拟系统资源键更新，验证不是载入时复制颜色；不修改用户的系统主题。
            contrast[SystemColors.WindowColorKey] = Colors.Magenta;
            Assert.Equal(Colors.Magenta, brush.Color);
        });
    }

    [Fact]
    public void 选中网络在高对比下保留文字和可见边界()
    {
        DesktopFixture.Run(window =>
        {
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(ApplicationTheme.HighContrast));
            DesktopFixture.ShowPage(window, "WifiPanel");
            var grid = (DataGrid)window.FindName("ProfileGrid");
            grid.ItemsSource = new[] { new NetGuardian.Core.Models.WifiProfile { Ssid = "高对比网络", Enabled = true } };
            grid.SelectedIndex = 0;
            DesktopFixture.Layout(window, 1040, 660);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.Equal(new Thickness(1), row.BorderThickness);
            Assert.Equal(SystemColors.WindowTextColor, Assert.IsType<SolidColorBrush>(row.BorderBrush).Color);
            Assert.True(row.ActualHeight >= 38);
        });
    }

    [Theory]
    [InlineData("HCWhite", "#000000", "#FFFFFF", "#FFFF00", "#000000")]
    [InlineData("HCWhite", "#FFFFFF", "#000000", "#000080", "#FFFFFF")]
    [InlineData("HCBlack", "#000000", "#FFFFFF", "#FFFF00", "#000000")]
    [InlineData("HCBlack", "#FFFFFF", "#000000", "#000080", "#FFFFFF")]
    public void 库高对比与用户自定义系统色配对(string preset, string background, string foreground, string highlight, string highlightText)
    {
        DesktopFixture.Run(window =>
        {
            // 与库管理器的具体资源入口一致；ThemesDictionary 不支持直接赋 HighContrast。
            window.Resources.MergedDictionaries[0] = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Wpf.Ui;component/Resources/Theme/{preset}.xaml")
            };
            var palette = AppThemePolicy.CreatePalette(ApplicationTheme.HighContrast);
            palette[SystemColors.WindowColorKey] = (Color)ColorConverter.ConvertFromString(background);
            palette[SystemColors.WindowTextColorKey] = (Color)ColorConverter.ConvertFromString(foreground);
            palette[SystemColors.HighlightColorKey] = (Color)ColorConverter.ConvertFromString(highlight);
            palette[SystemColors.HighlightTextColorKey] = (Color)ColorConverter.ConvertFromString(highlightText);
            window.Resources.MergedDictionaries.Add(palette);
            // 夹具初始位图衬底是固定深色，切换后改用动态资源；正式窗口由库管理背景。
            ((Panel)window.Content).SetResourceReference(Panel.BackgroundProperty, "ApplicationBackgroundBrush");
            DesktopFixture.ShowPage(window, "WifiPanel");
            var grid = (DataGrid)window.FindName("ProfileGrid");
            grid.ItemsSource = new[] { new NetGuardian.Core.Models.WifiProfile { Ssid = "系统对比度主题 · 自定义网络", Enabled = true } };
            grid.SelectedIndex = 0;
            DesktopFixture.Layout(window, 1040, 660);
            var text = DesktopFixture.Descendants(grid).OfType<TextBlock>().Single(item => item.Text == "系统对比度主题 · 自定义网络");
            Assert.Equal((Color)ColorConverter.ConvertFromString(foreground), Assert.IsType<SolidColorBrush>(text.Foreground).Color);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.Equal((Color)ColorConverter.ConvertFromString(background), Assert.IsType<SolidColorBrush>(row.Background).Color);
            var button = (Button)window.FindName("CheckNowButton");
            Assert.Equal((Color)ColorConverter.ConvertFromString(foreground), Assert.IsType<SolidColorBrush>(button.Foreground).Color);
            Assert.Equal((Color)ColorConverter.ConvertFromString(background), Assert.IsType<SolidColorBrush>(button.Background).Color);
            var primary = new Wpf.Ui.Controls.Button { Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Content = "主要操作" };
            var panel = (Panel)window.Content;
            panel.Children.Add(primary);
            window.UpdateLayout();
            Assert.Equal((Color)ColorConverter.ConvertFromString(highlightText), Assert.IsType<SolidColorBrush>(primary.Foreground).Color);
            Assert.Equal((Color)ColorConverter.ConvertFromString(highlight), Assert.IsType<SolidColorBrush>(primary.Background).Color);
            panel.Children.Remove(primary);
            window.UpdateLayout();
            DesktopFixture.Save(window, $"high-contrast-{preset}-{(background == "#000000" ? "black" : "white")}.png");
        });
    }
}
