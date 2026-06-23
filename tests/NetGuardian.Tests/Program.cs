using NetGuardian.Core.Models;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Network;
using NetGuardian.Core.Settings;
using NetGuardian.Core.Wifi;
using System.Text.Json;

var tests = new List<(string Name, Action Body)>
{
    ("parse current SSID", ParseCurrentSsid),
    ("parse Wi-Fi adapter diagnostics", ParseWifiAdapterDiagnostics),
    ("parse Chinese Wi-Fi adapter diagnostics", ParseChineseWifiAdapterDiagnostics),
    ("parse saved Wi-Fi password", ParseSavedWifiPassword),
    ("parse Wi-Fi profile names", ParseWifiProfileNames),
    ("parse Wi-Fi profile SSID", ParseWifiProfileSsid),
    ("parse scan output", ParseScanOutput),
    ("build WLAN profile XML", BuildWlanProfileXml),
    ("build WLAN profile XML with profile name", BuildWlanProfileXmlWithProfileName),
    ("normalize website URL", NormalizeWebsiteUrl),
    ("localization resources have matching keys", LocalizationResourcesHaveMatchingKeys),
    ("settings round trip", SettingsRoundTrip),
    ("settings restore backup", SettingsRestoreBackup),
    ("policy clamps upper bounds", PolicyClampsUpperBounds)
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

static void ParseWifiAdapterDiagnostics()
{
    const string output = """
        Name                   : Wi-Fi
        Description            : Intel(R) Wi-Fi
        State                  : connected
        SSID                   : Office-5G
        Radio status           : Hardware On
                                 Software On
        """;

    var diagnostics = NetshWifiClient.ParseInterfaceDiagnostics(output);
    AssertEqual("Wi-Fi", diagnostics.AdapterName);
    AssertEqual(true, diagnostics.AdapterPresent);
    AssertEqual(true, diagnostics.AdapterUp);
    AssertEqual(true, diagnostics.HardwareRadioOn);
    AssertEqual(true, diagnostics.SoftwareRadioOn);
}

static void ParseChineseWifiAdapterDiagnostics()
{
    const string output = """
        名称                   : WLAN
        描述                   : Intel(R) Wi-Fi
        状态                   : 已断开连接
        无线电状态             : 硬件 打开
                                 软件 关闭
        """;

    var diagnostics = NetshWifiClient.ParseInterfaceDiagnostics(output);
    AssertEqual("WLAN", diagnostics.AdapterName);
    AssertEqual(true, diagnostics.AdapterPresent);
    AssertEqual(false, diagnostics.AdapterUp);
    AssertEqual(true, diagnostics.HardwareRadioOn);
    AssertEqual(false, diagnostics.SoftwareRadioOn);
}

static void ParseSavedWifiPassword()
{
    const string output = """
        Security settings
        -----------------
            Authentication         : WPA2-Personal
            Cipher                 : CCMP
            Security key           : Present
            Key Content            : secret-12345
        """;

    AssertEqual("secret-12345", NetshWifiClient.ParseSavedPassword(output));
}

static void ParseWifiProfileNames()
{
    const string output = """
        Profiles on interface Wi-Fi:

        Group policy profiles (read only)
        ---------------------------------
            <None>

        User profiles
        -------------
            All User Profile     : Office 5G
            All User Profile     : Guest
        """;

    var names = NetshWifiClient.ParseProfileNames(output);
    AssertEqual(2, names.Count);
    AssertEqual("Office 5G", names[0]);
}

static void ParseWifiProfileSsid()
{
    const string output = """
        Profile Office:
        =======================================================================

        Applied: All User Profile

        Profile information
        -------------------
            Version                : 1
            Type                   : Wireless LAN
            Name                   : Office
            Control options        :
                Connection mode    : Connect automatically
                Network broadcast  : Connect only if this network is broadcasting
                AutoSwitch         : Do not switch to other networks
                MAC Randomization  : Disabled

        Connectivity settings
        ---------------------
            Number of SSIDs        : 1
            SSID name              : "Office 5G"
        """;

    AssertEqual("Office 5G", NetshWifiClient.ParseProfileSsid(output));
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
        DeleteSettingsFiles(path);
    }
}

