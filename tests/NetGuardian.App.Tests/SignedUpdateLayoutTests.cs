using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Updates;
using NetGuardian.Core.Updates;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class SignedUpdateLayoutTests
{
    [Fact]
    public void 开发构建从现有设置入口显示备用源未启用并保留检查按钮()
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "SettingsPanel");
            using var http = new System.Net.Http.HttpClient();
            var version = new Version(1, 2, 0);
            using var session = new UpdateSession(new GitHubUpdateService(http), version);
            SignedUpdateText.Apply(window, session, version, new Localizer(), true, null);
            var check = (Button)window.FindName("UpdateCheckButton");
            Assert.True(check.IsEnabled);
            Assert.Equal(Visibility.Visible, check.Visibility);
            var status = (TextBlock)window.FindName("UpdateStatusText");
            Assert.Contains("GitHub", status.Text);
            Assert.Contains("备用源尚未启用", status.Text);
        });
    }

    [Fact]
    public void 双源失败显示每条线路阶段与过期前检查时间()
    {
        var used = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        var status = new UpdateRouteStatus(UpdateRoute.MirrorDirect, UpdateStep.Download,
            [new(UpdateRoute.PrimarySystem, UpdateStep.Download, "HTTP 503"), new(UpdateRoute.MirrorDirect, UpdateStep.Manifest, "请求超时。")], used);
        var text = SignedUpdateText.Detail(status, new UpdateException(UpdateError.Expired, "清单已过期"), "zh-CN");
        foreach (var sample in new[] { "主源", "下载", "HTTP 503", "备用源", "直连", "清单", "请求超时", "2026-10-23", "过期", "已保存 0 B" }) Assert.Contains(sample, text);
    }
}
