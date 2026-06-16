from __future__ import annotations

import http.client
import re
import socket
import subprocess
import time

from .models import ConnectivityProbe, ConnectivityReport, MonitorPolicy, NetworkState


DNS_TARGETS = ("www.microsoft.com", "www.baidu.com")
HTTPS_TARGETS = ("www.microsoft.com", "www.baidu.com", "www.cloudflare.com")


class ConnectivityTester:
    def __init__(self, policy: MonitorPolicy | None = None) -> None:
        self.policy = policy or MonitorPolicy()

    def run_full_check(self, current_ssid: str | None = None) -> ConnectivityReport:
        checked_at = time.strftime("%Y-%m-%d %H:%M:%S")
        probes: list[ConnectivityProbe] = []

        ipv4 = self._get_primary_ipv4()
        gateway = self._get_default_gateway()

        probes.append(
            ConnectivityProbe(
                "IPv4",
                bool(ipv4),
                detail=ipv4 or "No active IPv4 address detected.",
            )
        )
        probes.append(
            ConnectivityProbe(
                "Default gateway",
                bool(gateway),
                detail=gateway or "No default gateway detected.",
            )
        )

        if not ipv4:
            return ConnectivityReport(
                state=NetworkState.DISCONNECTED,
                checked_at=checked_at,
                current_ssid=current_ssid,
                ipv4=ipv4,
                gateway=gateway,
                probes=probes,
                summary="No active IPv4 address.",
            )

        if gateway:
            gateway_probe = self._ping(gateway, "Gateway ping")
            probes.append(gateway_probe)
            if not gateway_probe.ok:
                return ConnectivityReport(
                    state=NetworkState.GATEWAY_ISSUE,
                    checked_at=checked_at,
                    current_ssid=current_ssid,
                    ipv4=ipv4,
                    gateway=gateway,
                    probes=probes,
                    summary="Gateway is not reachable.",
                )

        dns_successes = 0
        for host in DNS_TARGETS:
            probe = self._resolve(host)
            probes.append(probe)
            dns_successes += int(probe.ok)
        if dns_successes == 0:
            return ConnectivityReport(
                state=NetworkState.DNS_ISSUE,
                checked_at=checked_at,
                current_ssid=current_ssid,
                ipv4=ipv4,
                gateway=gateway,
                probes=probes,
                summary="DNS resolution failed for all probe domains.",
            )

        https_successes = 0
        for host in HTTPS_TARGETS:
            probe = self._https_head(host)
            probes.append(probe)
            https_successes += int(probe.ok)

        if https_successes >= 2:
            return ConnectivityReport(
                state=NetworkState.HEALTHY,
                checked_at=checked_at,
                current_ssid=current_ssid,
                ipv4=ipv4,
                gateway=gateway,
                probes=probes,
                summary="Network is healthy.",
            )

        return ConnectivityReport(
            state=NetworkState.INTERNET_ISSUE,
            checked_at=checked_at,
            current_ssid=current_ssid,
            ipv4=ipv4,
            gateway=gateway,
            probes=probes,
            summary="Public HTTPS probes did not reach quorum.",
        )

    def _get_primary_ipv4(self) -> str | None:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            sock.connect(("8.8.8.8", 80))
            ip = sock.getsockname()[0]
            if ip and not ip.startswith("127."):
                return ip
        except OSError:
            return None
        finally:
            sock.close()
        return None

    def _get_default_gateway(self) -> str | None:
        try:
            result = subprocess.run(
                ["route", "print", "-4", "0.0.0.0"],
                check=False,
                capture_output=True,
                text=True,
                timeout=3,
                encoding="utf-8",
                errors="replace",
            )
        except (OSError, subprocess.SubprocessError):
            return None

        for line in result.stdout.splitlines():
            columns = line.split()
            if len(columns) >= 5 and columns[0] == "0.0.0.0" and columns[1] == "0.0.0.0":
                candidate = columns[2]
                if re.match(r"^\d{1,3}(\.\d{1,3}){3}$", candidate):
                    return candidate
        return None

    def _ping(self, host: str, name: str) -> ConnectivityProbe:
        started = time.perf_counter()
        try:
            result = subprocess.run(
                ["ping", "-n", "1", "-w", str(int(self.policy.probe_timeout_seconds * 1000)), host],
                check=False,
                capture_output=True,
                text=True,
                timeout=self.policy.probe_timeout_seconds + 1,
                encoding="utf-8",
                errors="replace",
            )
        except (OSError, subprocess.SubprocessError) as exc:
            return ConnectivityProbe(name, False, detail=str(exc))
        elapsed = (time.perf_counter() - started) * 1000
        return ConnectivityProbe(name, result.returncode == 0, elapsed, host)

    def _resolve(self, host: str) -> ConnectivityProbe:
        started = time.perf_counter()
        try:
            socket.getaddrinfo(host, 443, type=socket.SOCK_STREAM)
        except OSError as exc:
            return ConnectivityProbe(f"DNS {host}", False, detail=str(exc))
        elapsed = (time.perf_counter() - started) * 1000
        return ConnectivityProbe(f"DNS {host}", True, elapsed, "Resolved.")

    def _https_head(self, host: str) -> ConnectivityProbe:
        started = time.perf_counter()
        try:
            conn = http.client.HTTPSConnection(host, timeout=self.policy.probe_timeout_seconds)
            conn.request("HEAD", "/", headers={"User-Agent": "NetGuardian/0.1"})
            response = conn.getresponse()
            response.read()
            ok = 200 <= response.status < 500
            detail = f"HTTP {response.status}"
        except OSError as exc:
            return ConnectivityProbe(f"HTTPS {host}", False, detail=str(exc))
        finally:
            try:
                conn.close()  # type: ignore[name-defined]
            except Exception:
                pass
        elapsed = (time.perf_counter() - started) * 1000
        return ConnectivityProbe(f"HTTPS {host}", ok, elapsed, detail)

