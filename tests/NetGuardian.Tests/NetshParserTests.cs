using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public class NetshParserTests
{
    [Fact]
    public void ParseCurrentSsid_ReturnsSsid()
    {
        const string output = """
            Name                   : Wi-Fi
            State                  : connected
            SSID                   : Office-5G
            BSSID                  : aa:bb:cc:dd:ee:ff
            """;

        Assert.Equal("Office-5G", NetshWifiClient.ParseCurrentSsid(output));
    }

    [Fact]
    public void ParseScanOutput_ParsesNetworks()
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
        Assert.Equal(2, networks.Count);
        Assert.Equal("Office-5G", networks[0].Ssid);
        Assert.Equal(86, networks[0].SignalPercent);
    }

    public static TheoryData<string, string, bool, bool, bool> InterfaceDiagnosticsCases => new()
    {
        // output, adapterName, adapterUp, hardwareRadioOn, softwareRadioOn
        {
            """
            Name                   : Wi-Fi
            Description            : Intel(R) Wi-Fi
            State                  : connected
            SSID                   : Office-5G
            Radio status           : Hardware On
                                     Software On
            """,
            "Wi-Fi", true, true, true
        },
        {
            """
            名称                   : WLAN
            描述                   : Intel(R) Wi-Fi
            状态                   : 已断开连接
            无线电状态             : 硬件 打开
                                     软件 关闭
            """,
            "WLAN", false, true, false
        }
    };

    [Theory]
    [MemberData(nameof(InterfaceDiagnosticsCases))]
    public void ParseInterfaceDiagnostics_HandlesEnglishAndChinese(
        string output, string adapterName, bool adapterUp, bool hardwareRadioOn, bool softwareRadioOn)
    {
        var diagnostics = NetshWifiClient.ParseInterfaceDiagnostics(output);
        Assert.Equal(adapterName, diagnostics.AdapterName);
        Assert.True(diagnostics.AdapterPresent);
        Assert.Equal(adapterUp, diagnostics.AdapterUp);
        Assert.Equal(hardwareRadioOn, diagnostics.HardwareRadioOn);
        Assert.Equal(softwareRadioOn, diagnostics.SoftwareRadioOn);
    }

    [Fact]
    public void ParseSavedPassword_ReturnsKeyContent()
    {
        const string output = """
            Security settings
            -----------------
                Authentication         : WPA2-Personal
                Cipher                 : CCMP
                Security key           : Present
                Key Content            : secret-12345
            """;

        Assert.Equal("secret-12345", NetshWifiClient.ParseSavedPassword(output));
    }

    [Fact]
    public void ParseProfileNames_ReturnsUserProfiles()
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
        Assert.Equal(2, names.Count);
        Assert.Equal("Office 5G", names[0]);
    }

    [Fact]
    public void ParseProfileSsid_ReturnsQuotedSsid()
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

        Assert.Equal("Office 5G", NetshWifiClient.ParseProfileSsid(output));
    }
}

public class WlanProfileXmlTests
{
    [Fact]
    public void BuildWlanProfileXml_EscapesSpecialCharacters()
    {
        var xml = NetshWifiClient.BuildWlanProfileXml("A&B", "pa<ss>", "WPA2-Personal");
        Assert.Contains("A&amp;B", xml);
        Assert.Contains("pa&lt;ss&gt;", xml);
    }

    [Fact]
    public void BuildWlanProfileXml_KeepsSeparateProfileAndSsidNames()
    {
        var xml = NetshWifiClient.BuildWlanProfileXml("NetGuardian-Verify", "Office-5G", "secret", "WPA2-Personal");
        Assert.Contains("<name>NetGuardian-Verify</name>", xml);
        Assert.Contains("<name>Office-5G</name>", xml);
    }
}
