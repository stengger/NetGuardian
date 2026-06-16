from __future__ import annotations

import json
import os
from dataclasses import asdict
from pathlib import Path
from typing import Any

from .models import MonitorPolicy, WifiProfile


APP_DIR_NAME = "NetGuardian"
SETTINGS_FILE_NAME = "settings.json"


def default_settings_path() -> Path:
    app_data = os.getenv("APPDATA")
    if app_data:
        return Path(app_data) / APP_DIR_NAME / SETTINGS_FILE_NAME
    return Path.home() / f".{APP_DIR_NAME.lower()}" / SETTINGS_FILE_NAME


class SettingsStore:
    def __init__(self, path: Path | None = None) -> None:
        self.path = path or default_settings_path()

    def load(self) -> tuple[MonitorPolicy, list[WifiProfile]]:
        if not self.path.exists():
            return MonitorPolicy(), []

        with self.path.open("r", encoding="utf-8") as handle:
            data = json.load(handle)

        policy = MonitorPolicy(**data.get("policy", {}))
        policy.normalize()
        profiles = [
            WifiProfile.from_dict(item)
            for item in data.get("profiles", [])
            if str(item.get("ssid", "")).strip()
        ]
        profiles.sort(key=lambda item: item.priority)
        return policy, profiles

    def save(self, policy: MonitorPolicy, profiles: list[WifiProfile]) -> None:
        policy.normalize()
        self.path.parent.mkdir(parents=True, exist_ok=True)
        payload: dict[str, Any] = {
            "policy": asdict(policy),
            "profiles": [profile.to_dict() for profile in sorted(profiles, key=lambda p: p.priority)],
        }
        temp_path = self.path.with_suffix(".tmp")
        with temp_path.open("w", encoding="utf-8") as handle:
            json.dump(payload, handle, indent=2, ensure_ascii=False)
        temp_path.replace(self.path)

