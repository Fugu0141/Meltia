#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-C5: offline reconciliation of PRIVATE T4-C4 VM session claims.

Checks a declared x64/x86 x standard/elevated matrix on one claimed Windows
guest/snapshot. Never probes Windows/TSF, starts observers, touches the
registry, grants registration approval, authenticates users, or writes files.
"""
import argparse
import json
from pathlib import Path
import re
import sys

import meltia_t4c1_vm_evidence_capture as c1
import meltia_t4c4_vm_snapshot_review as c4

SCHEMA = "meltia-t4c5-private-session-plan-v1"
SESSION_FIELDS = frozenset((
    "id", "record", "bundle", "staging_root", "profile_exe",
    "attribute_exe", "expected_profile_sha256",
    "expected_attribute_sha256",
))
PATH_FIELDS = ("record", "bundle", "staging_root", "profile_exe", "attribute_exe")
CELLS = tuple(f"{arch}/{privilege}" for arch in ("x64", "x86")
              for privilege in ("standard", "elevated"))
SESSION_ID = re.compile(r"session-[0-9]{2}\Z")
MAX_SESSIONS = 8


def blocked(reason="session-plan-incomplete"):
    return {
        "phase": "T4-C5",
        "status": "blocked-inconclusive",
        "reason_code": reason,
        "sessions_declared": 0,
        "sessions_reconciled_unverified": 0,
        "sessions_invalid_or_unverified": 0,
        "claimed_cells_unverified": [],
        "missing_claimed_cells": list(CELLS),
        "claimed_matrix_consistent_unverified": False,
        "declared_guest_snapshot_consistent_unverified": False,
        "independent_vm_provenance_verified": False,
        "snapshot_restoration_verified": False,
        "cross_user_architecture_coverage_verified": False,
        "operator_identity_authenticated": False,
        "registration_authorized": False,
        "registry_writes": False,
        "native_tsf_access": False,
        "native_dll_executed": False,
        "automatic_rollback": False,
    }


def load_plan(path):
    # The plan itself contains PRIVATE absolute paths and operator-controlled
    # checksums: never put it in Git, a PR, or CI artifacts.
    source = c4.private_record_outside_checkout(path)
    data = c1.read_json_bytes(c4.read_regular_bytes(source))
    if (not isinstance(data, dict) or set(data) != {"schema", "sessions"}
            or data["schema"] != SCHEMA
            or not isinstance(data["sessions"], list)
            or not 1 <= len(data["sessions"]) <= MAX_SESSIONS):
        raise ValueError("invalid private plan schema")
    return data["sessions"]


def read_session_claim(session):
    # This read is only a self-report, never evidence of a Windows VM.
    record_path = c4.private_record_outside_checkout(session["record"])
    data = c4.read_regular_bytes(record_path)
    claim = c1.read_json_bytes(data)
    if not isinstance(claim, dict):
        raise ValueError("invalid C4 record")
    return data, claim


def session_paths(session):
    """Validate inputs without prematurely losing other sessions' conflicts."""
    if not isinstance(session, dict):
        raise ValueError("invalid session")
    paths = {}
    for key in PATH_FIELDS:
        value = session.get(key)
        if (type(value) is not str or not 1 <= len(value) <= 2048
                or "\x00" in value or not Path(value).is_absolute()
                or ".." in Path(value).parts):
            raise ValueError("invalid session path")
        paths[key] = Path(value)
    return paths


