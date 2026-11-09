using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class WifiActionAvailabilityTests
{
    [Fact]
    public void 周围网络已保存时可直接连接且不能重复添加()
    {
        var saved = new WifiProfile { Ssid = "网络", CredentialSaved = true };
        var state = WifiActionAvailability.Create([saved], null, "网络", false);
        Assert.Same(saved, state.ConnectTarget);
        Assert.True(state.CanConnect);
        Assert.False(state.CanAdd);
        Assert.True(state.CanEdit);
    }

    [Fact]
    public void 缺少凭据时引导设置密码并区分大小写不同的附近网络()
    {
        var saved = new WifiProfile { Ssid = "Office", CredentialSaved = false };
        var state = WifiActionAvailability.Create([saved], saved, null, false);
        Assert.False(state.CanConnect);
        Assert.True(state.CanEdit);
        state = WifiActionAvailability.Create([saved], null, "office", false);
        Assert.True(state.CanAdd);
        Assert.Null(state.ConnectTarget);
        Assert.False(state.CanConnect);
    }

    [Fact]
    public void 操作期间两个列表入口均不可重复连接或编辑()
    {
        var saved = new WifiProfile { Ssid = "网络", CredentialSaved = true };
        foreach (var selected in new WifiProfile?[] { saved, null })
        {
            var state = WifiActionAvailability.Create([saved], selected, "网络", true);
            Assert.False(state.CanConnect);
            Assert.False(state.CanAdd);
            Assert.False(state.CanEdit);
        }
    }
}
