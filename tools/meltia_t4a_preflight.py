#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-A read-only Windows registry *preflight*, never registration authorization.

Use on an isolated Windows VM before any separate T4-B review. This performs
only bounded registry KEY_READ queries; it never invokes TSF COM or changes OS
state. Missing keys do NOT prove that TSF registration would be safe.
"""
import json
from pathlib import Path
import sys

from test_meltia_t2_identity_contract import legacy_guid_set, validate_identity

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "config/tsf/meltia-t2-identity.json"
GUID_FIELDS = (
    "clsid", "profileGuid", "inputAttributeGuid",
    "convertedAttributeGuid", "targetAttributeGuid",
)
HIVES = ("HKLM", "HKCU")
VIEWS = ("64", "32")
REGISTRY_ROOTS = (
    r"Software\Classes\CLSID",
    r"Software\Microsoft\CTF\TIP",
)


def validate_preflight_manifest(manifest):
    """Do not permit the preflight when T2 is no longer design-only."""
    validate_identity(manifest, legacy_guid_set())
    if manifest.get("registrationEnabled") is not False:
        raise ValueError("T4-A requires disabled TSF registration")
    deployment = manifest.get("deployment")
    if not isinstance(deployment, dict) or not deployment or any(
        value is not False for value in deployment.values()
    ):
        raise ValueError("T4-A refuses a manifest with enabled deployment")
    return manifest["identity"]


def planned_probes(identities):
    """Conservative bounded inventory, *not* a full TSF registration survey."""
    for field in GUID_FIELDS:
        guid = "{" + identities[field].upper() + "}"
        for hive in HIVES:
            for view in VIEWS:
                for root in REGISTRY_ROOTS:
                    yield {
                        "identity": field,
                        "hive": hive,
                        "view": view,
                        "key": root + "\\" + guid,
                    }


def read_key(hive, view, path):
    """Return True=present, False=absent, None=not reliably determined.

    No value reads, writes, enumeration, subprocess or COM activation. An
    unsupported 32/64-bit view is inconclusive, not an absent key.
    """
    import winreg  # Windows-only; imported when actual VM checking is requested

    root = winreg.HKEY_LOCAL_MACHINE if hive == "HKLM" else winreg.HKEY_CURRENT_USER
    wow_flag = winreg.KEY_WOW64_64KEY if view == "64" else winreg.KEY_WOW64_32KEY
    try:
        with winreg.OpenKey(root, path, 0, winreg.KEY_READ | wow_flag):
            return True
    except FileNotFoundError:
        return False
    except OSError:
        return None


def inspect(identities, probe):
    """Dependency-injected pure report logic; no OS access without a probe."""
    findings = []
    for item in planned_probes(identities):
        try:
            result = probe(item["hive"], item["view"], item["key"])
        except OSError:
            result = None
        if result not in (True, False, None):
            raise ValueError("registry probe must return True, False or None")
        findings.append({**item, "finding":
                         "occupied" if result is True else
                         "absent" if result is False else "unknown"})
    counts = {
        state: sum(item["finding"] == state for item in findings)
        for state in ("occupied", "absent", "unknown")
    }
    status = ("inconclusive" if counts["unknown"] else
              "occupied" if counts["occupied"] else "precheck-clear")
    return {
        "phase": "T4-A",
        "status": status,
        "registry_writes": False,
        "registration_authorized": False,
        "note": "Bounded registry key inventory only; never proof of TSF safety.",
        "counts": counts,
        "findings": findings,
    }


def main():
    # No stdout text other than machine-readable JSON.
    try:
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        identities = validate_preflight_manifest(manifest)
        if sys.platform != "win32":
            raise RuntimeError("Windows registry inventory requires an isolated Windows VM")
        report = inspect(identities, read_key)
        code = 0 if report["status"] == "precheck-clear" else (
            1 if report["status"] == "occupied" else 2
        )
    except (ValueError, KeyError, OSError, RuntimeError) as exc:
        report = {
            "phase": "T4-A",
            "status": "inconclusive",
            "registry_writes": False,
            "registration_authorized": False,
            "reason": str(exc),
        }
        code = 2
    print(json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True))
    return code


if __name__ == "__main__":
    sys.exit(main())
