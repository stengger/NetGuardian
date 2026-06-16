from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from typing import Any


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


class NetworkState(str, Enum):
    HEALTHY = "healthy"
    SUSPECT = "suspect"
    DNS_ISSUE = "dns_issue"
    GATEWAY_ISSUE = "gateway_issue"
    INTERNET_ISSUE = "internet_issue"
    DISCONNECTED = "disconnected"
    RECOVERING = "recovering"
    FAILED = "failed"


@dataclass(slots=True)
class WifiProfile:
    ssid: str
    priority: int = 100
    enabled: bool = True
    auto_connect: bool = True
    min_signal_percent: int = 0
    notes: str = ""
    last_success_at: str | None = None
    last_failure_at: str | None = None
    failure_count: int = 0
    avg_latency_ms: float | None = None
    last_speed_mbps: float | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "ssid": self.ssid,
            "priority": self.priority,
            "enabled": self.enabled,
            "auto_connect": self.auto_connect,
            "min_signal_percent": self.min_signal_percent,
            "notes": self.notes,
            "last_success_at": self.last_success_at,
            "last_failure_at": self.last_failure_at,
            "failure_count": self.failure_count,
            "avg_latency_ms": self.avg_latency_ms,
            "last_speed_mbps": self.last_speed_mbps,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "WifiProfile":
        return cls(
            ssid=str(data.get("ssid", "")),
            priority=int(data.get("priority", 100)),
            enabled=bool(data.get("enabled", True)),
            auto_connect=bool(data.get("auto_connect", True)),
            min_signal_percent=int(data.get("min_signal_percent", 0)),
            notes=str(data.get("notes", "")),
            last_success_at=data.get("last_success_at"),
            last_failure_at=data.get("last_failure_at"),
            failure_count=int(data.get("failure_count", 0)),
            avg_latency_ms=data.get("avg_latency_ms"),
            last_speed_mbps=data.get("last_speed_mbps"),
        )


@dataclass(slots=True)
class WifiNetwork:
    ssid: str
    signal_percent: int | None = None
    authentication: str | None = None
    encryption: str | None = None


@dataclass(slots=True)
class ConnectivityProbe:
    name: str
    ok: bool
    latency_ms: float | None = None
    detail: str = ""


@dataclass(slots=True)
class ConnectivityReport:
    state: NetworkState
    checked_at: str
    current_ssid: str | None = None
    ipv4: str | None = None
    gateway: str | None = None
    probes: list[ConnectivityProbe] = field(default_factory=list)
    summary: str = ""

    @property
    def ok(self) -> bool:
        return self.state == NetworkState.HEALTHY


@dataclass(slots=True)
class MonitorPolicy:
    check_interval_seconds: int = 30
    consecutive_failures_before_recovery: int = 3
    enable_auto_switch: bool = True
    enable_current_wifi_reconnect: bool = True
    enable_dns_flush: bool = False
    enable_wlan_service_restart: bool = False
    enable_adapter_restart: bool = False
    recovery_cooldown_seconds: int = 300
    probe_timeout_seconds: float = 2.0

    def normalize(self) -> None:
        self.check_interval_seconds = max(5, int(self.check_interval_seconds))
        self.consecutive_failures_before_recovery = max(
            1, int(self.consecutive_failures_before_recovery)
        )
        self.recovery_cooldown_seconds = max(30, int(self.recovery_cooldown_seconds))
        self.probe_timeout_seconds = max(0.5, float(self.probe_timeout_seconds))

