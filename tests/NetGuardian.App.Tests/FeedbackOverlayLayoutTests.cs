using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class FeedbackOverlayLayoutTests
{
    [Theory]
    [InlineData(1180, 758, "Dark")]
    [InlineData(1040, 660, "Dark")]
    [InlineData(800, 660, "Dark")]
    [InlineData(1180, 758, "Light")]
    [InlineData(1040, 660, "Light")]
    [InlineData(800, 660, "Light")]
    [InlineData(1180, 758, "HighContrast")]
    [InlineData(1040, 660, "HighContrast")]
    [InlineData(800, 660, "HighContrast")]
    public void 提示出现展开和关闭不改变无线列表或侧栏布局(double width, double height, string theme)
    {
        DesktopFixture.Run(window =>
        {
            window.MinWidth = Math.Min(width, 1040);
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme)));
            WifiWorkspaceLayoutTests.Populate(window);
            DesktopFixture.Layout(window, width, height);
            var before = Snapshot(window);
            var columns = new[] { "NearbyGrid", "ProfileGrid" }.SelectMany(name => ((DataGrid)window.FindName(name)).Columns).Select(c => c.ActualWidth).ToArray();
            var host = (FeedbackPresenter)window.FindName("RootSnackbar");
            DesktopFixture.Save(window, $"overlay-{width}-{theme}-before.png");
            host.ShowMessage("后台监控中", "后台守护已接管网络监控，此界面不会重复切换网络。", "Warning");
            DesktopFixture.Layout(window, width, height);
            ExpansionInteractionTests.Settle(window);
            DesktopFixture.Save(window, $"overlay-{width}-{theme}-shown.png");
            AssertStable(window, before, columns);

            for (var i = 0; i < 2; i++)
                host.ShowMessage("审查提示" + i, "摘要\n" + new string('长', 500), "Danger");
            foreach (var expander in DesktopFixture.Descendants(host).OfType<Expander>()) expander.IsExpanded = true;
            DesktopFixture.Layout(window, width, height);
            AssertStable(window, before, columns);
            DesktopFixture.Save(window, $"overlay-{width}-{theme}-expanded.png");
            var scroll = (ScrollViewer)host.Parent;
            Assert.True(scroll.ScrollableHeight > 0, "多条长提示仍需能滚动读取。");
            var bounds = scroll.TransformToAncestor(window).TransformBounds(new Rect(scroll.RenderSize));
            Assert.True(bounds.Left >= 0 && bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight);
            var actions = (FrameworkElement)window.FindName("PageActionBar");
            Assert.False(bounds.IntersectsWith(actions.TransformToAncestor(window).TransformBounds(new Rect(actions.RenderSize))));

            foreach (var close in DesktopFixture.Descendants(host).OfType<Button>().Where(b => b.Name == "FeedbackClose").ToArray())
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ExpansionInteractionTests.Settle(window);
            DesktopFixture.Layout(window, width, height);
            Assert.Equal(Visibility.Collapsed, host.Visibility);
            AssertStable(window, before, columns);
            if (width >= 960)
                foreach (var name in new[] { "NearbyGrid", "ProfileGrid" })
                    Assert.True(WifiWorkspaceLayoutTests.CompleteRows((DataGrid)window.FindName(name)) >= 6);
        }, theme == "Light" ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }

    private static Dictionary<string, Rect> Snapshot(Window window) => new[]
    {
        "WifiPanel", "WifiListsGrid", "NearbyGrid", "ProfileGrid", "CurrentWifiCard", "PageActionBar",
        "RootNavigation", "SidebarSpeedCard", "SettingsNavButton"
    }.ToDictionary(name => name, name =>
    {
        var element = (FrameworkElement)window.FindName(name);
        return element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
    });

    private static void AssertStable(Window window, Dictionary<string, Rect> before, double[] columns)
    {
        var after = Snapshot(window);
        foreach (var (name, bounds) in before)
            Assert.True(bounds == after[name], $"提示改变了 {name} 布局：出现前 {bounds}，出现后 {after[name]}。");
        Assert.Equal(columns, new[] { "NearbyGrid", "ProfileGrid" }.SelectMany(name => ((DataGrid)window.FindName(name)).Columns).Select(c => c.ActualWidth).ToArray());
    }
}
