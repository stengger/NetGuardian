using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class CurrentWifiPresentationTests
{
    [Fact]
    public void 连接卡片显示系统快照而非选中网络且包含信号安全和地址()
    {
        var view = CurrentWifiPresentation.Create(new(true, true, "实际网络", "WLAN", 89, "WPA3", "GCMP", "192.0.2.10"), new Localizer());
        Assert.Equal("实际网络", view.Name);
        Assert.Contains("89%", view.Details);
        Assert.Contains("WPA3", view.Details);
        Assert.Contains("192.0.2.10", view.Details);
    }

    [Fact]
    public void 查询失败与断开连接明确区分且不能保留旧SSID()
    {
        var localizer = new Localizer();
        var failed = CurrentWifiPresentation.Create(new(false, false, "旧SSID", Error: "访问被拒绝"), localizer);
        var disconnected = CurrentWifiPresentation.Create(new(true, false, "旧SSID"), localizer);
        Assert.NotEqual(failed.Name, disconnected.Name);
        Assert.Contains("访问被拒绝", failed.Details);
        Assert.DoesNotContain("旧SSID", failed.Name + failed.Details + disconnected.Name + disconnected.Details);
    }
}