static void SettingsRestoreBackup()
{
    var path = Path.Combine(Path.GetTempPath(), $"netguardian-{Guid.NewGuid():N}.json");
    try
    {
        var store = new SettingsStore(path);
        store.Save(new AppSettings
        {
            Profiles = [new WifiProfile { Ssid = "Recovered", Priority = 1 }]
        });
        store.Save(new AppSettings
        {
            Profiles = [new WifiProfile { Ssid = "Current", Priority = 1 }]
        });

        File.WriteAllText(path, "{broken");
        var loaded = store.Load();
        AssertEqual("Recovered", loaded.Profiles[0].Ssid);
        AssertEqual("Recovered", new SettingsStore(path).Load().Profiles[0].Ssid);
    }
    finally
    {
        DeleteSettingsFiles(path);
    }
}

static void PolicyClampsUpperBounds()
{
    var policy = new MonitorPolicy
    {
        CheckIntervalSeconds = int.MaxValue,
        ConsecutiveFailuresBeforeRecovery = int.MaxValue,
        RecoveryCooldownSeconds = int.MaxValue,
        ProbeTimeoutSeconds = double.MaxValue
    };

    policy.Normalize();
    AssertEqual(24 * 60 * 60, policy.CheckIntervalSeconds);
    AssertEqual(100, policy.ConsecutiveFailuresBeforeRecovery);
    AssertEqual(24 * 60 * 60, policy.RecoveryCooldownSeconds);
    AssertEqual(30d, policy.ProbeTimeoutSeconds);
}

static void BuildWlanProfileXml()
{
    var xml = NetshWifiClient.BuildWlanProfileXml("A&B", "pa<ss>", "WPA2-Personal");
    if (!xml.Contains("A&amp;B") || !xml.Contains("pa&lt;ss&gt;"))
    {
        throw new InvalidOperationException("WLAN profile XML did not escape SSID/password.");
    }
}

static void BuildWlanProfileXmlWithProfileName()
{
    var xml = NetshWifiClient.BuildWlanProfileXml("NetGuardian-Verify", "Office-5G", "secret", "WPA2-Personal");
    if (!xml.Contains("<name>NetGuardian-Verify</name>") || !xml.Contains("<name>Office-5G</name>"))
    {
        throw new InvalidOperationException("WLAN profile XML did not preserve separate profile and SSID names.");
    }
}

static void NormalizeWebsiteUrl()
{
    AssertEqual("https://example.com", WebsiteTestService.NormalizeUrl("example.com"));
    AssertEqual("http://example.com", WebsiteTestService.NormalizeUrl("http://example.com"));
}

static void LocalizationResourcesHaveMatchingKeys()
{
    var localizer = new Localizer();
    var languages = localizer.SupportedLanguages.ToList();
    AssertEqual(2, languages.Count);
    AssertEqual(true, languages.Contains("zh-CN"));
    AssertEqual(true, languages.Contains("en-US"));

    var zhKeys = localizer.KeysForLanguage("zh-CN").OrderBy(key => key).ToList();
    var enKeys = localizer.KeysForLanguage("en-US").OrderBy(key => key).ToList();

    var missingInEnglish = zhKeys.Except(enKeys, StringComparer.OrdinalIgnoreCase).ToList();
    var missingInChinese = enKeys.Except(zhKeys, StringComparer.OrdinalIgnoreCase).ToList();

    if (missingInEnglish.Count > 0 || missingInChinese.Count > 0)
    {
        throw new InvalidOperationException(
            $"Localization keys differ. Missing in English: {string.Join(", ", missingInEnglish)}. Missing in Chinese: {string.Join(", ", missingInChinese)}.");
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}");
    }
}

static void DeleteSettingsFiles(string path)
{
    foreach (var item in new[] { path, path + ".bak", path + ".tmp" })
    {
        if (File.Exists(item))
        {
            File.Delete(item);
        }
    }
}
