using System.Collections.ObjectModel;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class NearbySavedWifiViewTests
{
    [Fact]
    public void 只显示附近已保存网络且重新出现时保留原优先级和对象()
    {
        var nearby = new WifiProfile { Ssid = "8b304北", CredentialSaved = true, Priority = 3 };
        var historical = new WifiProfile { Ssid = "家里", CredentialSaved = true, Priority = 1 };
        var unsaved = new WifiProfile { Ssid = "未保存", CredentialSaved = false, Priority = 4 };
        var profiles = new ObservableCollection<WifiProfile> { historical, nearby, unsaved };
        var visible = new NearbySavedWifiView(profiles);
        visible.Update(["8b304北", "未保存", "陌生网络"]);
        Assert.Same(nearby, Assert.Single(visible.View.Cast<WifiProfile>()));
        visible.Update([]);
        Assert.Empty(visible.View.Cast<WifiProfile>());
        visible.Update(["家里", "8b304北"]);
        Assert.Equal(new[] { historical, nearby }, visible.View.Cast<WifiProfile>());
        Assert.Equal(3, nearby.Priority);
        Assert.Equal(3, profiles.Count);
        nearby.CredentialSaved = false;
        visible.View.Refresh();
        Assert.Same(historical, Assert.Single(visible.View.Cast<WifiProfile>()));
    }

    [Fact]
    public void 扫描前不展示历史记录且SSID大小写和空格不能混同()
    {
        var profiles = new ObservableCollection<WifiProfile>
        {
            new() { Ssid = "Home", CredentialSaved = true },
            new() { Ssid = "home", CredentialSaved = true },
            new() { Ssid = " Home ", CredentialSaved = true }
        };
        var visible = new NearbySavedWifiView(profiles);
        Assert.Empty(visible.View.Cast<WifiProfile>());
        visible.Update(["home"]);
        Assert.Equal("home", Assert.Single(visible.View.Cast<WifiProfile>()).Ssid);
    }
}
