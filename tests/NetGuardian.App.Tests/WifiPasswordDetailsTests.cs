using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WifiPasswordDetailsTests
{
    [Fact]
    public void 已保存网络的详情提供显示密码入口且默认不展示密码()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "WifiPanel");
            var table = (DataGrid)window.FindName("ProfileGrid");
            table.ItemsSource = new[] { new WifiProfile { Ssid = "测试网络", CredentialSaved = true, Security = "WPA2-Personal" } };
            DesktopFixture.Layout(window, 1180, 758);
            var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
            row.DetailsVisibility = Visibility.Visible;
            ExpansionInteractionTests.Settle(window);
            var button = DesktopFixture.Descendants(row).OfType<Button>().SingleOrDefault(b => b.Content?.ToString() == "显示密码");
            Assert.NotNull(button);
            Assert.DoesNotContain(DesktopFixture.Descendants(row).OfType<TextBox>(), t => t.Text == "仅供测试的密码");
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("HighContrast")]
    public void 点击才读取目标网络且隐藏后清除可复制文本(string theme)
    {
        DesktopFixture.Run(window =>
        {
            window.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme)));
            var (table, row, details) = OpenDetails(window);
            var calls = 0;
            WifiPasswordDetails.SetPasswordReader(table, (ssid, token) =>
            {
                Assert.Equal("测试网络", ssid);
                calls++;
                return Task.FromResult<string?>("仅供测试的密码");
            });
            var button = details.Children.OfType<Button>().Single();
            var value = details.Children.OfType<TextBox>().Single();
            Assert.Equal(0, calls);
            Click(button, window);
            Assert.Equal(1, calls);
            Assert.Equal("仅供测试的密码", value.Text);
            Assert.True(value.IsVisible && value.IsReadOnly && value.Focusable);
            value.SelectAll();
            Assert.Equal("仅供测试的密码", value.SelectedText);
            Assert.Equal("隐藏密码", button.Content);
            DesktopFixture.Save(window, $"wifi-password-{theme}.png");
            Click(button, window);
            Assert.Empty(value.Text);
            Assert.False(value.IsVisible);
            Assert.Equal("显示密码", button.Content);
        });
    }

    [Fact]
    public void 收起详情取消读取且迟到的结果不能重新显示()
    {
        DesktopFixture.Run(window =>
        {
            var (table, row, details) = OpenDetails(window);
            var pending = new TaskCompletionSource<string?>();
            CancellationToken requestToken = default;
            WifiPasswordDetails.SetPasswordReader(table, (_, token) => { requestToken = token; return pending.Task; });
            Click(details.Children.OfType<Button>().Single(), window);
            row.DetailsVisibility = Visibility.Collapsed;
            Assert.True(requestToken.IsCancellationRequested);
            pending.SetResult("不应出现的密码");
            ExpansionInteractionTests.Settle(window);
            row.DetailsVisibility = Visibility.Visible;
            ExpansionInteractionTests.Settle(window);
            Assert.Empty(details.Children.OfType<TextBox>().Single().Text);
            Assert.Equal("显示密码", details.Children.OfType<Button>().Single().Content);
        });
    }

    [Fact]
    public void 切换网络清除旧密码且重新点击读取新网络()
    {
        DesktopFixture.Run(window =>
        {
            var (table, row, details) = OpenDetails(window);
            WifiPasswordDetails.SetPasswordReader(table, (ssid, _) => Task.FromResult<string?>(ssid + "密码"));
            Click(details.Children.OfType<Button>().Single(), window);
            Assert.Equal("测试网络密码", details.Children.OfType<TextBox>().Single().Text);
            var previous = details;
            table.ItemsSource = new[] { new WifiProfile { Ssid = "新网络", CredentialSaved = true, Security = "WPA2-Personal" } };
            DesktopFixture.Layout(window, 1180, 758);
            row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
            row.DetailsVisibility = Visibility.Visible;
            ExpansionInteractionTests.Settle(window);
            Assert.Empty(previous.Children.OfType<TextBox>().Single().Text);
            details = DesktopFixture.Descendants(row).OfType<WifiPasswordDetails>().Single();
            Assert.Empty(details.Children.OfType<TextBox>().Single().Text);
            Click(details.Children.OfType<Button>().Single(), window);
            Assert.Equal("新网络密码", details.Children.OfType<TextBox>().Single().Text);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 读取失败只展示提示且允许重试(bool throws)
    {
        DesktopFixture.Run(window =>
        {
            var (table, _, details) = OpenDetails(window);
            WifiPasswordDetails.SetPasswordReader(table, (_, _) => throws
                ? Task.FromException<string?>(new InvalidOperationException("不能显示的底层敏感内容"))
                : Task.FromResult<string?>(null));
            Click(details.Children.OfType<Button>().Single(), window);
            Assert.Empty(details.Children.OfType<TextBox>().Single().Text);
            var status = details.Children.OfType<TextBlock>().Single(t => t.IsVisible && t.Text.Contains("未能读取"));
            Assert.Contains("测试网络", status.Text);
            Assert.DoesNotContain("底层敏感内容", status.Text);
            WifiPasswordDetails.SetPasswordReader(table, (_, _) => Task.FromResult<string?>("重试成功"));
            Click(details.Children.OfType<Button>().Single(), window);
            Assert.Equal("重试成功", details.Children.OfType<TextBox>().Single().Text);
        });
    }

    [Fact]
    public void 开放网络说明没有密码且未保存网络没有查看入口()
    {
        DesktopFixture.Run(window =>
        {
            var (table, row, details) = OpenDetails(window);
            WifiPasswordDetails.SetPasswordReader(table, (_, _) => throw new Exception("开放网络不应读取密码"));
            // 模拟虚拟化复用时，详情呈现器传入的新数据上下文。
            details.DataContext = new WifiProfile { Ssid = "开放网络", CredentialSaved = true, Security = "Open" };
            ExpansionInteractionTests.Settle(window);
            Click(details.Children.OfType<Button>().Single(), window);
            Assert.Contains(details.Children.OfType<TextBlock>(), t => t.IsVisible && t.Text == "开放网络，无密码。");
            details.DataContext = new WifiProfile { Ssid = "未保存", CredentialSaved = false };
            ExpansionInteractionTests.Settle(window);
            Assert.False(details.IsVisible);
        });
    }

    private static (DataGrid Table, DataGridRow Row, WifiPasswordDetails Details) OpenDetails(Window window)
    {
        DesktopFixture.ShowPage(window, "WifiPanel");
        var table = (DataGrid)window.FindName("ProfileGrid");
        table.ItemsSource = new[] { new WifiProfile { Ssid = "测试网络", CredentialSaved = true, Security = "WPA2-Personal" } };
        DesktopFixture.Layout(window, 1180, 758);
        var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
        row.DetailsVisibility = Visibility.Visible;
        ExpansionInteractionTests.Settle(window);
        return (table, row, DesktopFixture.Descendants(row).OfType<WifiPasswordDetails>().Single());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 查看未选中行后选择另一行也会清除密码或取消读取(bool pendingRead)
    {
        DesktopFixture.Run(window =>
        {
            var (table, row, details) = OpenDetails(window);
            var first = (WifiProfile)table.Items[0];
            var second = new WifiProfile { Ssid = "另一网络", CredentialSaved = true };
            table.ItemsSource = new[] { first, second };
            DesktopFixture.Layout(window, 1180, 758);
            row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
            row.DetailsVisibility = Visibility.Visible;
            ExpansionInteractionTests.Settle(window);
            details = DesktopFixture.Descendants(row).OfType<WifiPasswordDetails>().Single();
            Assert.Null(table.SelectedItem);
            CancellationToken requestToken = default;
            var pending = new TaskCompletionSource<string?>();
            WifiPasswordDetails.SetPasswordReader(table, (_, token) =>
            {
                requestToken = token;
                return pendingRead ? pending.Task : Task.FromResult<string?>("旧行密码");
            });
            Click(details.Children.OfType<Button>().Single(), window);
            table.SelectedItem = second;
            Assert.Empty(details.Children.OfType<TextBox>().Single().Text);
            if (pendingRead)
            {
                Assert.True(requestToken.IsCancellationRequested);
                pending.SetResult("迟到密码");
                ExpansionInteractionTests.Settle(window);
                Assert.Empty(details.Children.OfType<TextBox>().Single().Text);
            }
        });
    }

    private static void Click(Button button, Window window)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }
}
