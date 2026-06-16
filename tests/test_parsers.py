import unittest

from netguardian.wifi import build_wlan_profile_xml, parse_current_ssid, parse_scan_output


class ParserTests(unittest.TestCase):
    def test_parse_current_ssid_ignores_bssid(self) -> None:
        output = """
        Name                   : Wi-Fi
        State                  : connected
        SSID                   : Office-5G
        BSSID                  : aa:bb:cc:dd:ee:ff
        """
        self.assertEqual(parse_current_ssid(output), "Office-5G")

    def test_parse_scan_output_english(self) -> None:
        output = """
    SSID 1 : Office-5G
        Network type            : Infrastructure
        Authentication          : WPA2-Personal
        Encryption              : CCMP
        BSSID 1                 : aa:bb:cc:dd:ee:ff
             Signal             : 86%

    SSID 2 : Guest
        Authentication          : Open
        Encryption              : None
             Signal             : 42%
        """
        networks = parse_scan_output(output)
        self.assertEqual([item.ssid for item in networks], ["Guest", "Office-5G"])
        self.assertEqual(networks[1].signal_percent, 86)
        self.assertEqual(networks[1].authentication, "WPA2-Personal")

    def test_build_wlan_profile_escapes_values(self) -> None:
        xml = build_wlan_profile_xml("A&B", "pa<ss>")
        self.assertIn("A&amp;B", xml)
        self.assertIn("pa&lt;ss&gt;", xml)


if __name__ == "__main__":
    unittest.main()
