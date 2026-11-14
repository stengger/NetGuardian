using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WifiDetailRegressionTests
{
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("HighContrast")]
    public void 连接和优先级状态标签使用统一尺寸与填充模板(string theme)
    {
        DesktopFixture.Run(window =>
        {
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme)));
            DesktopFixture.ShowPage(window, "WifiPanel");
            var table = (DataGrid)window.FindName("NearbyGrid");
            table.ItemsSource = new[] { new { Ssid = "网络一", Authentication = "WPA2-Personal", SignalPercent = 91, PriorityBadge = "可添加", IsInPriorityList = false } };
            WifiRowPresentation.SetCurrentSsid(table, "网络一");
            ((Border)window.FindName("CurrentWifiConnectedBadge")).Visibility = Visibility.Visible;
            DesktopFixture.Layout(window, 1600, 1000);
            var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
            var badges = DesktopFixture.Descendants(row).OfType<Border>().Where(b =>
                b.Style == window.TryFindResource("WifiConnectedBadgeStyle") || b.Style == window.TryFindResource("WifiPriorityBadgeStyle")).ToArray();
            Assert.Equal(2, badges.Length);
            foreach (var badge in badges.Append((Border)window.FindName("CurrentWifiConnectedBadge")))
            {
                Assert.Equal(22, badge.ActualHeight);
                Assert.Equal(new Thickness(0), badge.BorderThickness);
                Assert.Equal((CornerRadius)window.FindResource("CornerControl"), badge.CornerRadius);
                Assert.Equal((Thickness)window.FindResource("WifiStatusBadgePadding"), badge.Padding);
                Assert.Equal(12d, TextElement.GetFontSize(badge));
            }
        }, theme == "Light" ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }

    [Fact]
    public void 详情内容在展开和折叠过程中保留过渡且最终恢复自然高度()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var table = (DataGrid)window.FindName("ProfileGrid");
            table.ItemsSource = new[] { new WifiProfile { Ssid = "短网络", Priority = 1, CredentialSaved = true } };
            DesktopFixture.Layout(window, 1180, 758);
            var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
            var presenter = DesktopFixture.Descendants(row).OfType<DataGridDetailsPresenter>().Single();
            row.DetailsVisibility = Visibility.Visible;
            Wait(60);
            if (SystemParameters.ClientAreaAnimation)
                Assert.InRange(presenter.Opacity, .01, .99);
            ExpansionInteractionTests.Settle(window);
            Assert.Equal(1, presenter.Opacity);
            Assert.True(double.IsNaN(presenter.Height));
            row.DetailsVisibility = Visibility.Collapsed;
            Wait(60);
            if (SystemParameters.ClientAreaAnimation)
            {
                Assert.True(presenter.IsVisible);
                Assert.InRange(presenter.Opacity, .01, .99);
            }
            ExpansionInteractionTests.Settle(window);
            Assert.Equal(40, row.ActualHeight);
        });
    }

    private static void Wait(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    [Theory]
    [InlineData("Dark", "NearbyGrid")]
    [InlineData("Light", "NearbyGrid")]
    [InlineData("HighContrast", "NearbyGrid")]
    [InlineData("Dark", "ProfileGrid")]
    [InlineData("Light", "ProfileGrid")]
    [InlineData("HighContrast", "ProfileGrid")]
    public void 被连接徽标挤占的名称和隐藏字段可在紧凑详情中读取(string theme, string name)
    {
        DesktopFixture.Run(window =>
        {
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme)));
            DesktopFixture.ShowPage(window, "WifiPanel");
            const string ssid = "Audit-01-ABCDEFGHIJKLMNOPQRST";
            var table = (DataGrid)window.FindName(name);
            table.ItemsSource = name == "ProfileGrid"
                ? new[] { new WifiProfile { Ssid = ssid, CredentialSaved = true, Priority = 1, FailureCount = 2 } }
                : new object[] { new { Ssid = ssid, Authentication = "WPA2-Personal", SignalPercent = 91, PriorityBadge = "可添加" } };
            WifiRowPresentation.SetCurrentSsid(table, ssid);
            DesktopFixture.Layout(window, 1180, 758);
            // 强制保留窄名称和隐藏字段场景，避免响应式列预算掩盖详情回归。
            table.Columns[name == "ProfileGrid" ? 1 : 0].Width = 220;
            if (name == "ProfileGrid")
            {
                table.Columns[3].Visibility = Visibility.Collapsed;
                table.Columns[5].Visibility = Visibility.Collapsed;
            }
            var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
            row.DetailsVisibility = Visibility.Visible;
            ExpansionInteractionTests.Settle(window);
            var details = DesktopFixture.Descendants(row).OfType<TableRowDetails>().Single();
            var values = details.Children.OfType<TextBox>().Where(v => v.IsVisible).ToArray();
            Assert.Contains(values, value => value.Text == ssid);
            Assert.DoesNotContain(values, value => value.Text == "⋮⋮");
            Assert.DoesNotContain(details.Children.OfType<TextBlock>().Where(t => t.IsVisible), text => string.IsNullOrWhiteSpace(text.Text));
            foreach (var value in values)
            {
                var label = details.Children.OfType<TextBlock>().Single(t => t.IsVisible && Grid.GetRow(t) == Grid.GetRow(value));
                Assert.Equal(0, Grid.GetColumn(label));
                Assert.Equal(1, Grid.GetColumn(value));
                Assert.InRange(value.ActualHeight + value.Margin.Top + value.Margin.Bottom, 1, 28);
                Assert.Equal(value.Text, value.ToolTip);
                Assert.True(value.IsReadOnly && value.Focusable);
            }
            if (name == "ProfileGrid")
            {
                Assert.Contains(values, value => value.Text == "已保存");
                Assert.Contains(values, value => value.Text == "2");
            }
            Assert.DoesNotContain(DesktopFixture.Descendants(details).OfType<ScrollBar>(), b => b.IsVisible);
            Assert.InRange(row.ActualHeight, 40, 160);
            DesktopFixture.Save(window, $"compact-details-{theme}-{name}.png");
            row.DetailsVisibility = Visibility.Collapsed;
            ExpansionInteractionTests.Settle(window);
            Assert.Equal(40, row.ActualHeight);
        }, theme == "Light" ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark);
    }

    [Fact]
    public void 完整短字段不在详情重复而隐藏字段仍保留()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var table = (DataGrid)window.FindName("ProfileGrid");
            table.ItemsSource = new[] { new WifiProfile { Ssid = "短网络", Priority = 1, CredentialSaved = true, FailureCount = 3 } };
            DesktopFixture.Layout(window, 1180, 758);
            var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
            row.DetailsVisibility = Visibility.Visible;
            ExpansionInteractionTests.Settle(window);
            var details = DesktopFixture.Descendants(row).OfType<TableRowDetails>().Single();
            var labels = details.Children.OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToArray();
            Assert.DoesNotContain("SSID", labels);
            Assert.DoesNotContain("优先级", labels);
            Assert.DoesNotContain("启用", labels);
            Assert.Contains("凭据", labels);
            Assert.Contains("失败次数", labels);
        });
    }
}
