from __future__ import annotations

import queue
import threading
import time
from dataclasses import dataclass
from enum import Enum

from .connectivity import ConnectivityTester
from .models import ConnectivityReport, MonitorPolicy, NetworkState, WifiProfile
from .recovery import RecoveryEngine
from .settings import SettingsStore
from .wifi import WifiManager


class MonitorEventType(str, Enum):
    REPORT = "report"
    LOG = "log"
    PROFILES_CHANGED = "profiles_changed"


@dataclass(slots=True)
class MonitorEvent:
    type: MonitorEventType
    payload: object


class MonitorWorker:
    def __init__(
        self,
        policy: MonitorPolicy,
        profiles: list[WifiProfile],
        store: SettingsStore,
        events: queue.Queue[MonitorEvent],
    ) -> None:
        self.policy = policy
        self.profiles = profiles
        self.store = store
        self.events = events
        self.wifi = WifiManager()
        self.tester = ConnectivityTester(policy)
        self.recovery = RecoveryEngine(self.wifi, self.tester, policy, self._log)
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None
        self._failure_count = 0

    def start(self) -> None:
        if self._thread and self._thread.is_alive():
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._run, name="NetGuardianMonitor", daemon=True)
        self._thread.start()
        self._log("Monitor started.")

    def stop(self) -> None:
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=2)
        self._log("Monitor stopped.")

    def run_once(self) -> ConnectivityReport:
        report = self.tester.run_full_check(self.wifi.current_ssid())
        self.events.put(MonitorEvent(MonitorEventType.REPORT, report))
        return report

    def _run(self) -> None:
        while not self._stop.is_set():
            report = self.run_once()
            if report.ok:
                self._failure_count = 0
            else:
                self._failure_count += 1
                self._log(f"Health check failed: {report.summary} ({self._failure_count}).")
                should_recover = (
                    self._failure_count >= self.policy.consecutive_failures_before_recovery
                    and report.state != NetworkState.SUSPECT
                    and self.recovery.can_attempt_recovery()
                )
                if should_recover:
                    recovery_report = self.recovery.recover(self.profiles)
                    self.events.put(MonitorEvent(MonitorEventType.REPORT, recovery_report))
                    self.store.save(self.policy, self.profiles)
                    self.events.put(MonitorEvent(MonitorEventType.PROFILES_CHANGED, self.profiles))
                    self._failure_count = 0 if recovery_report.ok else self._failure_count

            self._stop.wait(self.policy.check_interval_seconds)

    def _log(self, message: str) -> None:
        timestamp = time.strftime("%Y-%m-%d %H:%M:%S")
        self.events.put(MonitorEvent(MonitorEventType.LOG, f"{timestamp}  {message}"))

