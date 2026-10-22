#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-C3: read-only triple comparison of the PRIVATE C1 observer EXE hashes.

No OS probes, subprocesses, registry/COM/TSF calls, DLL loads or writes. A
matching hash proves only equality to caller-provided bytes, NOT VM provenance.
"""
import argparse
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import stat
import sys

import meltia_t4c2_offline_bundle_review as c2

MAX_EXE_BYTES = 8 * 1024 * 1024
SHA256 = re.compile(r"[0-9a-f]{64}\Z")
OBSERVERS = {
    "profile": ("profile_exe_sha256", "MeltiaT4bProfileInventory.exe"),
    "attribute": ("attribute_exe_sha256", "MeltiaT4b6VmTsfObserver.exe"),
}


def blocked(reason="bundle-inconsistent-or-unverified"):
    return {
        "phase": "T4-C3",
        "status": "blocked-inconclusive",
        "reason_code": reason,
        "bundle_consistency": "inconsistent",
        "observer_hash_checks": {name: "unverified" for name in OBSERVERS},
        "observer_binary_bytes_match_bundle_and_reference": False,
        "reference_hashes_authenticated": False,
        "observer_binary_origin_verified": False,
        "independent_vm_provenance_verified": False,
        "bundle_origin_authenticated": False,
        "registration_authorized": False,
        "registry_writes": False,
        "native_tsf_access": False,
        "native_dll_executed": False,
        "automatic_rollback": False,
    }


def hash_regular_executable(path, expected_name):
    """Read at most 8MiB; reject symlinks, parent aliases, directories, races.

    The caller must independently preserve the exact file bytes; even a valid
    returned digest cannot establish where or when the executable was used.
    """
    target = Path(path).absolute()
    # Windows may canonicalize case/8.3/long names differently in resolve().
    # Reject actual filesystem aliases instead of comparing path spellings.
    if target.name.casefold() != expected_name.casefold() or ".." in target.parts:
        raise ValueError("invalid observer path")
    for component in (target, *target.parents):
        if (component.is_symlink() or
                (hasattr(component, "is_junction") and component.is_junction())):
            raise ValueError("observer path contains a filesystem alias")
    # lstat before opening: open("rb") on an unattended FIFO can block
    # forever, so fstat alone is too late to reject special files.
    expected = target.lstat()
    if not stat.S_ISREG(expected.st_mode) or not 0 < expected.st_size <= MAX_EXE_BYTES:
        raise ValueError("observer must be a bounded regular file")
    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0)
    if os.name == "posix":
        # A regular file can be replaced by a FIFO/symlink after lstat.
        # Refuse the read entirely on POSIX systems without these protections.
        if not hasattr(os, "O_NONBLOCK") or not hasattr(os, "O_NOFOLLOW"):
            raise ValueError("safe nonblocking observer open is unavailable")
        flags |= os.O_NONBLOCK | os.O_NOFOLLOW
    with os.fdopen(os.open(target, flags), "rb") as stream:
        before = os.fstat(stream.fileno())
        if (not stat.S_ISREG(before.st_mode) or
                not 0 < before.st_size <= MAX_EXE_BYTES or
                (expected.st_dev, expected.st_ino) !=
                (before.st_dev, before.st_ino)):
            raise ValueError("observer replaced or is not a bounded regular file")
        sha = hashlib.sha256()
        read = 0
        while True:
            data = stream.read(65536)
            if not data:
                break
            read += len(data)
            if read > MAX_EXE_BYTES:
                raise ValueError("oversized observer")
            sha.update(data)
        after = os.fstat(stream.fileno())
        # Check the original path again: a rename after the descriptor was
        # opened must not count as a successful verification.
        current = target.lstat()
        if (not stat.S_ISREG(current.st_mode) or
                read != before.st_size or read != after.st_size or
                (before.st_dev, before.st_ino, before.st_mtime_ns) !=
                (after.st_dev, after.st_ino, after.st_mtime_ns) or
                (current.st_dev, current.st_ino, current.st_size,
                 current.st_mtime_ns) !=
                (before.st_dev, before.st_ino, before.st_size,
                 before.st_mtime_ns)):
            raise ValueError("observer changed while reading")
        return sha.hexdigest()


def assess(bundle, staging_root, observer_paths, independent_expected):
    """Combine C2 collision precedence with bounded EXE byte comparisons.

    independent_expected is *supplied* out-of-band; the tool cannot attest
    whether the caller actually obtained the expected digests independently.
    """
    report = blocked()
    # Always run C2 before reporting binary status: a known identity collision
    # must survive missing/mismatched observers and malformed C1 summary data.
    try:
        previous = c2.assess_bundle(Path(bundle), Path(staging_root))
        if previous["status"] not in ("blocked-inconclusive", "blocked-occupied"):
            return report
        report["status"] = previous["status"]
        report["reason_code"] = previous["reason_code"]
        report["bundle_consistency"] = previous["bundle_consistency"]
    except (OSError, ValueError, TypeError, KeyError, UnicodeError):
        return report

    try:
        summary = c2.strict_object(Path(bundle) / "review-summary.json")
        recorded = summary["binary_manifest_checksums"]
        if not isinstance(recorded, dict) or set(recorded) != {
                "profile_exe_sha256", "attribute_exe_sha256", "t2_manifest_sha256"}:
            raise ValueError("unexpected C1 digest schema")
    except (OSError, ValueError, TypeError, KeyError, UnicodeError):
        recorded = {}

    matches = []
    for kind, (key, filename) in OBSERVERS.items():
        try:
            reference = independent_expected[kind]
            bundled = recorded[key]
            if (type(reference) is not str or not SHA256.fullmatch(reference)
                    or type(bundled) is not str or not SHA256.fullmatch(bundled)):
                raise ValueError("invalid observer digest")
            actual = hash_regular_executable(observer_paths[kind], filename)
            match = (hmac.compare_digest(actual, reference) and
                     hmac.compare_digest(actual, bundled))
            report["observer_hash_checks"][kind] = "triple-match" if match else "mismatch"
            matches.append(match)
        except (OSError, ValueError, TypeError, KeyError, UnicodeError):
            report["observer_hash_checks"][kind] = "invalid-or-unavailable"
            matches.append(False)
    complete = (all(matches) and
                report["bundle_consistency"] == "internally-consistent-unverified")
    report["observer_binary_bytes_match_bundle_and_reference"] = complete
    if report["status"] != "blocked-occupied":
        report["reason_code"] = (
            "limited-observer-bytes-match-no-provenance" if complete else
            "observer-hashes-or-bundle-inconsistent")
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", required=True, type=Path)
    parser.add_argument("--staging-root", required=True, type=Path)
    parser.add_argument("--profile-exe", required=True, type=Path)
    parser.add_argument("--attribute-exe", required=True, type=Path)
    parser.add_argument("--expected-profile-sha256", required=True)
    parser.add_argument("--expected-attribute-sha256", required=True)
    args = parser.parse_args(argv)
    try:
        result = assess(
            args.bundle, args.staging_root,
            {"profile": args.profile_exe, "attribute": args.attribute_exe},
            {"profile": args.expected_profile_sha256,
             "attribute": args.expected_attribute_sha256})
    except (OSError, ValueError, TypeError, KeyError, UnicodeError):
        result = blocked("offline-review-failed")
    # No paths, hashes, GUIDs, external provider data or exception text.
    print(json.dumps(result, sort_keys=True))
    return 1 if result["status"] == "blocked-occupied" else 2


if __name__ == "__main__":
    sys.exit(main())
