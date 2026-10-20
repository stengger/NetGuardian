using NetGuardian.App.Presentation;
using NetGuardian.App.Views;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Runtime;
using Xunit;
using Forms = System.Windows.Forms;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class TrayMonitorPresenterTests
{
    [Fact]
    public void 后台归属与操作可用性实际应用到托盘且跟随语言变化()
    {
        DesktopFixture.Run(_ =>
        {
            using var tray = new Forms.NotifyIcon();
            var menu = new TrayContextMenu();
            var now = DateTimeOffset.Now;
            var background = new GuardianStatus(1, now.AddHours(-1), now, "Running", false);
            var localizer = new Localizer();
            var state = MonitorStatusPresentation.Create(false, false, background, now, localizer);
            TrayMonitorPresenter.Apply(tray, menu, state, localizer);
            Assert.Contains("后台监控中", tray.Text);
            Assert.False(menu.GetCommand("StartMonitor").IsEnabled);
            Assert.False(menu.GetCommand("Stop").IsEnabled);
            Assert.True(menu.GetCommand("CheckNow").IsEnabled);
            Assert.Contains("后台", menu.GetCommand("Stop").ToolTip!.ToString());
            localizer.SetLanguage("en-US");
            state = MonitorStatusPresentation.Create(false, false, background, now, localizer);
            TrayMonitorPresenter.Apply(tray, menu, state, localizer);
            Assert.Equal(localizer.T("Stop"), menu.GetCommand("Stop").Header);
            Assert.Contains(localizer.T("BackgroundMonitoring"), tray.Text);
        });
    }

    [Fact]
    public void 检测期间不能从页头或托盘重复开始()
    {
        var state = MonitorStatusPresentation.Create(false, true, null, DateTimeOffset.Now, new Localizer());
        Assert.False(state.CanCheck);
        Assert.False(state.CanStart);
        Assert.False(state.CanStop);
    }
}
