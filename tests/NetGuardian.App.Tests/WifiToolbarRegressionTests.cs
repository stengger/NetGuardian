using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WifiToolbarRegressionTests
{
    [Theory]
    [InlineData(false, false, "ScanButton")]
    [InlineData(false, true, "AddSelectedButton")]
    [InlineData(true, true, "ConnectButton")]
    public void 主按钮随选择场景切换且仅保留一个强调色(bool connectAvailable, bool addAvailable, string expected)
    {
        DesktopFixture.Run(window =>
        {
            WifiWorkspaceLayoutTests.Populate(window);
            var nearby = (DataGrid)window.FindName("NearbyGrid");
            nearby.SelectedIndex = addAvailable ? 0 : -1;
            ((Button)window.FindName("ConnectButton")).IsEnabled = connectAvailable;
            ((Button)window.FindName("AddSelectedButton")).IsEnabled = addAvailable;
            WifiWorkspacePresentation.ApplyActions(window, false);
            DesktopFixture.Layout(window, 1180, 758);
            var accent = (Brush)window.FindResource("WifiActionAccentBrush");
            var primary = new[] { "ScanButton", "ConnectButton", "AddSelectedButton" }
                .Select(n => (Button)window.FindName(n)).Where(b => ReferenceEquals(b.Background, accent)).ToArray();
            Assert.Equal(expected, Assert.Single(primary).Name);
            foreach (var button in DesktopFixture.Descendants((DependencyObject)window.FindName("WifiPageActions")).OfType<Button>().Where(b => b.IsVisible))
                Assert.Equal(32, button.ActualHeight);
            var remove = (Button)window.FindName("RemoveButton");
            Assert.Same(window.FindResource("CardStrokeColorDefaultBrush"), remove.BorderBrush);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("HighContrast")]
    public void 排序选项与数字列完整显示且对齐一致(string theme)
    {
        DesktopFixture.Run(window =>
        {
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme)));
            WifiWorkspaceLayoutTests.Populate(window);
            var sort = (ComboBox)window.FindName("NearbySortComboBox");
            foreach (var index in new[] { 0, 1 })
            {
                sort.SelectedIndex = index;
                DesktopFixture.Layout(window, 1040, 660);
                var selectedText = ((ComboBoxItem)sort.SelectedItem).Content?.ToString();
                var selectedLabels = DesktopFixture.Descendants(sort).OfType<TextBlock>().Where(t => t.IsVisible && t.Text == selectedText).ToArray();
                Assert.NotEmpty(selectedLabels);
                foreach (var text in selectedLabels)
                {
                    var measure = new FormattedText(text.Text, text.Language.GetEquivalentCulture(), text.FlowDirection,
                        new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
                        text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
                    Assert.True(measure.WidthIncludingTrailingWhitespace + 4 <= text.ActualWidth, $"排序项缺少余量：{text.Text}");
                }
            }
            var table = (DataGrid)window.FindName("ProfileGrid");
            var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
            var priority = Assert.IsType<TextBlock>(table.Columns[2].GetCellContent(row));
            Assert.Equal(TextAlignment.Right, priority.TextAlignment);
            var header = DesktopFixture.Descendants(table).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>()
                .Single(h => h.Column == table.Columns[2]);
            Assert.Equal(HorizontalAlignment.Right, header.HorizontalContentAlignment);
        });
    }

    [Theory]
    [InlineData(false, -1, "SelectProfileFirst", "SelectProfileFirst")]
    [InlineData(false, 0, "AlreadyFirstProfile", "AlreadyLastProfile")]
    [InlineData(true, 0, "ManualNetworkRunning", "ManualNetworkRunning")]
    public void 禁用原因复用原文且不改变操作可用性(bool busy, int index, string upKey, string downKey)
    {
        DesktopFixture.Run(window =>
        {
            WifiWorkspaceLayoutTests.Populate(window);
            foreach (var name in new[] { "MoveUpButton", "MoveDownButton", "AddSelectedButton", "RemoveButton" })
                ((Button)window.FindName(name)).IsEnabled = false;
            var localizer = new Localizer();
            WifiWorkspacePresentation.ApplyDisabledReasons(window, localizer, busy, index, null);
            Assert.Equal(localizer.T(upKey), ((Button)window.FindName("MoveUpButton")).ToolTip);
            Assert.Equal(localizer.T(downKey), ((Button)window.FindName("MoveDownButton")).ToolTip);
            foreach (var name in new[] { "MoveUpButton", "MoveDownButton", "AddSelectedButton", "RemoveButton" })
            {
                var button = (Button)window.FindName(name);
                Assert.False(button.IsEnabled);
                Assert.True(ToolTipService.GetShowOnDisabled(button));
                Assert.False(string.IsNullOrEmpty(button.ToolTip?.ToString()));
            }
        });
    }
}
