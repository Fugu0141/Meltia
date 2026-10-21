#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-B5: reconcile offline T4-A/B1/B3 observations and T4-B4 staged PE audit.

All external observations are untrusted; no report authorizes TSF registration.
No registry, COM, TSF, native-DLL load, process execution, or filesystem writes.
"""
import argparse
import json
from pathlib import Path
import sys

import meltia_t4b_audit_gate as b1
import meltia_t4b2_ownership_model as b2
import meltia_t4b3_evidence_gate as b3
import meltia_t4b4_native_artifact_gate as b4
from meltia_t4a_preflight import MANIFEST, validate_preflight_manifest

MAX_EVIDENCE_BYTES = 131072
CHECK_KEYS = ("registry_and_profile", "display_attributes_and_categories",
              "staged_native_artifacts")
UNRESOLVED = (
    "Actual T4-A/T4-B1/T4-B3 observations from one independently verified isolated VM",
    "TSF display-attribute absence and cross-user/scope ownership are not proven",
    "Per-resource ownership receipts, independent pre/post snapshots and rollback validation",
    "T3a registration-rejection behavior and build provenance are not proven by PE headers",
    "No OS registration, Windows key-input E2E or coexistence validation has occurred",
)


def _unique_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON object key")
        result[key] = value
    return result


def read_evidence(path):
    """Strict bounded JSON reader supporting PowerShell 5.1/7 BOM encodings."""
    if path.is_symlink() or not path.is_file():
        raise ValueError("evidence path must be an ordinary file")
    if not 0 < path.stat().st_size <= MAX_EVIDENCE_BYTES:
        raise ValueError("evidence file size outside limit")
    raw = path.read_bytes()
    if not 0 < len(raw) <= MAX_EVIDENCE_BYTES:
        raise ValueError("evidence bytes outside limit")
    encoding = "utf-16" if raw.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig"
    return json.loads(raw.decode(encoding), object_pairs_hook=_unique_keys)


def blocked(reason="invalid-or-unreadable-input"):
    return {
        "phase": "T4-B5", "status": "blocked-inconclusive",
        "registration_authorized": False, "registry_writes": False,
        "native_tsf_access": False, "native_dll_executed": False,
        "automatic_rollback": False, "independent_vm_provenance_verified": False,
        "checks": dict.fromkeys(CHECK_KEYS, "not-evaluated"),
        "blocked_by": list(UNRESOLVED),
        "reason_code": reason,
    }


def assess(manifest, t4a, tsf, attribute_evidence, staging_root):
    """Pure observation reconciliation plus a read-only reinspection of staged DLLs.

    A clean *bounded* observation cannot certify another user/registry scope,
    a real build, or registry mutations. There is deliberately no success state.
    """
    report = blocked("preregistration-review-only")
    try:
        validate_preflight_manifest(manifest)
        inventory = b2.design_inventory(manifest)
        if len(inventory["resources"]) != 8 or len(
                inventory["display_attributes_unverified"]) != 3:
            raise ValueError("unexpected T4-B2 ownership model")
        b4.validate_manifest(manifest)
    except (ValueError, KeyError, TypeError, AttributeError):
        return report

    checks = report["checks"]
    try:
        # B1 prioritizes "inconclusive" for its overall result. A verified
        # present resource must still take precedence over unrelated unknowns
        # in this strictly blocking cross-gate report.
        identity = manifest["identity"]
        b1.validate_t4a(t4a, identity)
        b1.validate_tsf(tsf, identity)
        if (t4a["counts"]["occupied"] > 0 or tsf["clsid_matches"] > 0
                or tsf["profile_guid_matches"] > 0):
            checks["registry_and_profile"] = "occupied"
        else:
            checks["registry_and_profile"] = b1.assess(t4a, tsf, manifest)["status"]
    except (ValueError, KeyError, TypeError, AttributeError):
        checks["registry_and_profile"] = "inconclusive"

    try:
        # Distinguish valid negative/unknown evidence from malformed records.
        # B3's public assess() intentionally returns inconclusive for BOTH.
        b3.validate_evidence(manifest, attribute_evidence)
        checks["display_attributes_and_categories"] = b3.assess(
            manifest, attribute_evidence)["status"]
    except (ValueError, KeyError, TypeError, AttributeError):
        checks["display_attributes_and_categories"] = "invalid-evidence"

    try:
        artifact_report = b4.verify_staging_directory(staging_root, manifest)
        if (artifact_report.get("status") != "structural-audit-only-not-authorized"
                or artifact_report.get("registration_authorized") is not False
                or artifact_report.get("build_provenance_proven") is not False
                or artifact_report.get("registration_stubs_proven") is not False):
            raise ValueError("artifact report violates safety boundary")
        checks["staged_native_artifacts"] = "structural-audit-only"
    except (OSError, ValueError, KeyError, TypeError, AttributeError):
        checks["staged_native_artifacts"] = "inconclusive"

    if ("occupied" in checks.values()):
        report["status"] = "blocked-occupied"
        report["reason_code"] = "observed-identity-conflict"
    elif (checks["registry_and_profile"] != "audit-clear-not-authorized"
          or checks["display_attributes_and_categories"] != "inconclusive"
          or checks["staged_native_artifacts"] != "structural-audit-only"):
        report["reason_code"] = "missing-invalid-or-incomplete-evidence"
    else:
        # T4-B3 is necessarily inconclusive on negative/absent observations.
        report["reason_code"] = "partial-evidence-never-authorizes-registration"
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--t4a", type=Path, required=True)
    parser.add_argument("--tsf", type=Path, required=True)
    parser.add_argument("--attributes", type=Path, required=True)
    parser.add_argument("--staging-root", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        manifest = read_evidence(MANIFEST)
        report = assess(manifest, read_evidence(args.t4a), read_evidence(args.tsf),
                        read_evidence(args.attributes), args.staging_root)
    except (OSError, ValueError, TypeError, UnicodeError, KeyError):
        # Never print filesystem paths, personal data, or opaque OS exceptions.
        report = blocked()
    print(json.dumps(report, ensure_ascii=False, sort_keys=True))
    # Never return a green/authorization exit code, even for synthetic input.
    return 1 if report["status"] == "blocked-occupied" else 2


if __name__ == "__main__":
    sys.exit(main())
