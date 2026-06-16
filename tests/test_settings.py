from pathlib import Path
import tempfile
import unittest

from netguardian.models import MonitorPolicy, WifiProfile
from netguardian.settings import SettingsStore


class SettingsTests(unittest.TestCase):
    def test_settings_round_trip(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            store = SettingsStore(Path(directory) / "settings.json")
            policy = MonitorPolicy(check_interval_seconds=1)
            profiles = [WifiProfile(ssid="Office", priority=2), WifiProfile(ssid="Backup", priority=1)]

            store.save(policy, profiles)
            loaded_policy, loaded_profiles = store.load()

            self.assertEqual(loaded_policy.check_interval_seconds, 5)
            self.assertEqual([profile.ssid for profile in loaded_profiles], ["Backup", "Office"])


if __name__ == "__main__":
    unittest.main()
