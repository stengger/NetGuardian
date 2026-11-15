using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class EmptyStateLayoutTests
{
    [Theory]
    [InlineData(960, 500, "Dark")]
    [InlineData(1040, 660, "Dark")]
    [InlineData(1180, 760, "Dark")]
    [InlineData(960, 500, "Light")]
    [InlineData(1040, 660, "Light")]
    [InlineData(1180, 760, "Light")]
    [InlineData(960, 500, "HighContrast")]
    [InlineData(1040, 660, "HighContrast")]
    [InlineData(1180, 760, "HighContrast")]
    public void 网络质量空态完整位于表头下方且没有内部滚动(double width, double height, string theme)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = 960;
            window.MinHeight = 500;
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(
                Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme)));
            var grid = PrepareQualityEmptyState(window);
            DesktopFixture.Layout(window, width, height);
            var guide = (Button)window.FindName("QualityEmptyAction");
            guide.BringIntoView();
            window.UpdateLayout();
            DesktopFixture.Save(window, $"quality-empty-{theme}-{width}-100.png");
            SaveScaled(window, $"quality-empty-{theme}-{width}", 1.25);
            SaveScaled(window, $"quality-empty-{theme}-{width}", 1.5);
            AssertQualityEmptyStateBounds(window, grid);
            var outer = (ScrollViewer)window.FindName("QualityBodyScrollViewer");
            var buttonBounds = guide.TransformToAncestor(outer).TransformBounds(new Rect(guide.RenderSize));
            Assert.True(buttonBounds.Top >= -1 && buttonBounds.Bottom <= outer.ActualHeight + 1,
                $"空态操作不可达：{buttonBounds}，页面视口高度={outer.ActualHeight}");
            var inner = DesktopFixture.Descendants(grid).OfType<ScrollViewer>().Single(viewer => viewer.Name == "DG_ScrollViewer");
            Assert.Equal(0, inner.ScrollableHeight);
            Assert.Equal(0, inner.ScrollableWidth);
        }, theme == "Light" ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(64)]
    public void 网络质量空态随表头高度和长文案自适应(double headerHeight)
    {
        DesktopFixture.Run(window =>
        {
            var grid = PrepareQualityEmptyState(window);
            grid.ColumnHeaderHeight = headerHeight;
            var message = (TextBlock)window.FindName("QualityEmptyText");
            message.Text = "尚未检测。";
            DesktopFixture.Layout(window, 1040, 660);
            var shortHeight = grid.ActualHeight;
            message.Text = string.Join("\n", Enumerable.Repeat(new Localizer().T("NoQualityRows"), 3));
            DesktopFixture.Layout(window, 1040, 660);
            DesktopFixture.Save(window, $"quality-empty-long-header-{headerHeight}.png");
            Assert.True(grid.ActualHeight > shortHeight, "长文案必须撑开空数据区。");
            AssertQualityEmptyStateBounds(window, grid);
        });
    }

    [Fact]
    public void 网络质量检测结果出现后空态退出布局且没有多余留白()
    {
        DesktopFixture.Run(window =>
        {
            var grid = PrepareQualityEmptyState(window);
            DesktopFixture.Layout(window, 1040, 660);
            ((TextBlock)window.FindName("QualityEmptyText")).Visibility = Visibility.Collapsed;
            grid.ItemsSource = Enumerable.Range(1, 3).Select(i => new
            {
                Name = $"HTTPS 检查-{i}", ResultText = "通过", LatencyText = "10 ms", Detail = "HTTP 200"
            }).ToArray();
            DesktopFixture.Layout(window, 1040, 660);
            Assert.False(((Button)window.FindName("QualityEmptyAction")).IsVisible);
            var header = DesktopFixture.Descendants(grid).OfType<DataGridColumnHeadersPresenter>().First();
            var rows = DesktopFixture.Descendants(grid).OfType<DataGridRow>().ToArray();
            Assert.Equal(3, rows.Length);
            Assert.InRange(grid.ActualHeight, header.ActualHeight + rows.Sum(row => row.ActualHeight),
                header.ActualHeight + rows.Sum(row => row.ActualHeight) + 2);
            DesktopFixture.Save(window, "quality-results-after-empty.png");
        });
    }

    [Theory]
    [InlineData("Quality", "QualityPanel", "")]
    [InlineData("Website", "ToolsPanel", "WebsiteToolsTab")]
    [InlineData("IpInfo", "ToolsPanel", "IpInfoToolsTab")]
    [InlineData("Report", "ToolsPanel", "ReportToolsTab")]
    public void 空结果有可聚焦引导且跟随原操作禁用状态(string prefix, string page, string tab)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, page);
            if (tab.Length > 0) ((TabControl)window.FindName("ToolsTabControl")).SelectedItem = window.FindName(tab);
            var message = (TextBlock)window.FindName(prefix + "EmptyText");
            message.Text = "尚未检测。开始检查后显示结果。";
            message.Visibility = Visibility.Visible;
            var original = (Button)window.FindName(prefix == "IpInfo" ? "IpInfoRefreshButton" : prefix + "RunButton");
            original.Content = "开始检查";
            DesktopFixture.Layout(window, 1040, 660);
            var guide = Assert.IsType<Button>(window.FindName(prefix + "EmptyAction"));
            Assert.True(guide.IsVisible && guide.Focusable && guide.ActualHeight >= 32);
            original.IsEnabled = false;
            window.UpdateLayout();
            Assert.False(guide.IsEnabled);
            DesktopFixture.Save(window, $"empty-{prefix}.png");
        });
    }

    private static DataGrid PrepareQualityEmptyState(Window window)
    {
        DesktopFixture.ShowPage(window, "QualityPanel");
        var localizer = new Localizer();
        var grid = (DataGrid)window.FindName("QualityProbeGrid");
        foreach (var (name, key) in new[] { ("QualityProbeColumn", "Probe"), ("QualityResultColumn", "Result"),
                     ("QualityLatencyColumn", "ProbeElapsed"), ("QualityDetailColumn", "Detail") })
            ((DataGridColumn)window.FindName(name)).Header = localizer.T(key);
        var message = (TextBlock)window.FindName("QualityEmptyText");
        message.Text = localizer.T("NoQualityRows");
        message.Visibility = Visibility.Visible;
        ((Button)window.FindName("QualityRunButton")).Content = localizer.T("RunQualityCheck");
        return grid;
    }

    private static void AssertQualityEmptyStateBounds(Window window, DataGrid grid)
    {
        var message = (TextBlock)window.FindName("QualityEmptyText");
        var icon = ((StackPanel)message.Parent).Children.OfType<Wpf.Ui.Controls.SymbolIcon>().Single();
        var guide = (Button)window.FindName("QualityEmptyAction");
        var header = DesktopFixture.Descendants(grid).OfType<DataGridColumnHeadersPresenter>().First();
        var headerBounds = header.TransformToAncestor(grid).TransformBounds(new Rect(header.RenderSize));
        var host = (FrameworkElement)grid.Parent;
        foreach (var element in new FrameworkElement[] { icon, message, guide })
        {
            var bounds = element.TransformToAncestor(host).TransformBounds(new Rect(element.RenderSize));
            var gridBounds = grid.TransformToAncestor(host).TransformBounds(new Rect(grid.RenderSize));
            Assert.True(bounds.Top >= gridBounds.Top + headerBounds.Bottom - 1,
                $"{element.GetType().Name}覆盖表头：空态={bounds}，表头底部={gridBounds.Top + headerBounds.Bottom}");
            Assert.True(bounds.Left >= gridBounds.Left - 1 && bounds.Right <= gridBounds.Right + 1 && bounds.Bottom <= gridBounds.Bottom + 1,
                $"空态内容超出数据区：空态={bounds}，表格={gridBounds}");
            Assert.True(element.ActualWidth > 0 && element.ActualHeight > 0);
        }
        Assert.True(message.ActualHeight >= message.DesiredSize.Height - message.Margin.Top - message.Margin.Bottom - 1,
            "空态说明被截高。");
        Assert.True(guide.Focusable && guide.ActualHeight >= 32);
    }

    private static void SaveScaled(Window window, string name, double scale)
    {
        var directory = Environment.GetEnvironmentVariable("NETGUARDIAN_DESKTOP_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * scale), (int)(window.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, $"{name}-{scale * 100:0}.png"));
        encoder.Save(file);
    }
}
