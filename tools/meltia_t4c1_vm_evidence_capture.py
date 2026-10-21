#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-C1 VM operator ONLY: capture bounded TSF pre-registration evidence.

The only live path requires --vm-readonly, explicit snapshot/provider consent,
Windows (not CI), and precompiled standalone observers. It NEVER registers,
unregisters, elevates, writes to registry, changes IME settings, runs TIP DLLs,
or grants permission to install. A provider lookup may activate third-party COM.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import threading

from meltia_t4a_preflight import MANIFEST, ROOT, validate_preflight_manifest
from meltia_t4b_audit_gate import validate_t4a, validate_tsf
from meltia_t4b3_evidence_gate import validate_evidence

MAX_BYTES = 131072
STAGES = ("t4a", "t4b1", "t4b3")


def _unique_pairs(pairs):
    result = {}
    for name, value in pairs:
        if name in result:
            raise ValueError("duplicate JSON key")
        result[name] = value
    return result


def read_json_bytes(content):
    if not isinstance(content, bytes) or not 0 < len(content) <= MAX_BYTES:
        raise ValueError("missing or oversized evidence")
    # Helpers are expected to write ASCII/UTF-8 JSON to captured stdout.
    parsed = json.loads(content.decode("utf-8-sig"), object_pairs_hook=_unique_pairs)
    if not isinstance(parsed, dict):
        raise ValueError("evidence top-level JSON must be an object")
    return parsed


def validate_observation(stage, report, code, manifest):
    if not isinstance(report, dict):
        raise ValueError("invalid evidence object")
    identity = validate_preflight_manifest(manifest)
    if type(code) is not int:
        raise ValueError("invalid process exit status")
    if stage == "t4a":
        state = validate_t4a(report, identity)
        occupied = report["counts"]["occupied"] > 0
        expected = 0 if state == "precheck-clear" else 1 if state == "occupied" else 2
        if expected != code:
            raise ValueError("T4-A exit and findings differ")
        return "occupied" if occupied else state
    if stage == "t4b1":
        state = validate_tsf(report, identity)
        expected = 0 if state == "enumerated-clear" else 1 if state == "occupied" else 2
        if expected != code:
            raise ValueError("T4-B1 exit and findings differ")
        return state
    if stage == "t4b3":
        findings = validate_evidence(manifest, report)
        occupied = bool(findings["existing"])
        if code != (1 if occupied else 2):
            raise ValueError("T4-B3 exit and findings differ")
        return "occupied" if occupied else "inconclusive"
    raise ValueError("unsupported evidence stage")


def capture(manifest, runner, commands):
    """Pure orchestration; runner is injected to allow CI with NO OS probes.

    Safety: if registry preflight is not fully clear, do not touch TSF COM.
    If a TSF profile is occupied/unknown, do not query external attributes.
    Invalid evidence is never promoted to absence or authorization.
    """
    validate_preflight_manifest(manifest)
    if set(commands) != set(STAGES):
        raise ValueError("all three bounded stages must be configured")
    evidence = {}
    outcomes = dict.fromkeys(STAGES, "not-run")
    status = "blocked-inconclusive"
    reason = "operator-evidence-incomplete"
    for stage in STAGES:
        try:
            code, output = runner(commands[stage])
            observed = read_json_bytes(output)
            outcome = validate_observation(stage, observed, code, manifest)
        except (ValueError, KeyError, TypeError, UnicodeError, OSError,
                subprocess.TimeoutExpired, subprocess.SubprocessError):
            outcomes[stage] = "invalid-or-unavailable"
            reason = stage + "-invalid-or-unavailable"
            break
        evidence[stage] = observed
        outcomes[stage] = outcome
        if outcome == "occupied":
            status = "blocked-occupied"
            reason = stage + "-observed-identity-conflict"
            break
        if stage == "t4a" and outcome != "precheck-clear":
            reason = "registry-not-conclusively-clear"
            break
        if stage == "t4b1" and outcome != "enumerated-clear":
            reason = "profile-not-conclusively-clear"
            break
        # T4-B3 never authorizes registration, even after all observations.
        if stage == "t4b3":
            reason = "read-only-observations-never-authorize-registration"
    return evidence, {
        "phase": "T4-C1",
        "status": status,
        "reason_code": reason,
        "stage_checks": outcomes,
        "registration_authorized": False,
        "registry_writes_requested": False,
        "automatic_rollback": False,
        "independent_vm_provenance_verified": False,
        "scope": "one-operator-claimed-vm-session-current-user-only",
        "requires_manual_review": True,
    }


def guarded_platform(platform, environ, *, vm_readonly, snapshot_confirmed,
                     provider_consent):
    """No CI/non-Windows/background or accidental live invocation."""
    return (platform == "win32" and not environ.get("CI") and
            not environ.get("GITHUB_ACTIONS") and vm_readonly and
            snapshot_confirmed and provider_consent)


def checked_observer(path, expected_name):
    candidate = Path(path).absolute()
    root = ROOT.resolve()
    if candidate.is_symlink() or not candidate.is_file() or (
            candidate.name.lower() != expected_name.lower() or
            candidate.resolve() == root or root in candidate.resolve().parents):
        raise ValueError("observer executable must be a separate, expected, existing file")
    if candidate.stat().st_size <= 0 or candidate.stat().st_size > 8 * 1024 * 1024:
        raise ValueError("unexpected observer file size")
    return candidate


