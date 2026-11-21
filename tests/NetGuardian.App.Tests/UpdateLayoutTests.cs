using System.IO;
using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.App.Updates;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Updates;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class UpdateLayoutTests
{
    [Theory]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Light, "zh-CN")]
    [InlineData(Wpf.Ui.Appearance.ApplicationTheme.Dark, "en-US")]
    public void 设置更新卡在最小窗口内换行且长说明可达(Wpf.Ui.Appearance.ApplicationTheme theme, string language)
    {
        DesktopFixture.Run(window =>
        {
            DesktopFixture.ShowPage(window, "SettingsPanel");
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            using var session = new UpdateSession(new Service(), new Version(1, 1, 0));
            session.CheckAsync().GetAwaiter().GetResult();
            UpdatePresentation.Apply(window, session, new Version(1, 1, 0), localizer, true);
            Assert.True(((Button)window.FindName("UpdateDownloadButton")).IsEnabled);
            Assert.Equal(Visibility.Collapsed, ((Button)window.FindName("UpdateInstallButton")).Visibility);
            Assert.True(((Button)window.FindName("UpdateCheckButton")).Focusable);
            DesktopFixture.Layout(window, 1040, 660);
            DesktopFixture.Save(window, $"updates-top-{theme}-{language}.png");
            var notes = (Expander)window.FindName("UpdateNotesExpander");
            notes.IsExpanded = true;
            DesktopFixture.Layout(window, 1040, 660);
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            window.UpdateLayout();
            var scroll = (ScrollViewer)window.FindName("SettingsScrollViewer");
            Assert.True(scroll.ScrollableHeight > 0);
            Assert.True(scroll.ExtentWidth <= scroll.ViewportWidth + 1);
            scroll.ScrollToEnd();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            Assert.True(scroll.VerticalOffset >= scroll.ScrollableHeight - 1);
            DesktopFixture.Save(window, $"updates-{theme}-{language}.png");
            session.DownloadAsync("缓存").GetAwaiter().GetResult();
            UpdatePresentation.Apply(window, session, new Version(1, 1, 0), localizer, false);
            var install = (Button)window.FindName("UpdateInstallButton");
            Assert.Equal(Visibility.Visible, install.Visibility);
            Assert.False(install.IsEnabled);
            Assert.Equal(localizer.T("UpdateInstalledOnly"), install.ToolTip);
            UpdatePresentation.Apply(window, session, new Version(1, 1, 0), localizer, true);
            Assert.True(install.IsEnabled);
            session.BeginInstall();
            session.WaitForExit();
            UpdatePresentation.Apply(window, session, new Version(1, 1, 0), localizer, true);
            Assert.Equal(localizer.T("UpdateWaitingForExit"), ((TextBlock)window.FindName("UpdateStatusText")).Text);
            Assert.False(install.IsEnabled);
            Assert.False(((Button)window.FindName("UpdateCheckButton")).IsEnabled);
            session.InstallFailed(new IOException("退出收尾失败"));
            UpdatePresentation.Apply(window, session, new Version(1, 1, 0), localizer, true);
            Assert.True(install.IsEnabled);
            Assert.True(((Button)window.FindName("UpdateCheckButton")).IsEnabled);
            Assert.Contains("退出收尾失败", ((TextBlock)window.FindName("UpdateStatusText")).Text);
        }, theme);
    }

    [Theory]
    [InlineData("zh-CN", "继续下载")]
    [InlineData("en-US", "Resume download")]
    public void 下载中断时显示已保存的真实进度并可继续(string language, string action)
    {
        DesktopFixture.Run(window =>
        {
            var text = new Localizer();
            text.SetLanguage(language);
            var interrupted = new UpdateException(UpdateError.Network, "断流") { SavedProgress = new UpdateProgress(40, 100) };
            var version = new Version(1, 1, 0);
            using var session = new UpdateSession(new Service(interrupted), version);
            session.CheckAsync().GetAwaiter().GetResult();
            session.DownloadAsync("缓存").GetAwaiter().GetResult();
            UpdatePresentation.Apply(window, session, version, text, true);
            var resume = (Button)window.FindName("UpdateDownloadButton");
            Assert.Equal(action, resume.Content);
            Assert.True(resume.IsEnabled);
            var bar = (ProgressBar)window.FindName("UpdateProgressBar");
            Assert.Equal(Visibility.Visible, bar.Visibility);
            Assert.False(bar.IsIndeterminate);
            Assert.Equal(40d, bar.Value);
            Assert.Contains("40%", ((TextBlock)window.FindName("UpdateStatusText")).Text);
            Assert.Equal(Visibility.Collapsed, ((Button)window.FindName("UpdateInstallButton")).Visibility);
        });
    }

    private sealed class Service(UpdateException? failure = null) : IUpdateService
    {
        public Task<UpdateRelease?> CheckAsync(Version version, CancellationToken token) => Task.FromResult<UpdateRelease?>(new(
            new Version(1, 2, 0), string.Join("\n", Enumerable.Repeat("发布说明 / Release notes", 80)), new Uri("https://github.com"),
            new UpdateAsset("包", new Uri("https://github.com"), 100, null), new UpdateAsset("摘要", new Uri("https://github.com"), 100, null)));
        public Task<DownloadedUpdate> DownloadAsync(UpdateRelease release, string directory, IProgress<UpdateProgress>? progress, CancellationToken token) =>
            failure is null ? Task.FromResult(new DownloadedUpdate(release.Version, "安装包", "摘要")) : Task.FromException<DownloadedUpdate>(failure);
    }
}
