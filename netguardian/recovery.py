from __future__ import annotations

import subprocess
import time
from collections.abc import Callable

from .connectivity import ConnectivityTester
from .models import ConnectivityReport, MonitorPolicy, NetworkState, WifiProfile, utc_now_iso
from .wifi import WifiManager


EventSink = Callable[[str], None]


class RecoveryEngine:
    def __init__(
        self,
        wifi: WifiManager,
        tester: ConnectivityTester,
        policy: MonitorPolicy,
        emit: EventSink | None = None,
    ) -> None:
        self.wifi = wifi
        self.tester = tester
        self.policy = policy
        self.emit = emit or (lambda message: None)
        self._last_recovery_at = 0.0

    def can_attempt_recovery(self) -> bool:
        return time.monotonic() - self._last_recovery_at >= self.policy.recovery_cooldown_seconds

    def recover(self, profiles: list[WifiProfile]) -> ConnectivityReport:
        self._last_recovery_at = time.monotonic()
        self.emit("Recovery started.")

        if self.policy.enable_dns_flush:
            self._flush_dns()

        current = self.wifi.current_ssid()
        if current and self.policy.enable_current_wifi_reconnect:
            self.emit(f"Trying soft reconnect for current Wi-Fi: {current}")
            self.wifi.disconnect()
            time.sleep(2)
            self.wifi.connect(current)
            time.sleep(8)
            report = self.tester.run_full_check(self.wifi.current_ssid())
            if report.ok:
                self.emit("Current Wi-Fi recovered.")
                self._mark_success(profiles, current)
                return report

        if self.policy.enable_auto_switch:
            for profile in self._candidate_profiles(profiles):
                self.emit(f"Trying Wi-Fi profile: {profile.ssid}")
                if not self.wifi.connect(profile.ssid):
                    self._mark_failure(profile)
                    continue
                time.sleep(10)
                report = self.tester.run_full_check(self.wifi.current_ssid())
                if report.ok:
                    self.emit(f"Connected through {profile.ssid}.")
                    self._mark_success(profiles, profile.ssid)
                    return report
                self._mark_failure(profile)

        self.emit("Recovery failed. High-risk actions are not automatic in this MVP.")
        return ConnectivityReport(
            state=NetworkState.FAILED,
            checked_at=time.strftime("%Y-%m-%d %H:%M:%S"),
            current_ssid=self.wifi.current_ssid(),
            summary="Recovery attempts failed or no enabled Wi-Fi profile was available.",
        )

    def _candidate_profiles(self, profiles: list[WifiProfile]) -> list[WifiProfile]:
        enabled = [profile for profile in profiles if profile.enabled and profile.auto_connect]
        return sorted(enabled, key=lambda profile: (profile.priority, profile.failure_count))

    def _flush_dns(self) -> None:
        self.emit("Flushing DNS cache.")
        subprocess.run(["ipconfig", "/flushdns"], check=False, capture_output=True, text=True, timeout=10)

    def _mark_success(self, profiles: list[WifiProfile], ssid: str) -> None:
        for profile in profiles:
            if profile.ssid == ssid:
                profile.last_success_at = utc_now_iso()
                profile.failure_count = 0

    def _mark_failure(self, profile: WifiProfile) -> None:
        profile.last_failure_at = utc_now_iso()
        profile.failure_count += 1