def assess(plan_path):
    report = blocked()
    try:
        sessions = load_plan(plan_path)
    except (OSError, ValueError, TypeError, KeyError, UnicodeError):
        report["reason_code"] = "private-session-plan-invalid"
        return report

    report["sessions_declared"] = len(sessions)
    occupied = False
    invalid = 0
    declared_ids = set()
    used_records = set()
    used_bundles = set()
    claimed = set()
    origin_claim = None
    mismatched_origin = False

    for session in sessions:
        # Inspect all parseable sessions, even if some others were invalid.
        # A good occupied finding must not be concealed by bad metadata.
        try:
            paths = session_paths(session)
        except (OSError, ValueError, TypeError):
            invalid += 1
            continue
        observers = {"profile": paths["profile_exe"],
                     "attribute": paths["attribute_exe"]}
        expected = {"profile": session.get("expected_profile_sha256"),
                    "attribute": session.get("expected_attribute_sha256")}
        before = None
        try:
            before, claim = read_session_claim(session)
        except (OSError, ValueError, TypeError, KeyError, UnicodeError):
            claim = None

        try:
            previous = c4.assess(paths["record"], paths["bundle"],
                                 paths["staging_root"], observers, expected)
        except (OSError, ValueError, TypeError, KeyError, UnicodeError):
            previous = c4.blocked("offline-session-review-failed")

        if not isinstance(previous, dict):
            invalid += 1
            continue
        if previous.get("status") == "blocked-occupied":
            occupied = True

        # Validate the metadata *after* preserving any occupied finding.
        try:
            if set(session) != SESSION_FIELDS:
                raise ValueError("unknown session field")
            if any(type(expected[k]) is not str or not c4.SHA256.fullmatch(expected[k])
                   for k in ("profile", "attribute")):
                raise ValueError("invalid independently supplied digest")
            sid = session["id"]
            if type(sid) is not str or not SESSION_ID.fullmatch(sid):
                raise ValueError("invalid anonymous session id")
            if sid in declared_ids:
                raise ValueError("reused session id")
            declared_ids.add(sid)
            record = c4.private_record_outside_checkout(paths["record"])
            bundle = c4.private_record_outside_checkout(paths["bundle"])
            if record in used_records or bundle in used_bundles:
                raise ValueError("reused private evidence")
            used_records.add(record)
            used_bundles.add(bundle)
            forbidden = ("registration_authorized", "registry_writes",
                         "native_tsf_access", "native_dll_executed",
                         "automatic_rollback", "independent_vm_provenance_verified",
                         "operator_identity_authenticated",
                         "snapshot_restoration_verified",
                         "cross_user_architecture_coverage_verified")
            if (previous.get("phase") != "T4-C4"
                    or any(previous.get(key) is not False for key in forbidden)
                    or previous.get("snapshot_reference_consistent_unverified") is not True
                    or not previous.get("private_record_consistent_unverified") is True
                    or previous.get("upstream_bundle_consistency") !=
                    "internally-consistent-unverified"
                    or previous.get("upstream_observer_bytes_match") is not True
                    or previous.get("status") != "blocked-inconclusive"
                    or before is None or claim is None
                    or before != c4.read_regular_bytes(record)):
                raise ValueError("session evidence was not internally reconciled")
            # Metadata is read again only to confirm C4 reviewed these same
            # bytes; the self-report cannot authenticate a real VM.
            env = claim["environment"]
            snapshot = claim["snapshot"]
            cell = f"{env['observer_arch']}/{env['privilege']}"
            if cell not in CELLS:
                raise ValueError("unsupported matrix claim")
            observed_origin = (env["guest_os"], env["guest_build"],
                               snapshot["pre_capture_reference_sha256"])
            if origin_claim is None:
                origin_claim = observed_origin
            elif observed_origin != origin_claim:
                mismatched_origin = True
            claimed.add(cell)
        except (OSError, ValueError, TypeError, KeyError, UnicodeError):
            invalid += 1

    report["sessions_invalid_or_unverified"] = invalid
    report["sessions_reconciled_unverified"] = len(sessions) - invalid
    report["claimed_cells_unverified"] = [cell for cell in CELLS if cell in claimed]
    report["missing_claimed_cells"] = [cell for cell in CELLS if cell not in claimed]
    origin_consistent = bool(claimed) and not mismatched_origin and invalid == 0
    report["declared_guest_snapshot_consistent_unverified"] = origin_consistent
    complete = (origin_consistent and set(claimed) == set(CELLS)
                and len(sessions) == len(CELLS))
    report["claimed_matrix_consistent_unverified"] = complete
    if occupied:
        report["status"] = "blocked-occupied"
        report["reason_code"] = "potential-identity-conflict-in-one-or-more-sessions"
        report["claimed_matrix_consistent_unverified"] = False
        report["declared_guest_snapshot_consistent_unverified"] = False
    elif complete:
        report["reason_code"] = "all-four-session-claims-consistent-not-authenticated"
    elif mismatched_origin:
        report["reason_code"] = "claimed-vm-or-snapshot-disagreement"
    elif invalid:
        report["reason_code"] = "invalid-or-unverified-session-evidence"
    else:
        report["reason_code"] = "claimed-observer-privilege-coverage-incomplete"
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--private-plan", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        report = assess(args.private_plan)
    except (OSError, ValueError, TypeError, KeyError, UnicodeError):
        report = blocked("offline-review-failed")
    # No paths, claimed build, snapshot digest, SID, GUID or private comments.
    print(json.dumps(report, sort_keys=True))
    return 1 if report["status"] == "blocked-occupied" else 2


if __name__ == "__main__":
    sys.exit(main())
