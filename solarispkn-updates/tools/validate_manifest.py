#!/usr/bin/env python3
"""Validate SolarisPKN Updates release feeds (stdlib only; no execution)."""
# SPDX-License-Identifier: GPL-3.0-only
import json
import re
import sys
from pathlib import Path
from urllib.parse import urlparse, unquote

HEX40 = re.compile(r"^[a-fA-F0-9]{40}$")
HEX64 = re.compile(r"^[a-fA-F0-9]{64}$")
VER = re.compile(r"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?$")
NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$")
PREFIX = "/SolarisPKN/SolarisPKN-OS/releases/download/"
ALLOWED = {"solarispkn-paste", "solarispkn-verity"}


def validate(path: Path, stable=True):
    if path.stat().st_size > 1048576:
        raise ValueError("feed exceeds 1 MiB")
    doc = json.loads(path.read_text(encoding="utf-8"))
    if doc.get("schema") != 1 or doc.get("channel") != ("stable" if stable else "candidate"):
        raise ValueError("invalid schema or channel")
    products = doc.get("products")
    if not isinstance(products, list):
        raise ValueError("missing products")
    found = set()
    seen = set()
    for product in products:
        pid = product.get("id")
        if pid not in ALLOWED or pid in found:
            raise ValueError("unknown or duplicate product")
        found.add(pid)
        releases = product.get("releases")
        if not isinstance(releases, list):
            raise ValueError("invalid releases list")
        for release in releases:
            v = release.get("version")
            if not isinstance(v, str) or not VER.fullmatch(v):
                raise ValueError("invalid version")
            if (pid, v) in seen:
                raise ValueError("duplicate release")
            seen.add((pid, v))
            approved = release.get("approved") is True
            if stable and not approved:
                raise ValueError("stable channel contains unapproved release")
            validation = release.get("validation") or {}
            asset = release.get("asset") or {}
            if not approved:
                continue
            if (validation.get("passed") is not True or
                    type(validation.get("runId")) is not int or validation["runId"] <= 0 or
                    not HEX40.fullmatch(str(validation.get("sourceSha", "")))):
                raise ValueError("approved release lacks successful CI provenance")
            if (asset.get("platform") != "windows" or
                    asset.get("architecture") not in ("x64", "arm64") or
                    type(asset.get("minOsBuild")) is not int or
                    asset["minOsBuild"] < 19045 or
                    type(asset.get("maxOsBuild")) is not int or
                    asset["maxOsBuild"] < 0 or
                    type(asset.get("bytes")) is not int or
                    not 0 < asset["bytes"] <= 1024**3):
                raise ValueError("invalid architecture, compatibility or size")
            filename = asset.get("fileName", "")
            if not NAME.fullmatch(filename) or not filename.endswith(".exe"):
                raise ValueError("untrusted installer file name")
            if not HEX64.fullmatch(str(asset.get("sha256", ""))):
                raise ValueError("missing verified SHA-256")
            parsed = urlparse(asset.get("url", ""))
            parts = parsed.path[len(PREFIX):].split("/") if parsed.path.startswith(PREFIX) else []
            if (parsed.scheme != "https" or parsed.netloc != "github.com" or
                    parsed.query or parsed.fragment or len(parts) != 2 or
                    not NAME.fullmatch(parts[0]) or unquote(parts[1]) != filename):
                raise ValueError("untrusted release URL")
    return sum(1 for p in products for x in p["releases"] if x.get("approved") is True)


if __name__ == "__main__":
    try:
        target = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(
            "solarispkn-updates/channels/stable.json")
        is_stable = target.name == "stable.json"
        count = validate(target, stable=is_stable)
        print(f"UPDATES_FEED_PASS file={target} approved_releases={count}")
    except (OSError, ValueError, TypeError, KeyError, json.JSONDecodeError) as error:
        print("UPDATES_FEED_FAIL:", error, file=sys.stderr)
        sys.exit(2)
