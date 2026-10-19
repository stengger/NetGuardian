using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Runtime;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class MonitorStatusPresentationTests
{
    [Theory]
    [InlineData(false, "后台监控中")]
    [InlineData(true, "后台检测中")]
    public void 后台接管时状态颜色归属与不可用原因一致(bool checking, string expected)
    {
        var now = DateTimeOffset.Now;
        var background = new GuardianStatus(1, now.AddHours(-1), now, "Running", checking);
        var state = MonitorStatusPresentation.Create(false, false, background, now, new Localizer());
        Assert.Equal(expected, state.ActivityText);
        Assert.True(state.HasBackground);
        Assert.False(state.CanStart);
        Assert.False(state.CanStop);
        Assert.Contains("独立后台", state.Hint);
        Assert.NotEqual("StatusDangerBrush", state.BrushKey);
    }

    [Fact]
    public void 过期后台心跳不能当成有效接管()
    {
        var now = DateTimeOffset.Now;
        var background = new GuardianStatus(1, now.AddHours(-1), now.AddSeconds(-31), "Running", false);
        var state = MonitorStatusPresentation.Create(false, false, background, now, new Localizer());
        Assert.False(state.HasBackground);
        Assert.True(state.CanStart);
        Assert.Contains("未启动", state.Hint);
    }

    [Fact]
    public void 界面监控中不应还能再次点击开始()
    {
        var state = MonitorStatusPresentation.Create(true, false, null, DateTimeOffset.Now, new Localizer());
        Assert.False(state.CanStart);
        Assert.True(state.CanStop);
    }
}
