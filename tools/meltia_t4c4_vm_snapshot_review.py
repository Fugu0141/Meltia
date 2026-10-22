#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-C4: offline review of a PRIVATE, self-reported VM snapshot/session record.

Does not create/check a VM snapshot, probe the OS, execute observers, register
an IME, load DLLs, start subprocesses or write files. A self-reported record
is never independent proof of VM provenance or a restoration drill.
"""
import argparse
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys

import meltia_t4c1_vm_evidence_capture as c1
from meltia_offline_evidence_reader import read_regular_bytes as safe_read_bytes
import meltia_t4c3_observer_hash_review as c3

SHA256 = re.compile(r"[0-9a-f]{64}\Z")
BUILD = re.compile(r"[0-9]{1,6}(?:\.[0-9]{1,6}){2,3}\Z")
UTC_TIME = re.compile(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\Z")
FILE_NAMES = ("review-summary.json", "t4a.json", "t4b1.json", "t4b3.json")


def blocked(reason="private-session-record-incomplete"):
    return {
        "phase": "T4-C4",
        "status": "blocked-inconclusive",
        "reason_code": reason,
        "upstream_bundle_consistency": "inconsistent",
        "upstream_observer_bytes_match": False,
        "private_record_consistent_unverified": False,
        "snapshot_reference_consistent_unverified": False,
        "operator_identity_authenticated": False,
        "snapshot_restoration_verified": False,
        "independent_vm_provenance_verified": False,
        "cross_user_architecture_coverage_verified": False,
        "registration_authorized": False,
        "registry_writes": False,
        "native_tsf_access": False,
        "native_dll_executed": False,
        "automatic_rollback": False,
    }


def no_aliases(path):
    p = Path(path).absolute()
    if ".." in p.parts:
        raise ValueError("relative traversal")
    for component in (p, *p.parents):
        if (component.is_symlink() or
                (hasattr(component, "is_junction") and component.is_junction())):
            raise ValueError("filesystem alias")
    return p


def bundle_members(directory):
    """Enumerate metadata without reading extra/unexpected or special files.

    This must run AFTER C3. A bad unrelated file cannot mask a verified
    identity collision from another independent stage. C2 and B5 use the
    shared nonblocking reader so unsafe paths cannot hang in their pipeline.
    """
    directory = no_aliases(directory)
    if not stat.S_ISDIR(directory.lstat().st_mode):
        raise ValueError("bundle is not an ordinary directory")
    present = set()
    clean = True
    with os.scandir(directory) as entries:
        for entry in entries:
            if entry.name not in FILE_NAMES:
                clean = False
                continue  # Never open or follow unrecognized entries.
            st = entry.stat(follow_symlinks=False)
            if not stat.S_ISREG(st.st_mode) or not 0 < st.st_size <= c1.MAX_BYTES:
                clean = False
                continue
            present.add(entry.name)
    return present, clean


def read_regular_bytes(path, limit=c1.MAX_BYTES):
    """Shared no-follow, bounded evidence reader (not atomic attestation)."""
    return safe_read_bytes(path, limit)


def private_record_outside_checkout(path):
    """Reject private operator metadata kept in the Git source tree."""
    p = no_aliases(path)
    checkout = c1.ROOT.resolve()
    if p == checkout or checkout in p.parents:
        raise ValueError("private operator record must stay outside checkout")
    return p


def check_record(record, actual_sha):
    """Validate strictly limited metadata, not the truth of operator claims."""
    if not isinstance(record, dict) or set(record) != {
            "schema", "origin", "environment", "snapshot", "capture",
            "evidence_sha256"}:
        return False
    if (record["schema"] != "meltia-t4c4-private-record-v1" or
            record["origin"] != "operator-supplied-unverified"):
        return False
    environment = record["environment"]
    snapshot = record["snapshot"]
    capture = record["capture"]
    evidence = record["evidence_sha256"]
    if (not isinstance(environment, dict) or set(environment) != {
            "guest_os", "guest_build", "guest_arch", "observer_arch",
            "privilege", "visibility", "network_isolated",
            "snapshot_restorable", "restore_drill_recorded",
            "provider_activation_risk_acknowledged"} or
            environment["guest_os"] not in ("windows-10", "windows-11") or
            type(environment["guest_build"]) is not str or
            not BUILD.fullmatch(environment["guest_build"]) or
            environment["guest_arch"] != "x64" or
            environment["observer_arch"] not in ("x64", "x86") or
            environment["privilege"] not in ("standard", "elevated") or
            environment["visibility"] != "current-user-only" or
            any(environment[k] is not True for k in (
                "network_isolated", "snapshot_restorable",
                "restore_drill_recorded", "provider_activation_risk_acknowledged"))):
        return False
    if (not isinstance(snapshot, dict) or set(snapshot) != {
            "pre_capture_reference_sha256", "restore_drill_reference_sha256"} or
            any(type(snapshot[k]) is not str or not SHA256.fullmatch(snapshot[k])
                for k in snapshot) or
            snapshot["pre_capture_reference_sha256"] !=
            snapshot["restore_drill_reference_sha256"]):
        return False
    if (not isinstance(capture, dict) or set(capture) != {
            "operator_role", "observation_utc"} or
            capture["operator_role"] != "designated-vm-tester" or
            type(capture["observation_utc"]) is not str or
            not UTC_TIME.fullmatch(capture["observation_utc"])):
        return False
    try:
        datetime.fromisoformat(capture["observation_utc"].replace("Z", "+00:00"))
    except ValueError:
        return False
    if ("review-summary.json" not in actual_sha or
            not isinstance(evidence, dict) or set(evidence) != set(actual_sha)
            or any(type(v) is not str or not SHA256.fullmatch(v)
                   for v in evidence.values())):
        return False
    return all(evidence[k] == v for k, v in actual_sha.items())


def assess(record_path, bundle, staging_root, observer_paths, expected):
    report = blocked()
    try:
        # Directory alias checks are independent of the individual stage files.
        bundle = no_aliases(bundle)
        if not stat.S_ISDIR(bundle.lstat().st_mode):
            return report
    except (OSError, ValueError, TypeError):
        return report
    # C3 must run BEFORE examining malformed/extra/special evidence members.
    # C2/B5 skip unsafe stages without losing independent occupied evidence.
    try:
        earlier = c3.assess(bundle, staging_root, observer_paths, expected)
        if (earlier["status"] not in ("blocked-inconclusive", "blocked-occupied") or
                earlier["bundle_consistency"] not in (
                    "inconsistent", "internally-consistent-unverified")):
            return report
        report["status"] = earlier["status"]
        report["reason_code"] = earlier["reason_code"]
        report["upstream_bundle_consistency"] = earlier["bundle_consistency"]
        report["upstream_observer_bytes_match"] = (
            earlier["observer_binary_bytes_match_bundle_and_reference"] is True)
    except (OSError, ValueError, TypeError, KeyError, UnicodeError):
        return report

    # A record can only add unverified context; never upgrade a C3 failure.
    try:
        names, clean = bundle_members(bundle)
        if not clean:
            raise ValueError("unexpected or unsafe bundle member")
        record_path = private_record_outside_checkout(record_path)
        raw_record = read_regular_bytes(record_path)
        record = c1.read_json_bytes(raw_record)
        actual = {}
        for filename in FILE_NAMES:
            if filename in names:
                actual[filename] = hashlib.sha256(
                    read_regular_bytes(bundle / filename)).hexdigest()
        valid = check_record(record, actual)
        if not valid:
            raise ValueError("record metadata/digest mismatch")
        report["private_record_consistent_unverified"] = True
        report["snapshot_reference_consistent_unverified"] = True
    except (OSError, ValueError, TypeError, KeyError, UnicodeError):
        if report["status"] != "blocked-occupied":
            report["reason_code"] = "private-session-record-invalid-or-mismatched"
        return report

    if report["status"] != "blocked-occupied":
        if (report["upstream_bundle_consistency"] == "internally-consistent-unverified"
                and report["upstream_observer_bytes_match"]):
            report["reason_code"] = "self-reported-vm-metadata-consistent-not-authenticated"
        else:
            report["reason_code"] = "upstream-evidence-unverified"
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--record", type=Path, required=True)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--staging-root", type=Path, required=True)
    parser.add_argument("--profile-exe", type=Path, required=True)
    parser.add_argument("--attribute-exe", type=Path, required=True)
    parser.add_argument("--expected-profile-sha256", required=True)
    parser.add_argument("--expected-attribute-sha256", required=True)
    args = parser.parse_args(argv)
    try:
        result = assess(args.record, args.bundle, args.staging_root,
                        {"profile": args.profile_exe, "attribute": args.attribute_exe},
                        {"profile": args.expected_profile_sha256,
                         "attribute": args.expected_attribute_sha256})
    except (OSError, ValueError, TypeError, KeyError, UnicodeError):
        result = blocked("offline-review-failed")
    # NEVER print guest build, paths, snapshot identifiers, GUIDs or hashes.
    print(json.dumps(result, sort_keys=True))
    return 1 if result["status"] == "blocked-occupied" else 2


if __name__ == "__main__":
    sys.exit(main())
