using NetGuardian.Core.Models;
using NetGuardian.Core.Settings;
using Xunit;

namespace NetGuardian.Tests;

public sealed class SettingsReliabilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NetGuardian-settings-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("{\"policy\":null}")]
    [InlineData("{\"profiles\":null}")]
    [InlineData("{\"profiles\":[null]}")]
    [InlineData("null")]
    public void 结构损坏的配置回退最近有效备份(string invalidJson)
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings { Profiles = [new WifiProfile { Ssid = "备用网络" }] });
        store.Save(new AppSettings { Profiles = [new WifiProfile { Ssid = "新网络" }] });
        File.WriteAllText(path, invalidJson);
        Assert.Equal("备用网络", store.Load().Profiles.Single().Ssid);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
