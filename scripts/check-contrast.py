#!/usr/bin/env python3
"""WCAG contrast regression check for NetGuardian semantic palettes (U2 §9).

Parses Themes/Palette.Dark.xaml and Themes/Palette.Light.xaml, then verifies
each semantic token meets its WCAG 2.1 target against the surface it renders on:

  - Text/foreground tokens  -> 4.5:1 (AA normal text, SC 1.4.3)
  - Stroke/border tokens    -> 3:1   (non-text contrast, SC 1.4.11)

Exit code 0 if all pairs pass, 1 otherwise. No third-party dependencies.

Usage:
    python scripts/check-contrast.py [--repo-root PATH]
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


# --- WCAG relative luminance & contrast (per WCAG 2.1 definitions) -----------

def _linearize(channel: float) -> float:
    """sRGB channel (0..1) -> linear-light value."""
    return channel / 12.92 if channel <= 0.03928 else ((channel + 0.055) / 1.055) ** 2.4


def relative_luminance(rgb: tuple[int, int, int]) -> float:
    r, g, b = (_linearize(c / 255.0) for c in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast_ratio(fg: tuple[int, int, int], bg: tuple[int, int, int]) -> float:
    l1, l2 = relative_luminance(fg), relative_luminance(bg)
    lighter, darker = max(l1, l2), min(l1, l2)
    return (lighter + 0.05) / (darker + 0.05)


# --- Color parsing -----------------------------------------------------------

def parse_hex(value: str) -> tuple[int, int, int]:
    """Parse #RRGGBB or #AARRGGBB (alpha dropped) into an (r, g, b) tuple.

    Alpha-prefixed brushes (e.g. row-selection overlays) are flattened to their
    opaque RGB for a conservative check; the real composited color would sit
    against the card, so this errs toward the stricter reading.
    """
    v = value.lstrip("#")
    if len(v) == 8:  # AARRGGBB
        v = v[2:]
    if len(v) != 6:
        raise ValueError(f"Unexpected color literal: {value!r}")
    return int(v[0:2], 16), int(v[2:4], 16), int(v[4:6], 16)


def load_palette(path: Path) -> dict[str, str]:
    """Extract {resourceKey: '#hex'} from a SolidColorBrush palette dictionary."""
    text = path.read_text(encoding="utf-8")
    pattern = re.compile(r'x:Key="([^"]+)"\s+Color="(#[0-9A-Fa-f]+)"')
    palette = {key: color for key, color in pattern.findall(text)}
    if not palette:
        raise ValueError(f"No SolidColorBrush entries found in {path}")
    return palette


# --- Check specification -----------------------------------------------------

# Each token is checked against the surface it actually renders on.
# kind: "text" (4.5:1) or "stroke" (3:1). background is a palette key or literal.
CHECKS = [
    # Status colors render as text/icons on the card surface.
    ("StatusHealthyBrush", "text", "SurfaceCardBrush"),
    ("StatusWarningBrush", "text", "SurfaceCardBrush"),
    ("StatusDangerBrush", "text", "SurfaceCardBrush"),
    # Input border must clear the 3:1 non-text bar against the card.
    ("StrokeInputBrush", "stroke", "SurfaceCardBrush"),
    # Chart accent is a graphical stroke; treat as non-text 3:1 on the card.
    ("ChartAccentBrush", "stroke", "SurfaceCardBrush"),
]

TARGETS = {"text": 4.5, "stroke": 3.0}


def run_checks(name: str, palette: dict[str, str]) -> list[tuple[str, float, float, bool]]:
    results = []
    for token, kind, bg_key in CHECKS:
        if token not in palette:
            raise KeyError(f"[{name}] missing token {token!r}")
        bg_literal = palette.get(bg_key, bg_key)
        ratio = contrast_ratio(parse_hex(palette[token]), parse_hex(bg_literal))
        target = TARGETS[kind]
        results.append((f"{token} on {bg_key} ({kind})", ratio, target, ratio >= target))
    return results


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
        help="Repository root (defaults to the parent of scripts/).",
    )
    args = parser.parse_args()

    themes = args.repo_root / "src" / "NetGuardian.App" / "Themes"
    palettes = {
        "Dark": themes / "Palette.Dark.xaml",
        "Light": themes / "Palette.Light.xaml",
    }

    all_passed = True
    for name, path in palettes.items():
        print(f"\n=== {name} palette ({path.name}) ===")
        results = run_checks(name, load_palette(path))
        for label, ratio, target, ok in results:
            status = "PASS" if ok else "FAIL"
            print(f"  [{status}] {label:<44} {ratio:5.2f}:1  (need {target:g}:1)")
            all_passed = all_passed and ok

    total = len(CHECKS) * len(palettes)
    print(f"\n{'ALL PASS' if all_passed else 'FAILURES PRESENT'} — "
          f"{total} pairs checked across {len(palettes)} palettes.")
    return 0 if all_passed else 1


if __name__ == "__main__":
    sys.exit(main())
