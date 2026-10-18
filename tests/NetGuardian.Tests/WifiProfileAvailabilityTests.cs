using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class WifiProfileAvailabilityTests
{
    [Fact]
    public void 已存在Windows配置的备用网络不能因陈旧标记被排除()
    {
        var profiles = new[]
        {
            new WifiProfile { Ssid = "Redmi", CredentialSaved = false, Priority = 1 },
            new WifiProfile { Ssid = "实验室", CredentialSaved = true, Priority = 2 }
        };
        WifiProfileAvailability.Apply(profiles, new WifiProfileInventory(true, ["Redmi", "实验室"]));
        Assert.Equal("Redmi", RecoveryPlanner.OrderSwitchCandidates(profiles, "实验室").Single().Ssid);
    }

    [Fact]
    public void Windows配置查询失败时保留最近有效凭据状态()
    {
        var profiles = new[] { new WifiProfile { Ssid = "备用", CredentialSaved = true } };
        WifiProfileAvailability.Apply(profiles, new WifiProfileInventory(false, [], "无线服务暂不可用"));
        Assert.True(profiles[0].CredentialSaved);
    }

    [Fact]
    public void 明确删除的配置不再列为可自动切换候选()
    {
        var profiles = new[] { new WifiProfile { Ssid = "已删除", CredentialSaved = true } };
        WifiProfileAvailability.Apply(profiles, new WifiProfileInventory(true, []));
        Assert.Empty(RecoveryPlanner.OrderSwitchCandidates(profiles, null));
    }
}
