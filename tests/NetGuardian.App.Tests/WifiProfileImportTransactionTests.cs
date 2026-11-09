using System.IO;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using NetGuardian.Core.Settings;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class WifiProfileImportTransactionTests
{
    [Fact]
    public void 保存失败不得发布新网络或改变现有凭据映射()
    {
        var profile = new WifiProfile { Ssid = "现有", CredentialSaved = false };
        var settings = new AppSettings { Profiles = [profile] };
        var failure = Assert.Throws<IOException>(() => WifiProfileImportTransaction.PrepareAndSave(
            settings, settings.Profiles, new(true, [new("配置", "现有"), new("新配置", "新网络")]), "新网络",
            snapshot =>
            {
                Assert.Equal(2, snapshot.Profiles.Count);
                Assert.True(snapshot.Profiles[0].CredentialSaved);
                throw new IOException("磁盘只读");
            }));
        Assert.Equal("磁盘只读", failure.Message);
        Assert.Same(profile, Assert.Single(settings.Profiles));
        Assert.False(profile.CredentialSaved);
        Assert.Empty(profile.WindowsProfileName);
    }

    [Fact]
    public void 保存使用当前界面列表的独立快照并保留排除记录及策略()
    {
        var settings = new AppSettings { ExcludedWifiSsids = ["已移除"], Profiles = [new() { Ssid = "旧列表" }] };
        var edited = new WifiProfile { Ssid = "编辑中", Enabled = false, Priority = 3 };
        AppSettings? saved = null;
        var (snapshot, result) = WifiProfileImportTransaction.PrepareAndSave(settings, [edited],
            new(true, [new("配置", "编辑中"), new("新配置", "新网络"), new("已移除", "已移除")]), "新网络",
            candidate => saved = candidate);
        Assert.Equal(1, result.AddedCount);
        Assert.Same(snapshot, saved);
        Assert.NotSame(settings.Policy, snapshot.Policy);
        Assert.Equal(settings.Policy.CheckIntervalSeconds, snapshot.Policy.CheckIntervalSeconds);
        Assert.Equal(new[] { "编辑中", "新网络" }, snapshot.Profiles.Select(profile => profile.Ssid));
        Assert.False(snapshot.Profiles[0].Enabled);
        Assert.Equal(4, snapshot.Profiles[1].Priority);
        Assert.Equal("已移除", Assert.Single(snapshot.ExcludedWifiSsids));
        Assert.Equal("旧列表", Assert.Single(settings.Profiles).Ssid);
        Assert.False(edited.CredentialSaved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 查询失败或没有变化时不写入配置(bool success)
    {
        var (_, result) = WifiProfileImportTransaction.PrepareAndSave(new(), [], new(success, []), null,
            _ => Assert.Fail("无需写入配置。"));
        Assert.False(result.Changed);
    }
}
