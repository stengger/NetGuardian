using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WifiColumnBudgetTests
{
    [Theory]
    [InlineData(1040, 660, false)]
    [InlineData(1180, 758, true)]
    [InlineData(1600, 1000, true)]
    public void 必要列在最小窗口完整保留且预算不足时改为上下布局(double width, double height, bool horizontal)
    {
        DesktopFixture.Run(window =>
        {
            WifiWorkspaceLayoutTests.Populate(window);
            DesktopFixture.Layout(window, width, height);
            var region = (FrameworkElement)window.FindName("WifiListsGrid");
            var priority = (FrameworkElement)window.FindName("PriorityNetworksCard");
            Assert.Equal(horizontal, priority.TranslatePoint(new Point(), region).X > 1);
            var table = (DataGrid)window.FindName("NearbyGrid");
            Assert.All(table.Columns, c => Assert.Equal(Visibility.Visible, c.Visibility));
            Assert.True(table.Columns[2].ActualWidth >= 120);
            Assert.True(table.Columns[3].ActualWidth >= 80);
            foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
            {
                var grid = (DataGrid)window.FindName(name);
                Assert.True(grid.Columns[grid.Name == "NearbyGrid" ? 0 : 1].ActualWidth >= 112);
                Assert.True(grid.RowHeaderWidth + grid.Columns.Where(c => c.Visibility == Visibility.Visible).Sum(c => c.ActualWidth)
                    <= grid.ActualWidth - 10, $"{name} 列侵入滚动条：{grid.ActualWidth}");
                Assert.Equal(0, WifiSectionLayoutTests.Scroll(grid).ScrollableWidth);
            }
            var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
            var security = (TextBlock)table.Columns[2].GetCellContent(row);
            var text = new System.Windows.Media.FormattedText(security.Text, security.Language.GetEquivalentCulture(), security.FlowDirection,
                new System.Windows.Media.Typeface(security.FontFamily, security.FontStyle, security.FontWeight, security.FontStretch),
                security.FontSize, security.Foreground, System.Windows.Media.VisualTreeHelper.GetDpi(security).PixelsPerDip);
            Assert.True(text.WidthIncludingTrailingWhitespace <= security.ActualWidth);
            DesktopFixture.Save(window, $"column-budget-{width}.png");
        });
    }

    [Fact]
    public void 管理按钮合并到顶部且分区头辅助区不会挤占标题()
    {
        DesktopFixture.Run(window =>
        {
            WifiWorkspaceLayoutTests.Populate(window);
            DesktopFixture.Layout(window, 1040, 660);
            var toolbar = (FrameworkElement)window.FindName("WifiPageActions");
            foreach (var name in new[] { "MoveUpButton", "MoveDownButton", "RemoveButton" })
                Assert.Contains(DesktopFixture.Descendants(toolbar).OfType<Button>(), b => b.Name == name);
            foreach (var name in new[] { "NearbyNetworksTitle", "PriorityListTitle" })
            {
                var title = (FrameworkElement)window.FindName(name);
                var header = ResultTableInteraction.Ancestor<NetGuardian.App.Views.ListSectionHeader>(title)!;
                var bounds = title.TransformToAncestor(header).TransformBounds(new Rect(title.RenderSize));
                Assert.True(bounds.Left >= 32 && bounds.Right <= header.ActualWidth);
                Assert.True(bounds.Bottom <= 32);
            }
        });
    }
}
