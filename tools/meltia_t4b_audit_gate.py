#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-B1 offline evidence validation; NEVER an authorization to register TSF.

Use *separately captured* T4-A and read-only TSF profile inventory JSON.
No Windows API access, subprocess, system state mutation, or file writes.
"""
import argparse
import json
from pathlib import Path
import sys
from uuid import UUID

from meltia_t4a_preflight import (
    MANIFEST, inspect, planned_probes, validate_preflight_manifest,
)


def normalized_guid(value):
    if not isinstance(value, str):
        raise ValueError("GUID must be text")
    return str(UUID(value.strip("{}")))


def validate_t4a(report, identity):
    """Refuse abridged, tampered, inconsistent or incompletely probed reports."""
    if (report.get("phase") != "T4-A" or report.get("registry_writes") is not False
            or report.get("registration_authorized") is not False):
        raise ValueError("T4-A phase/readonly flags invalid")
    findings = report.get("findings")
    plan = list(planned_probes(identity))
    if not isinstance(findings, list) or len(findings) != len(plan):
        raise ValueError("T4-A must supply the complete 40-probe report")
    counts = dict.fromkeys(("occupied", "absent", "unknown"), 0)
    for found, expected in zip(findings, plan):
        if not isinstance(found, dict) or any(found.get(k) != v
                                              for k, v in expected.items()):
            raise ValueError("T4-A probe target/order does not match identity")
        state = found.get("finding")
        if state not in counts:
            raise ValueError("T4-A unknown probe finding")
        counts[state] += 1
    if report.get("counts") != counts:
        raise ValueError("T4-A counts do not match findings")
    status = ("inconclusive" if counts["unknown"] else
              "occupied" if counts["occupied"] else "precheck-clear")
    if report.get("status") != status:
        raise ValueError("T4-A status does not match findings")
    return status


def validate_tsf(report, identity):
    """Audit visible profiles, not global attribute ownership or registration."""
    if (report.get("phase") != "T4-B1" or
            report.get("coverage") != "current-user-visible-tsf-profiles" or
            report.get("registry_writes") is not False or
            report.get("registration_authorized") is not False):
        raise ValueError("TSF inventory metadata invalid")
    if (normalized_guid(report.get("expected_clsid")) != identity["clsid"] or
            normalized_guid(report.get("expected_profile_guid")) != identity["profileGuid"]):
        raise ValueError("TSF inventory used different GUIDs")
    count_fields = ("profile_count", "clsid_matches", "profile_guid_matches")
    counts = [report.get(k) for k in count_fields]
    if any(type(n) is not int or n < 0 or n > 100000 for n in counts):
        raise ValueError("invalid TSF profile counters")
    if counts[1] > counts[0] or counts[2] > counts[0]:
        raise ValueError("TSF matches cannot exceed enumerated profiles")
    complete = report.get("complete")
    if type(complete) is not bool:
        raise ValueError("TSF completeness is required")
    actual = ("inconclusive" if not complete else
              "occupied" if counts[1] or counts[2] else "enumerated-clear")
    if report.get("status") != actual:
        raise ValueError("TSF status does not match results")
    return actual


def assess(t4a, tsf, manifest):
    identity = validate_preflight_manifest(manifest)
    try:
        registry_status = validate_t4a(t4a, identity)
        profile_status = validate_tsf(tsf, identity)
        status = ("inconclusive" if "inconclusive" in (registry_status, profile_status)
                  else "occupied" if "occupied" in (registry_status, profile_status)
                  else "audit-clear-not-authorized")
        reason = ("Complete bounded registry and visible TSF profile checks only; "
                  "attribute ownership, other users, rollback and VM E2E are not verified.")
    except (ValueError, TypeError, KeyError, AttributeError) as exc:
        status = "inconclusive"
        reason = f"Evidence rejected: {exc}"
    return {
        "phase": "T4-B1",
        "status": status,
        "registration_authorized": False,
        "registry_writes": False,
        "reason": reason,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--t4a", type=Path, required=True, help="T4-A captured JSON")
    parser.add_argument("--tsf", type=Path, required=True, help="native TSF inventory JSON")
    args = parser.parse_args()
    try:
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        t4a = json.loads(args.t4a.read_text(encoding="utf-8"))
        tsf = json.loads(args.tsf.read_text(encoding="utf-8"))
        report = assess(t4a, tsf, manifest)
    except (ValueError, OSError, UnicodeError) as exc:
        report = {
            "phase": "T4-B1", "status": "inconclusive",
            "registration_authorized": False, "registry_writes": False,
            "reason": f"Unable to read audit inputs: {exc}",
        }
    print(json.dumps(report, sort_keys=True, ensure_ascii=False))
    return 0 if report["status"] == "audit-clear-not-authorized" else (
        1 if report["status"] == "occupied" else 2
    )


if __name__ == "__main__":
    sys.exit(main())
