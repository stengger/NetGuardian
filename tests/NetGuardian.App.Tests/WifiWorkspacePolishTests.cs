using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetGuardian.App.Presentation;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WifiWorkspacePolishTests
{
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("HighContrast")]
    public void 当前连接徽标代替重复信号图标且选中行不加粗(string theme)
    {
        DesktopFixture.Run(window =>
        {
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme)));
            WifiWorkspaceLayoutTests.Populate(window);
            foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
            {
                var table = (DataGrid)window.FindName(name);
                var ssid = name == "NearbyGrid" ? "实验室网络-1-长名称012345678901234567890" : "恢复网络-1-长名称012345678901234567890";
                WifiRowPresentation.SetCurrentSsid(table, ssid);
                table.SelectedIndex = 0;
            }
            DesktopFixture.Layout(window, 1180, 758);
            foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
            {
                var table = (DataGrid)window.FindName(name);
                var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.Equal(FontWeights.Normal, row.FontWeight);
                Assert.Equal(40, row.ActualHeight);
                var icons = DesktopFixture.Descendants(row).OfType<SymbolIcon>().Where(i => i.IsVisible).ToArray();
                Assert.Equal(name == "NearbyGrid" ? 1 : 0, icons.Length);
                Assert.Contains(DesktopFixture.Descendants(row).OfType<TextBlock>(), t => t.IsVisible && t.Text == "已连接");
                Assert.Equal((Thickness)window.FindResource("StrokeSelectedIndicator"), row.BorderThickness);
                Assert.Equal(0, WifiSectionLayoutTests.Scroll(table).ScrollableWidth);
            }
            Assert.Equal(20, ((Thickness)window.FindResource("WifiSectionGap")).Bottom);
            Assert.Equal(8, ((Thickness)window.FindResource("WifiSectionContentGap")).Top);
            DesktopFixture.Save(window, $"workspace-selected-{theme}.png");
        }, theme == "Light" ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 工具栏只有一个主色且连接忙碌不改变尺寸(bool canConnect)
    {
        DesktopFixture.Run(window =>
        {
            WifiWorkspaceLayoutTests.Populate(window);
            ((Button)window.FindName("ConnectButton")).IsEnabled = canConnect;
            var presentation = typeof(WifiRowPresentation).Assembly.GetType("NetGuardian.App.Presentation.WifiWorkspacePresentation");
            Assert.NotNull(presentation);
            presentation!.GetMethod("ApplyActions")!.Invoke(null, new object[] { window, false });
            DesktopFixture.Layout(window, 1180, 758);
            var connect = (Button)window.FindName("ConnectButton");
            var scan = (Button)window.FindName("ScanButton");
            var primary = window.FindResource("WifiToolbarPrimaryButtonStyle");
            Assert.Equal(canConnect, ReferenceEquals(connect.Style, primary));
            Assert.Equal(!canConnect, ReferenceEquals(scan.Style, primary));
            var size = connect.RenderSize;
            using (new ButtonActivityPresentation(connect, "连接中…"))
            {
                presentation.GetMethod("ApplyActions")!.Invoke(null, new object[] { window, true });
                window.UpdateLayout(); Assert.Equal(size, connect.RenderSize);
                Assert.Same(primary, connect.Style);
                DesktopFixture.Save(window, "workspace-connecting.png");
            }
            foreach (var name in new[] { "ScanButton", "ConnectButton", "AddSelectedButton", "WifiMoreButton", "MoveUpButton", "MoveDownButton", "RemoveButton", "NearbyFilterButton" })
                Assert.Equal(32, ((FrameworkElement)window.FindName(name)).ActualHeight);
        });
    }

    [Fact]
    public void 扫描错误保留上次扫描时间且不挤占六行视口()
    {
        DesktopFixture.Run(window =>
        {
            WifiWorkspaceLayoutTests.Populate(window);
            WifiWorkspacePresentation.ApplyScanStatus(window, "最后扫描：16:40:00", string.Join("\n", Enumerable.Repeat("扫描失败：访问被拒绝。", 20)), true);
            DesktopFixture.Layout(window, 1180, 758);
            Assert.True(((Button)window.FindName("WifiScanStateButton")).IsVisible);
            Assert.Equal("最后扫描：16:40:00", ((TextBlock)window.FindName("WifiLastScanText")).Text);
            Assert.True(WifiWorkspaceLayoutTests.CompleteRows((DataGrid)window.FindName("NearbyGrid")) >= 6);
            WifiWorkspacePresentation.ApplyScanStatus(window, "最后扫描：16:40:00", "扫描完成", false);
            window.UpdateLayout();
            Assert.False(((Button)window.FindName("WifiScanStateButton")).IsVisible);
        });
    }
}
