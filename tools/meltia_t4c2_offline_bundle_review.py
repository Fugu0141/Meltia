#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-C2: read-only, fail-closed review of a T4-C1 evidence bundle.

Synthetic CI or PRIVATE offline VM evidence only. Does not start any process,
invoke COM/TSF, read the Windows registry, load a DLL, or write to the disk.
Hashes detect accidental bundle drift; they do NOT attest a VM or operator.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

import meltia_t4b5_offline_review_gate as b5
import meltia_t4c1_vm_evidence_capture as c1
from meltia_t4a_preflight import MANIFEST, validate_preflight_manifest
from meltia_t4b_audit_gate import validate_t4a

STAGES = ("t4a", "t4b1", "t4b3")
HASH_KEYS = ("profile_exe_sha256", "attribute_exe_sha256", "t2_manifest_sha256")
SHA256 = re.compile(r"[0-9a-f]{64}\Z")


def blocked(reason):
    return {
        "phase": "T4-C2",
        "status": "blocked-inconclusive",
        "reason_code": reason,
        "bundle_consistency": "inconsistent",
        "registration_authorized": False,
        "registry_writes": False,
        "native_tsf_access": False,
        "native_dll_executed": False,
        "automatic_rollback": False,
        "independent_vm_provenance_verified": False,
        "bundle_origin_authenticated": False,
        "checks": dict.fromkeys(STAGES, "untrusted"),
        "staged_native_artifacts": "untrusted",
    }


def strict_object(path):
    value = b5.read_evidence(path)
    if not isinstance(value, dict):
        raise ValueError("top-level JSON must be an object")
    return value


def load_bundle(directory):
    if directory.is_symlink() or not directory.is_dir():
        raise ValueError("bundle must be an existing ordinary directory")
    # The summary and each stage are independent inputs. One unreadable file
    # must not conceal an independently validated identity conflict elsewhere.
    try:
        summary = strict_object(directory / "review-summary.json")
    except (OSError, ValueError, TypeError, UnicodeError, KeyError):
        summary = None
    files = {}
    raw = {}
    for stage in STAGES:
        p = directory / (stage + ".json")
        try:
            if p.is_symlink() or not p.is_file():
                continue
            # Parse and hash exactly the same bounded bytes. Reading the file
            # twice could accept a mixed snapshot if it changes between reads.
            with p.open("rb") as stream:
                data = stream.read(c1.MAX_BYTES + 1)
            files[stage] = c1.read_json_bytes(data)
            raw[stage] = data
        except (OSError, ValueError, TypeError, UnicodeError, KeyError):
            continue
    return summary, files, raw


def summary_contract(summary, manifest, manifest_sha, files, raw):
    """Check known C1 schema, sequential stage observations and stored digests.

    This checks *internal* consistency only. A malicious party can replace both
    observations and hashes; do not infer VM provenance from a passing check.
    """
    if (not isinstance(summary, dict) or summary.get("phase") != "T4-C1"
            or summary.get("status") not in ("blocked-occupied", "blocked-inconclusive")
            or type(summary.get("reason_code")) is not str
            or summary.get("registration_authorized") is not False
            or summary.get("registry_writes_requested") is not False
            or summary.get("automatic_rollback") is not False
            or summary.get("independent_vm_provenance_verified") is not False
            or summary.get("actual_vm_snapshot_verified") is not False
            or summary.get("requires_manual_review") is not True
            or summary.get("scope") != "one-operator-claimed-vm-session-current-user-only"):
        return False

    digests = summary.get("binary_manifest_checksums")
    stage_hashes = summary.get("evidence_sha256")
    steps = summary.get("stage_checks")
    if (not isinstance(digests, dict) or set(digests) != set(HASH_KEYS)
            or any(type(digests[k]) is not str or not SHA256.fullmatch(digests[k])
                   for k in HASH_KEYS)
            or digests["t2_manifest_sha256"] != manifest_sha
            or not isinstance(stage_hashes, dict)
            or set(stage_hashes) != set(files)
            or not isinstance(steps, dict) or set(steps) != set(STAGES)):
        return False
    if any(type(stage_hashes[k]) is not str
           or not SHA256.fullmatch(stage_hashes[k])
           or hashlib.sha256(raw[k]).hexdigest() != stage_hashes[k]
           for k in files):
        return False

    valid_steps = {
        "t4a": ("precheck-clear", "occupied", "inconclusive", "invalid-or-unavailable", "not-run"),
        "t4b1": ("enumerated-clear", "occupied", "inconclusive", "invalid-or-unavailable", "not-run"),
        "t4b3": ("occupied", "inconclusive", "invalid-or-unavailable", "not-run"),
    }
    if any(type(steps[k]) is not str or steps[k] not in valid_steps[k]
           for k in STAGES):
        return False

    exits = {
        "t4a": {"precheck-clear": 0, "occupied": 1, "inconclusive": 2},
        "t4b1": {"enumerated-clear": 0, "occupied": 1, "inconclusive": 2},
        "t4b3": {"occupied": 1, "inconclusive": 2},
    }
    # Replay C1's strict ordering, stage validators and no-next-probe rules.
    # A missing/invalid stage is represented as a runner exception, not a fake
    # observation. All execution is in-memory and invokes NO OS observers.
    def replay_runner(stage):
        outcome = steps[stage]
        if outcome not in exits[stage] or stage not in files:
            raise ValueError("missing or invalid stage")
        # T4-A's summary is normalized by C1: an observed collision wins even
        # when the original registry probe reported unknown keys. In that case
        # the source was "inconclusive" (exit 2), not "occupied" (exit 1).
        # Recover the raw, validated source status before replaying C1.
        if stage == "t4a":
            source_status = validate_t4a(files[stage], manifest["identity"])
            code = exits[stage][source_status]
        else:
            code = exits[stage][outcome]
        return code, json.dumps(files[stage]).encode("utf-8")

    try:
        regenerated_files, regenerated = c1.capture(
            manifest, replay_runner, {stage: stage for stage in STAGES})
    except (ValueError, KeyError, TypeError, OSError):
        return False
    return (set(regenerated_files) == set(files)
            and regenerated["status"] == summary["status"]
            and regenerated["reason_code"] == summary["reason_code"]
            and regenerated["stage_checks"] == steps)


