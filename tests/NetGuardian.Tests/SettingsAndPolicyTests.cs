using System.Text.Json;
using NetGuardian.Core.Models;
using NetGuardian.Core.Settings;
using Xunit;

namespace NetGuardian.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"netguardian-{Guid.NewGuid():N}.json");

    [Fact]
    public void SaveThenLoad_NormalizesPolicyAndSortsProfiles()
    {
        var store = new SettingsStore(_path);
        var settings = new AppSettings
        {
            Policy = new MonitorPolicy { CheckIntervalSeconds = 1 },
            Profiles =
            [
                new WifiProfile { Ssid = "Office", Priority = 2 },
                new WifiProfile { Ssid = "Backup", Priority = 1 }
            ]
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(5, loaded.Policy.CheckIntervalSeconds);
        Assert.Equal("Backup", loaded.Profiles[0].Ssid);
    }

    [Fact]
    public void Load_RestoresFromBackup_WhenFileCorrupted()
    {
        var store = new SettingsStore(_path);
        store.Save(new AppSettings { Profiles = [new WifiProfile { Ssid = "Recovered", Priority = 1 }] });
        store.Save(new AppSettings { Profiles = [new WifiProfile { Ssid = "Current", Priority = 1 }] });

        File.WriteAllText(_path, "{broken");

        Assert.Equal("Recovered", store.Load().Profiles[0].Ssid);
        Assert.Equal("Recovered", new SettingsStore(_path).Load().Profiles[0].Ssid);
    }

    [Fact]
    public void Save_DropsBlankWifiProfiles()
    {
        var store = new SettingsStore(_path);
        store.Save(new AppSettings
        {
            Profiles =
            [
                new WifiProfile { Ssid = "Office", Priority = 3 },
                new WifiProfile { Ssid = "   ", Priority = 1 },
                new WifiProfile { Ssid = "Backup", Priority = 2 }
            ]
        });

        using var document = JsonDocument.Parse(File.ReadAllText(_path));
        var profiles = document.RootElement.GetProperty("profiles").EnumerateArray().ToList();
        Assert.Equal(2, profiles.Count);
        Assert.Equal("Backup", profiles[0].GetProperty("ssid").GetString());
        Assert.Equal("Office", profiles[1].GetProperty("ssid").GetString());
    }

    public void Dispose()
    {
        foreach (var item in new[] { _path, _path + ".bak", _path + ".tmp" })
        {
            if (File.Exists(item))
            {
                File.Delete(item);
            }
        }
    }
}

public class MonitorPolicyTests
{
    [Fact]
    public void Normalize_ClampsUpperBounds()
    {
        var policy = new MonitorPolicy
        {
            CheckIntervalSeconds = int.MaxValue,
            ConsecutiveFailuresBeforeRecovery = int.MaxValue,
            RecoveryCooldownSeconds = int.MaxValue,
            ProbeTimeoutSeconds = double.MaxValue
        };

        policy.Normalize();

        Assert.Equal(24 * 60 * 60, policy.CheckIntervalSeconds);
        Assert.Equal(100, policy.ConsecutiveFailuresBeforeRecovery);
        Assert.Equal(24 * 60 * 60, policy.RecoveryCooldownSeconds);
        Assert.Equal(30d, policy.ProbeTimeoutSeconds);
    }
}

public class ProbeTargetTests
{
    [Fact]
    public void Normalize_TrimsDedupesAndCaps()
    {
        var policy = new MonitorPolicy
        {
            DnsProbeHosts = [" a.com ", "a.com", "b.com", "c.com", "d.com", "e.com", "f.com"],
            HttpsProbeHosts = ["x.com"]
        };

        policy.Normalize();

        Assert.Equal(["a.com", "b.com", "c.com", "d.com", "e.com"], policy.DnsProbeHosts);
        Assert.Equal(["x.com"], policy.HttpsProbeHosts);
    }

    [Fact]
    public void Normalize_FallsBackToDefaults_WhenAllInvalid()
    {
        var policy = new MonitorPolicy
        {
            DnsProbeHosts = ["", "   ", "bad host name!"],
            HttpsProbeHosts = []
        };

        policy.Normalize();

        Assert.Equal(MonitorPolicy.DefaultDnsProbeHosts, policy.DnsProbeHosts);
        Assert.Equal(MonitorPolicy.DefaultHttpsProbeHosts, policy.HttpsProbeHosts);
    }

    [Fact]
    public void ProbeHosts_RoundTripThroughSettingsStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netguardian-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.Save(new AppSettings
            {
                Policy = new MonitorPolicy { DnsProbeHosts = ["dns.internal"], HttpsProbeHosts = ["web.internal"] }
            });

            var loaded = store.Load();
            Assert.Equal(["dns.internal"], loaded.Policy.DnsProbeHosts);
            Assert.Equal(["web.internal"], loaded.Policy.HttpsProbeHosts);
        }
        finally
        {
            foreach (var item in new[] { path, path + ".bak", path + ".tmp" })
            {
                if (File.Exists(item))
                {
                    File.Delete(item);
                }
            }
        }
    }
}
