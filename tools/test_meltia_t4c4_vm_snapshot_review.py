#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-C4 synthetic private-record checks; never inspect real OS/VM/TSF."""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

import meltia_t4c4_vm_snapshot_review as c4


class VmSnapshotRecordTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        self.stage = self.root / "stage"
        self.stage.mkdir()
        self.record_path = self.root / "private-record.json"
        self.record = {
            "schema": "meltia-t4c4-private-record-v1",
            "origin": "operator-supplied-unverified",
            "environment": {
                "guest_os": "windows-11",
                "guest_build": "10.0.26100",
                "guest_arch": "x64",
                "observer_arch": "x64",
                "privilege": "standard",
                "visibility": "current-user-only",
                "network_isolated": True,
                "snapshot_restorable": True,
                "restore_drill_recorded": True,
                "provider_activation_risk_acknowledged": True,
            },
            "snapshot": {
                "pre_capture_reference_sha256": "a" * 64,
                "restore_drill_reference_sha256": "a" * 64,
            },
            "capture": {
                "operator_role": "designated-vm-tester",
                "observation_utc": "2026-10-22T04:00:00Z",
            },
            "evidence_sha256": {},
        }
        for filename, payload in [
                ("review-summary.json", b'{"phase":"T4-C1"}'),
                ("t4a.json", b'{"phase":"T4-A"}')]:
            (self.bundle / filename).write_bytes(payload)
            self.record["evidence_sha256"][filename] = hashlib.sha256(payload).hexdigest()
        self.earlier = {
            "status": "blocked-inconclusive",
            "reason_code": "limited-observer-bytes-match-no-provenance",
            "bundle_consistency": "internally-consistent-unverified",
            "observer_binary_bytes_match_bundle_and_reference": True,
        }
        self.write_record()

    def write_record(self):
        self.record_path.write_text(json.dumps(self.record), encoding="utf-8")

    def assess(self):
        with mock.patch.object(c4.c3, "assess", return_value=self.earlier):
            return c4.assess(self.record_path, self.bundle, self.stage, {}, {})

    def assert_never_authorized(self, result):
        self.assertIn(result["status"], ("blocked-inconclusive", "blocked-occupied"))
        for key in ("registration_authorized", "registry_writes", "native_tsf_access",
                    "native_dll_executed", "automatic_rollback",
                    "independent_vm_provenance_verified",
                    "snapshot_restoration_verified", "operator_identity_authenticated",
                    "cross_user_architecture_coverage_verified"):
            self.assertIs(result[key], False, key)
        self.assertNotIn(str(self.root), str(result))
        self.assertNotIn("a" * 64, str(result))

    def test_complete_claims_are_only_consistent_unverified(self):
        r = self.assess()
        self.assert_never_authorized(r)
        self.assertTrue(r["private_record_consistent_unverified"])
        self.assertTrue(r["snapshot_reference_consistent_unverified"])
        self.assertEqual("self-reported-vm-metadata-consistent-not-authenticated",
                         r["reason_code"])

    def test_occupied_c3_result_overrides_bad_record(self):
        self.earlier["status"] = "blocked-occupied"
        self.earlier["reason_code"] = "observed-identity-conflict"
        self.record_path.unlink()
        r = self.assess()
        self.assert_never_authorized(r)
        self.assertEqual("blocked-occupied", r["status"])
        self.assertEqual("observed-identity-conflict", r["reason_code"])

    def test_inconsistent_upstream_not_promoted_by_complete_record(self):
        self.earlier["bundle_consistency"] = "inconsistent"
        self.earlier["observer_binary_bytes_match_bundle_and_reference"] = False
        r = self.assess()
        self.assert_never_authorized(r)
        self.assertTrue(r["private_record_consistent_unverified"])
        self.assertEqual("upstream-evidence-unverified", r["reason_code"])

    def test_unknown_upstream_status_is_rejected(self):
        self.earlier["status"] = "registration-ready"
        r = self.assess()
        self.assert_never_authorized(r)
        self.assertFalse(r["private_record_consistent_unverified"])

    def test_evidence_drift_or_missing_file_fails(self):
        (self.bundle / "t4a.json").write_bytes(b'{"phase":"changed"}')
        r = self.assess()
        self.assert_never_authorized(r)
        self.assertFalse(r["private_record_consistent_unverified"])
        (self.bundle / "t4a.json").unlink()
        r = self.assess()
        self.assertFalse(r["private_record_consistent_unverified"])

    def test_missing_summary_cannot_count_as_valid_record(self):
        (self.bundle / "review-summary.json").unlink()
        self.record["evidence_sha256"].pop("review-summary.json")
        self.write_record()
        r = self.assess()
        self.assert_never_authorized(r)
        self.assertFalse(r["private_record_consistent_unverified"])

    def test_extra_unlisted_evidence_fails(self):
        (self.bundle / "t4b1.json").write_bytes(b"{}")
        r = self.assess()
        self.assert_never_authorized(r)
        self.assertFalse(r["private_record_consistent_unverified"])

    def test_unexpected_bundle_filename_and_directory_are_rejected(self):
        for name, as_directory in (("t4b2.json", False), ("another-stage.json", False),
                                   ("unexpected-subdirectory", True)):
            with self.subTest(name=name):
                path = self.bundle / name
                if as_directory:
                    path.mkdir()
                else:
                    path.write_bytes(b"{}")
                result = self.assess()
                self.assert_never_authorized(result)
                self.assertFalse(result["private_record_consistent_unverified"])
                if as_directory:
                    path.rmdir()
                else:
                    path.unlink()

    def test_private_record_inside_checkout_is_rejected(self):
        checkout = self.root / "checkout"
        checkout.mkdir()
        private = checkout / "private-record.json"
        private.write_bytes(self.record_path.read_bytes())
        with (mock.patch.object(c4.c1, "ROOT", checkout),
              mock.patch.object(c4.c3, "assess", return_value=self.earlier)):
            r = c4.assess(private, self.bundle, self.stage, {}, {})
        self.assert_never_authorized(r)
        self.assertFalse(r["private_record_consistent_unverified"])

    def test_real_c2_c3_collision_survives_invalid_unrelated_stage(self):
        # Genuine C2/B5/C3 code, no mock. This exercises the P1 regression:
        # valid occupied T4-A remains occupied when a later stage is corrupt.
        import meltia_t4a_preflight as t4a
        from test_meltia_t4c2_offline_bundle_review import BundleReviewTests
        for kind in ("malformed", "fifo"):
            if kind == "fifo" and (os.name != "posix" or
                                   not hasattr(os, "mkfifo")):
                continue
            with self.subTest(kind=kind):
                fixture = BundleReviewTests(
                    methodName="test_complete_bundle_is_only_internally_consistent")
                fixture.setUp()
                try:
                    fixture.registry = t4a.inspect(fixture.identity, lambda *_: True)
                    fixture.generate(codes=(1, 0, 2))
                    unrelated = fixture.bundle / "t4b3.json"
                    if kind == "fifo":
                        os.mkfifo(unrelated)
                    else:
                        unrelated.write_bytes(b"not-json")
                    result = c4.assess(self.record_path, fixture.bundle,
                                       fixture.stage, {}, {})
                    self.assert_never_authorized(result)
                    self.assertEqual("blocked-occupied", result["status"])
                    self.assertFalse(result["private_record_consistent_unverified"])
                finally:
                    fixture.doCleanups()

    @unittest.skipUnless(os.name == "posix" and hasattr(os, "mkfifo"),
                         "POSIX nonblocking open-time race regression")
    def test_real_c2_open_time_fifo_swap_does_not_hang_or_hide_collision(self):
        # Run inside a subprocess: a regression to blocking open cannot hang CI.
        script = """
import os, sys
from pathlib import Path
sys.path.insert(0, sys.argv[1])
import meltia_t4a_preflight as t4a
import meltia_t4c4_vm_snapshot_review as c4
import meltia_offline_evidence_reader as reader
from test_meltia_t4c2_offline_bundle_review import BundleReviewTests
fixture = BundleReviewTests(methodName='test_complete_bundle_is_only_internally_consistent')
fixture.setUp()
try:
    fixture.registry = t4a.inspect(fixture.identity, lambda *_: True)
    fixture.generate(codes=(1, 0, 2))
    target = fixture.bundle / 't4b3.json'
    target.write_bytes(b'{}')
    previous = reader.os.open
    swap_done = [False]
    def swap(path, flags):
        if Path(path) == target and not swap_done[0]:
            swap_done[0] = True
            target.unlink()
            os.mkfifo(target)
        return previous(path, flags)
    reader.os.open = swap
    report = c4.assess(fixture.root / 'missing-record.json',
                       fixture.bundle, fixture.stage, {}, {})
    assert swap_done[0], 'open-time swap was not exercised'
    assert report['status'] == 'blocked-occupied', report
    assert report['registration_authorized'] is False
finally:
    fixture.doCleanups()
"""
        result = subprocess.run(
            [sys.executable, "-c", script, str(Path(__file__).resolve().parent)],
            capture_output=True, text=True, timeout=5, check=False)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_snapshot_reference_mismatch_and_missing_restore_ack_fail(self):
        self.record["snapshot"]["restore_drill_reference_sha256"] = "b" * 64
        self.write_record()
        self.assertFalse(self.assess()["private_record_consistent_unverified"])
        self.record["snapshot"]["restore_drill_reference_sha256"] = "a" * 64
        self.record["environment"]["restore_drill_recorded"] = False
        self.write_record()
        self.assertFalse(self.assess()["private_record_consistent_unverified"])

    def test_invalid_roles_platform_and_scope_fail(self):
        for field, value in [
                ("guest_build", "10.0.invalid"), ("guest_arch", "arm64"),
                ("observer_arch", "other"), ("privilege", "system"),
                ("visibility", "all-users"), ("network_isolated", 1),
                ("provider_activation_risk_acknowledged", False)]:
            with self.subTest(field=field):
                original = self.record["environment"][field]
                self.record["environment"][field] = value
                self.write_record()
                self.assertFalse(self.assess()["private_record_consistent_unverified"])
                self.record["environment"][field] = original

    def test_invalid_time_role_unknown_fields_and_bad_digest_fail(self):
        mutations = [
            ("capture", "observation_utc", "2026-02-31T01:01:01Z"),
            ("capture", "operator_role", "unknown"),
            ("evidence_sha256", "t4a.json", "A" * 64),
        ]
        for part, name, value in mutations:
            with self.subTest(name=name):
                original = self.record[part][name]
                self.record[part][name] = value
                self.write_record()
                self.assertFalse(self.assess()["private_record_consistent_unverified"])
                self.record[part][name] = original
        self.record["operator_name"] = "do-not-collect-personal-data"
        self.write_record()
        self.assertFalse(self.assess()["private_record_consistent_unverified"])

    def test_duplicate_keys_and_oversized_record_fail(self):
        self.record_path.write_bytes(
            b'{"schema":"a","schema":"b"}')
        self.assertFalse(self.assess()["private_record_consistent_unverified"])
        self.record_path.write_bytes(b" " * (c4.c1.MAX_BYTES + 1))
        self.assertFalse(self.assess()["private_record_consistent_unverified"])

    def test_symlink_and_directory_refused_without_read(self):
        self.record_path.unlink()
        self.record_path.mkdir()
        self.assertFalse(self.assess()["private_record_consistent_unverified"])
        self.record_path.rmdir()
        try:
            self.record_path.symlink_to(self.bundle / "t4a.json")
        except (OSError, NotImplementedError):
            self.skipTest("symlinks unavailable")
        else:
            self.assertFalse(self.assess()["private_record_consistent_unverified"])

    @unittest.skipUnless(os.name == "posix" and hasattr(os, "mkfifo"),
                         "nonblocking FIFO regression on POSIX")
    def test_fifo_record_and_bundle_refused_before_open(self):
        self.record_path.unlink()
        os.mkfifo(self.record_path)
        script = ("import sys\n"
                  "sys.path.insert(0, sys.argv[1])\n"
                  "import meltia_t4c4_vm_snapshot_review as c4\n"
                  "try:\n"
                  "    c4.read_regular_bytes(sys.argv[2])\n"
                  "except (OSError, ValueError):\n"
                  "    sys.exit(0)\n"
                  "sys.exit(1)\n")
        result = subprocess.run(
            [sys.executable, "-c", script, str(Path(__file__).resolve().parent),
             str(self.record_path)], capture_output=True, timeout=3, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        (self.bundle / "t4a.json").unlink()
        os.mkfifo(self.bundle / "t4a.json")
        # C3 MUST run even if an unrelated stage is unsafe: it might find
        # independent evidence of an identity collision elsewhere.
        with mock.patch.object(c4.c3, "assess", return_value=self.earlier) as called:
            r = c4.assess(self.record_path, self.bundle, self.stage, {}, {})
        called.assert_called_once()
        self.assert_never_authorized(r)
        self.assertFalse(r["private_record_consistent_unverified"])

    def test_cli_exits_two_or_one_and_never_exposes_private_values(self):
        args = ["--record", str(self.record_path),
                "--bundle", str(self.bundle), "--staging-root", str(self.stage),
                "--profile-exe", "private-profile.exe",
                "--attribute-exe", "private-attribute.exe",
                "--expected-profile-sha256", "c" * 64,
                "--expected-attribute-sha256", "d" * 64]
        for state, code in (("blocked-inconclusive", 2), ("blocked-occupied", 1)):
            self.earlier["status"] = state
            out = io.StringIO()
            with mock.patch.object(c4.c3, "assess", return_value=self.earlier), \
                    contextlib.redirect_stdout(out):
                rc = c4.main(args)
            self.assertEqual(code, rc)
            self.assert_never_authorized(json.loads(out.getvalue()))
            self.assertNotIn(str(self.root), out.getvalue())
            self.assertNotIn("c" * 64, out.getvalue())


if __name__ == "__main__":
    unittest.main(verbosity=2)