def checked_output(path):
    # Path.resolve() follows existing parent symlinks and Windows junctions
    # even when the final evidence directory has not yet been created.
    output = Path(path).absolute()
    resolved = output.resolve()
    root = ROOT.resolve()
    if (output.exists() or output.is_symlink() or
            resolved == root or root in resolved.parents):
        raise ValueError("output must be a fresh directory outside the checkout")
    # Use the canonical destination rather than writing through an alias.
    return resolved


def sha256_file(path):
    checksum = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(65536), b""):
            checksum.update(block)
    return checksum.hexdigest()


def native_runner(command):
    # Drain stdout while the child is running, enforcing the limit before
    # further output is buffered. stderr is discarded (never propagated).
    # A reader thread works with Windows pipes, unlike select() on pipes.
    with subprocess.Popen(command, shell=False, stdin=subprocess.DEVNULL,
                          stdout=subprocess.PIPE, stderr=subprocess.DEVNULL) as proc:
        chunks = []
        exceeded = threading.Event()

        def drain_stdout():
            size = 0
            try:
                for chunk in iter(lambda: proc.stdout.read(8192), b""):
                    size += len(chunk)
                    if size > MAX_BYTES:
                        exceeded.set()
                        proc.kill()
                        return
                    chunks.append(chunk)
            except OSError:
                # Thread errors must not print private provider diagnostics.
                exceeded.set()
                try:
                    proc.kill()
                except OSError:
                    pass

        reader = threading.Thread(target=drain_stdout, daemon=True)
        reader.start()
        try:
            proc.wait(timeout=90)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait()
            reader.join(timeout=5)
            raise
        reader.join(timeout=5)
        if reader.is_alive() or exceeded.is_set():
            raise ValueError("observer stdout limit exceeded or unavailable")
        return proc.returncode, b"".join(chunks)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--vm-readonly", action="store_true")
    parser.add_argument("--snapshot-confirmed", action="store_true")
    parser.add_argument("--provider-activation-acknowledged", action="store_true")
    parser.add_argument("--profile-exe", type=Path, required=True)
    parser.add_argument("--attribute-exe", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)

    if not guarded_platform(sys.platform, os.environ, vm_readonly=args.vm_readonly,
                            snapshot_confirmed=args.snapshot_confirmed,
                            provider_consent=args.provider_activation_acknowledged):
        print("T4-C1 blocked: isolated Windows VM + explicit snapshot/provider acknowledgments required")
        return 2

    try:
        manifest = read_json_bytes(MANIFEST.read_bytes())
        ids = validate_preflight_manifest(manifest)
        profile = checked_observer(args.profile_exe, "MeltiaT4bProfileInventory.exe")
        attributes = checked_observer(args.attribute_exe, "MeltiaT4b6VmTsfObserver.exe")
        output = checked_output(args.output)
        binaries = {
            "profile_exe_sha256": sha256_file(profile),
            "attribute_exe_sha256": sha256_file(attributes),
            "t2_manifest_sha256": sha256_file(MANIFEST),
        }
        commands = {
            "t4a": [sys.executable, str(ROOT / "tools/meltia_t4a_preflight.py")],
            "t4b1": [str(profile), "{" + ids["clsid"] + "}",
                      "{" + ids["profileGuid"] + "}"],
            "t4b3": [str(attributes), "--vm-readonly", str(MANIFEST),
                      ids["clsid"], ids["inputAttributeGuid"],
                      ids["convertedAttributeGuid"], ids["targetAttributeGuid"]],
        }
        # Out-of-tree/private bundle. Never write observations to Git.
        output.mkdir(mode=0o700)
        if output.resolve() != output:
            raise ValueError("output destination changed during creation")
        evidence, result = capture(manifest, native_runner, commands)
        # A changed binary/manifest invalidates collected observations.
        if (binaries["profile_exe_sha256"] != sha256_file(profile) or
                binaries["attribute_exe_sha256"] != sha256_file(attributes) or
                binaries["t2_manifest_sha256"] != sha256_file(MANIFEST)):
            evidence.clear()
            result["status"] = "blocked-inconclusive"
            result["reason_code"] = "binary-or-manifest-changed-during-collection"
            result["stage_checks"] = dict.fromkeys(STAGES, "untrusted")
        evidence_hashes = {}
        for stage, entry in evidence.items():
            encoded = (json.dumps(entry, sort_keys=True, ensure_ascii=False,
                                  indent=2) + "\n").encode("utf-8")
            (output / (stage + ".json")).write_bytes(encoded)
            evidence_hashes[stage] = hashlib.sha256(encoded).hexdigest()
        # Bind each saved observation to this summary. These hashes establish
        # internal drift detection, NOT the VM's independent provenance.
        result["evidence_sha256"] = evidence_hashes
        result["binary_manifest_checksums"] = binaries
        result["actual_vm_snapshot_verified"] = False
        (output / "review-summary.json").write_text(
            json.dumps(result, sort_keys=True, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8")
        # Only an anonymized status summary is displayed; raw reports remain private.
        print(json.dumps({key: result[key] for key in (
            "phase", "status", "reason_code", "stage_checks", "registration_authorized")},
            sort_keys=True))
        return 1 if result["status"] == "blocked-occupied" else 2
    except (OSError, ValueError, TypeError, KeyError, UnicodeError,
            subprocess.SubprocessError) as exc:
        # Do not print exceptions: OS error strings may contain private paths.
        print(json.dumps({"phase": "T4-C1", "status": "blocked-inconclusive",
                          "registration_authorized": False,
                          "reason_code": "operator-preflight-or-write-failed"}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
