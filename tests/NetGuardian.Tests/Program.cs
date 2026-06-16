using NetGuardian.Core.Models;
using NetGuardian.Core.Settings;
using NetGuardian.Core.Wifi;

var tests = new List<(string Name, Action Body)>
{
    ("parse current SSID", ParseCurrentSsid),
    ("parse scan output", ParseScanOutput),
    ("settings round trip", SettingsRoundTrip)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void ParseCurrentSsid()
{
    const string output = """
        Name                   : Wi-Fi
        State                  : connected
        SSID                   : Office-5G
        BSSID                  : aa:bb:cc:dd:ee:ff
        """;

    AssertEqual("Office-5G", NetshWifiClient.ParseCurrentSsid(output));
}

static void ParseScanOutput()
{
    const string output = """
        SSID 1 : Office-5G
            Authentication          : WPA2-Personal
            Encryption              : CCMP
                 Signal             : 86%

        SSID 2 : Guest
            Authentication          : Open
            Encryption              : None
                 Signal             : 42%
        """;

    var networks = NetshWifiClient.ParseScanOutput(output);
    AssertEqual(2, networks.Count);
    AssertEqual("Office-5G", networks[0].Ssid);
    AssertEqual(86, networks[0].SignalPercent);
}

static void SettingsRoundTrip()
{
    var path = Path.Combine(Path.GetTempPath(), $"netguardian-{Guid.NewGuid():N}.json");
    try
    {
        var store = new SettingsStore(path);
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
        AssertEqual(5, loaded.Policy.CheckIntervalSeconds);
        AssertEqual("Backup", loaded.Profiles[0].Ssid);
    }
    finally
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}");
    }
}
