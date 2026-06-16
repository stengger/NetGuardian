from __future__ import annotations

import html
import re
import subprocess
import tempfile
from pathlib import Path

from .models import WifiNetwork


class WifiManager:
    def scan(self) -> list[WifiNetwork]:
        result = self._netsh(["wlan", "show", "networks", "mode=bssid"], timeout=10)
        return parse_scan_output(result.stdout)

    def current_ssid(self) -> str | None:
        result = self._netsh(["wlan", "show", "interfaces"], timeout=5)
        return parse_current_ssid(result.stdout)

    def connect(self, ssid: str) -> bool:
        if not ssid:
            return False
        result = self._netsh(["wlan", "connect", f"name={ssid}", f"ssid={ssid}"], timeout=15)
        return result.returncode == 0

    def disconnect(self) -> bool:
        result = self._netsh(["wlan", "disconnect"], timeout=10)
        return result.returncode == 0

    def add_profile(self, ssid: str, password: str | None = None) -> bool:
        xml = build_wlan_profile_xml(ssid, password)
        temp_path: Path | None = None
        try:
            with tempfile.NamedTemporaryFile(
                "w", suffix=".xml", encoding="utf-8", delete=False
            ) as handle:
                temp_path = Path(handle.name)
                handle.write(xml)
            result = self._netsh(["wlan", "add", "profile", f"filename={temp_path}", "user=current"], timeout=10)
            return result.returncode == 0
        finally:
            if temp_path and temp_path.exists():
                temp_path.unlink(missing_ok=True)

    def _netsh(self, args: list[str], timeout: int) -> subprocess.CompletedProcess[str]:
        try:
            return subprocess.run(
                ["netsh", *args],
                check=False,
                capture_output=True,
                text=True,
                timeout=timeout,
                encoding="utf-8",
                errors="replace",
            )
        except (OSError, subprocess.SubprocessError) as exc:
            return subprocess.CompletedProcess(["netsh", *args], 1, "", str(exc))


def parse_current_ssid(output: str) -> str | None:
    for line in output.splitlines():
        if "BSSID" in line:
            continue
        if re.search(r"(^|\s)(SSID|SSID 名称)\s*:", line, flags=re.IGNORECASE):
            value = line.split(":", 1)[1].strip()
            return value or None
    return None


def parse_scan_output(output: str) -> list[WifiNetwork]:
    networks: list[WifiNetwork] = []
    current: dict[str, str | int | None] | None = None

    for raw_line in output.splitlines():
        line = raw_line.strip()
        ssid_match = re.match(r"SSID\s+\d+\s*:\s*(.*)$", line, flags=re.IGNORECASE)
        if not ssid_match:
            ssid_match = re.match(r"SSID\s*\d*\s*名称\s*:\s*(.*)$", line, flags=re.IGNORECASE)
        if ssid_match:
            if current and current.get("ssid"):
                networks.append(_network_from_dict(current))
            current = {"ssid": ssid_match.group(1).strip(), "signal": None, "auth": None, "encryption": None}
            continue

        if current is None:
            continue

        if ":" not in line:
            continue
        key, value = [part.strip() for part in line.split(":", 1)]
        key_lower = key.lower()
        if key_lower.startswith("signal") or key.startswith("信号"):
            signal_match = re.search(r"(\d+)", value)
            if signal_match:
                current["signal"] = int(signal_match.group(1))
        elif key_lower.startswith("authentication") or key.startswith("身份验证"):
            current["auth"] = value
        elif key_lower.startswith("encryption") or key.startswith("加密"):
            current["encryption"] = value

    if current and current.get("ssid"):
        networks.append(_network_from_dict(current))

    seen: set[str] = set()
    unique: list[WifiNetwork] = []
    for network in sorted(networks, key=lambda item: (item.ssid.lower(), -(item.signal_percent or 0))):
        if network.ssid not in seen:
            unique.append(network)
            seen.add(network.ssid)
    return unique


def _network_from_dict(data: dict[str, str | int | None]) -> WifiNetwork:
    return WifiNetwork(
        ssid=str(data.get("ssid") or ""),
        signal_percent=data.get("signal") if isinstance(data.get("signal"), int) else None,
        authentication=data.get("auth") if isinstance(data.get("auth"), str) else None,
        encryption=data.get("encryption") if isinstance(data.get("encryption"), str) else None,
    )


def build_wlan_profile_xml(ssid: str, password: str | None = None) -> str:
    escaped_ssid = html.escape(ssid, quote=True)
    if password:
        escaped_password = html.escape(password, quote=True)
        auth_block = f"""
        <authEncryption>
          <authentication>WPA2PSK</authentication>
          <encryption>AES</encryption>
          <useOneX>false</useOneX>
        </authEncryption>
        <sharedKey>
          <keyType>passPhrase</keyType>
          <protected>false</protected>
          <keyMaterial>{escaped_password}</keyMaterial>
        </sharedKey>"""
    else:
        auth_block = """
        <authEncryption>
          <authentication>open</authentication>
          <encryption>none</encryption>
          <useOneX>false</useOneX>
        </authEncryption>"""

    return f"""<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{escaped_ssid}</name>
  <SSIDConfig>
    <SSID>
      <name>{escaped_ssid}</name>
    </SSID>
  </SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM>
    <security>{auth_block}
    </security>
  </MSM>
</WLANProfile>
"""

