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
    identity = manifest["identity"]
    # Validate each observation *independently*. An invalid T4-B1 report must
    # never conceal a validated T4-A occupancy (and vice versa). Existing B1
    # assess() combines them inside one try and cannot supply this precedence.
    try:
        registry_state = b1.validate_t4a(t4a, identity)
        if t4a["counts"]["occupied"] > 0:
            registry_state = "occupied"
    except (ValueError, KeyError, TypeError, AttributeError):
        registry_state = "invalid-evidence"
    try:
        profile_state = b1.validate_tsf(tsf, identity)
        if tsf["clsid_matches"] > 0 or tsf["profile_guid_matches"] > 0:
            profile_state = "occupied"
    except (ValueError, KeyError, TypeError, AttributeError):
        profile_state = "invalid-evidence"

    report["source_checks"] = {
        "t4a": registry_state, "t4b1_profiles": profile_state,
    }
    if "occupied" in (registry_state, profile_state):
        checks["registry_and_profile"] = "occupied"
    elif (registry_state == "precheck-clear"
          and profile_state == "enumerated-clear"):
        checks["registry_and_profile"] = "audit-clear-not-authorized"
    else:
        checks["registry_and_profile"] = "inconclusive"

    b3_incomplete = False
    try:
        # Distinguish valid negative/unknown evidence from malformed records.
        # B3's public assess() intentionally returns inconclusive for BOTH.
        b3.validate_evidence(manifest, attribute_evidence)
        checks["display_attributes_and_categories"] = b3.assess(
            manifest, attribute_evidence)["status"]
        # Counts only: never expose foreign owners, GUIDs, paths or user data.
        attrs = attribute_evidence["attributes"]
        categories = attribute_evidence["categories"]
        failed = sum(item["lookup"] == "failed" for item in attrs)
        incomplete = sum(item["complete"] is False for item in categories)
        report["b3_observation_summary"] = {
            "attribute_lookups_failed": failed,
            "attribute_lookups_not_found": sum(
                item["lookup"] == "not-found" for item in attrs),
            "category_enumerations_incomplete": incomplete,
            "categories_enumerated_absent": sum(
                item["complete"] is True and item["present"] is False
                for item in categories),
        }
        b3_incomplete = failed > 0 or incomplete > 0
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
    elif b3_incomplete:
        report["reason_code"] = "incomplete-or-failed-tsf-evidence"
    else:
        # Even fully enumerated negatives remain limited to visible scopes.
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
        # Invalid T2 manifest is a global stop. Do not evaluate other input.
        validate_preflight_manifest(manifest)
        b2.design_inventory(manifest)
        b4.validate_manifest(manifest)
    except (OSError, ValueError, TypeError, UnicodeError, KeyError, AttributeError):
        report = blocked("invalid-manifest")
    else:
        # Missing/malformed evidence is local to one source, not a global abort.
        # Continue evaluating the other independent sources for occupancy.
        def optional_evidence(path):
            try:
                return read_evidence(path)
            except (OSError, ValueError, TypeError, UnicodeError, KeyError):
                return None

        report = assess(manifest, optional_evidence(args.t4a),
                        optional_evidence(args.tsf),
                        optional_evidence(args.attributes), args.staging_root)
    print(json.dumps(report, ensure_ascii=False, sort_keys=True))
    # Never return a green/authorization exit code, even for synthetic input.
    return 1 if report["status"] == "blocked-occupied" else 2


if __name__ == "__main__":
    sys.exit(main())