def assess_bundle(directory, staging_root):
    report = blocked("bundle-unavailable-or-inconsistent")
    try:
        manifest_raw = MANIFEST.read_bytes()
        manifest = c1.read_json_bytes(manifest_raw)
        validate_preflight_manifest(manifest)
    except (OSError, ValueError, TypeError, KeyError, UnicodeError):
        report["reason_code"] = "invalid-t2-manifest"
        return report

    try:
        summary, observed, raw = load_bundle(directory)
    except (OSError, ValueError, TypeError, KeyError, UnicodeError):
        # If the summary is missing or malformed, do not call the bundle trusted.
        # No conclusion about OS absence or ownership is possible.
        return report

    # Independent B5 inspection: an invalid C1 summary must not conceal an
    # individually well-formed occupied observation in the available files.
    b5_report = b5.assess(manifest, observed.get("t4a"),
                          observed.get("t4b1"), observed.get("t4b3"),
                          staging_root)
    report["checks"] = {
        "t4a": b5_report.get("source_checks", {}).get("t4a", "untrusted"),
        "t4b1": b5_report.get("source_checks", {}).get("t4b1_profiles", "untrusted"),
        "t4b3": b5_report["checks"]["display_attributes_and_categories"],
    }
    report["staged_native_artifacts"] = b5_report["checks"]["staged_native_artifacts"]

    consistent = False
    try:
        # Manifest must be immutable relative to the bundle. No live artifact
        # binary hashing is possible here; the two EXE digests are self-reported.
        consistent = summary_contract(
            summary, manifest, hashlib.sha256(manifest_raw).hexdigest(),
            observed, raw)
    except (ValueError, KeyError, TypeError, OSError, UnicodeError):
        consistent = False

    if consistent:
        report["bundle_consistency"] = "internally-consistent-unverified"
        report["reason_code"] = "limited-offline-review-never-authorizes-registration"
    if b5_report["status"] == "blocked-occupied":
        report["status"] = "blocked-occupied"
        report["reason_code"] = ("observed-identity-conflict" if consistent
                                 else "potential-identity-conflict-in-unverified-bundle")
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True,
                        help="existing PRIVATE T4-C1 evidence directory; never commit it")
    parser.add_argument("--staging-root", type=Path, required=True,
                        help="separate T3a registration-disabled x64/x86 stage")
    args = parser.parse_args(argv)
    try:
        report = assess_bundle(args.bundle, args.staging_root)
    except (OSError, ValueError, KeyError, TypeError, UnicodeError):
        report = blocked("offline-review-failed")
    # Do not echo local paths, GUIDs, observer JSON, host information or PII.
    print(json.dumps(report, sort_keys=True))
    return 1 if report["status"] == "blocked-occupied" else 2


if __name__ == "__main__":
    sys.exit(main())
