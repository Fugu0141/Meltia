#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-C3 regression tests: private synthetic EXEs; NO registration/OS probes."""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import stat
import sys
import types
import tempfile
import unittest
from unittest import mock

import meltia_t4c3_observer_hash_review as c3


class ObserverHashReviewTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        self.stage = self.root / "stage"
        self.stage.mkdir()
        self.observers = {}
        self.expected = {}
        recorded = {"t2_manifest_sha256": "c" * 64}
        for kind, (key, filename) in c3.OBSERVERS.items():
            path = self.root / filename
            body = ("test fixture " + kind).encode("ascii")
            path.write_bytes(body)
            self.observers[kind] = path
            self.expected[kind] = hashlib.sha256(body).hexdigest()
            recorded[key] = self.expected[kind]
        self.summary = {"binary_manifest_checksums": recorded}
        self.save_summary()
        self.c2_report = {
            "status": "blocked-inconclusive",
            "reason_code": "limited-offline-review-never-authorizes-registration",
            "bundle_consistency": "internally-consistent-unverified",
        }

    def save_summary(self):
        (self.bundle / "review-summary.json").write_text(
            json.dumps(self.summary), encoding="utf-8")

    def run_review(self):
        with mock.patch.object(c3.c2, "assess_bundle", return_value=self.c2_report):
            return c3.assess(self.bundle, self.stage, self.observers, self.expected)

    def assert_denied(self, report):
        for key in ("registration_authorized", "registry_writes", "native_tsf_access",
                    "native_dll_executed", "automatic_rollback",
                    "independent_vm_provenance_verified", "bundle_origin_authenticated",
                    "reference_hashes_authenticated", "observer_binary_origin_verified"):
            self.assertIs(report[key], False, key)
        self.assertIn(report["status"], ("blocked-occupied", "blocked-inconclusive"))
        self.assertNotIn(str(self.root), str(report))
        if isinstance(self.expected.get("profile"), str):
            self.assertNotIn(self.expected["profile"], str(report))

    def test_exact_three_way_byte_match_still_blocks_registration(self):
        r = self.run_review()
        self.assert_denied(r)
        self.assertTrue(r["observer_binary_bytes_match_bundle_and_reference"])
        self.assertEqual({"profile": "triple-match", "attribute": "triple-match"},
                         r["observer_hash_checks"])
        self.assertEqual("blocked-inconclusive", r["status"])

    def test_self_reported_summary_matches_but_external_reference_differs(self):
        self.expected["attribute"] = "0" * 64
        r = self.run_review()
        self.assert_denied(r)
        self.assertFalse(r["observer_binary_bytes_match_bundle_and_reference"])
        self.assertEqual("mismatch", r["observer_hash_checks"]["attribute"])

    def test_external_reference_matches_but_summary_is_tampered(self):
        self.summary["binary_manifest_checksums"]["profile_exe_sha256"] = "0" * 64
        self.save_summary()
        r = self.run_review()
        self.assert_denied(r)
        self.assertEqual("mismatch", r["observer_hash_checks"]["profile"])

    def test_file_change_after_capture_is_detected(self):
        self.observers["profile"].write_bytes(b"new binary")
        r = self.run_review()
        self.assert_denied(r)
        self.assertEqual("mismatch", r["observer_hash_checks"]["profile"])

    def test_occupied_overrides_malformed_or_missing_binary(self):
        self.c2_report.update(status="blocked-occupied",
                              reason_code="observed-identity-conflict")
        self.observers["profile"].unlink()
        self.summary["binary_manifest_checksums"] = None
        self.save_summary()
        r = self.run_review()
        self.assert_denied(r)
        self.assertEqual("blocked-occupied", r["status"])
        self.assertEqual("observed-identity-conflict", r["reason_code"])

    def test_c2_inconsistent_never_becomes_verified_on_matching_hashes(self):
        self.c2_report["bundle_consistency"] = "inconsistent"
        r = self.run_review()
        self.assert_denied(r)
        self.assertFalse(r["observer_binary_bytes_match_bundle_and_reference"])

    def test_unexpected_upstream_state_is_fail_closed(self):
        self.c2_report["status"] = "registration-ready"
        r = self.run_review()
        self.assert_denied(r)
        self.assertEqual("blocked-inconclusive", r["status"])
        self.assertFalse(r["observer_binary_bytes_match_bundle_and_reference"])

    def test_rejects_missing_wrong_name_directory_zero_oversize_and_alias(self):
        profile = self.observers["profile"]
        with self.assertRaises((OSError, ValueError)):
            c3.hash_regular_executable(self.root / "missing.exe", profile.name)
        with self.assertRaises(ValueError):
            c3.hash_regular_executable(profile, "different.exe")
        profile.unlink()
        profile.mkdir()
        with self.assertRaises((OSError, ValueError)):
            c3.hash_regular_executable(profile, profile.name)
        profile.rmdir()
        profile.write_bytes(b"")
        with self.assertRaises(ValueError):
            c3.hash_regular_executable(profile, profile.name)
        profile.write_bytes(b"x" * (c3.MAX_EXE_BYTES + 1))
        with self.assertRaises(ValueError):
            c3.hash_regular_executable(profile, profile.name)
        profile.write_bytes(b"safe")
        alias = self.root / "alias"
        try:
            alias.symlink_to(self.root, target_is_directory=True)
        except (OSError, NotImplementedError):
            pass
        else:
            with self.assertRaises(ValueError):
                c3.hash_regular_executable(alias / profile.name, profile.name)

    @unittest.skipUnless(os.name == "posix" and hasattr(os, "mkfifo"),
                         "FIFO tests require POSIX")
    def test_fifo_rejected_before_open_without_a_writer(self):
        profile = self.observers["profile"]
        profile.unlink()
        os.mkfifo(profile)
        # Catch a regression that blocks in open() instead of timing out CI.
        script = (
            "import sys\n"
            "sys.path.insert(0, sys.argv[1])\n"
            "import meltia_t4c3_observer_hash_review as c3\n"
            "try:\n"
            "    c3.hash_regular_executable(sys.argv[2], sys.argv[3])\n"
            "except (OSError, ValueError):\n"
            "    sys.exit(0)\n"
            "sys.exit(1)\n"
        )
        outcome = subprocess.run(
            [sys.executable, "-c", script, str(Path(__file__).resolve().parent),
             str(profile), profile.name],
            capture_output=True, text=True, timeout=3, check=False)
        self.assertEqual(0, outcome.returncode, outcome.stderr)
        # Even before the open, nonregular paths must be rejected.
        with mock.patch.object(c3.os, "open",
                               side_effect=AssertionError("opened a FIFO")):
            with self.assertRaises(ValueError):
                c3.hash_regular_executable(profile, profile.name)

    @unittest.skipUnless(os.name == "posix" and hasattr(os, "mkfifo"),
                         "FIFO races require POSIX")
    def test_file_swapped_for_fifo_between_lstat_and_open_does_not_block(self):
        profile = self.observers["profile"]
        script = (
            "import os, sys\n"
            "from pathlib import Path\n"
            "sys.path.insert(0, sys.argv[1])\n"
            "import meltia_t4c3_observer_hash_review as c3\n"
            "file = Path(sys.argv[2])\n"
            "original_open = os.open\n"
            "def swap(path, flags):\n"
            "    file.rename(file.with_suffix('.saved'))\n"
            "    os.mkfifo(file)\n"
            "    return original_open(path, flags)\n"
            "c3.os.open = swap\n"
            "try:\n"
            "    c3.hash_regular_executable(file, file.name)\n"
            "except (OSError, ValueError):\n"
            "    sys.exit(0)\n"
            "sys.exit(1)\n"
        )
        outcome = subprocess.run(
            [sys.executable, "-c", script, str(Path(__file__).resolve().parent),
             str(profile)],
            capture_output=True, text=True, timeout=3, check=False)
        self.assertEqual(0, outcome.returncode, outcome.stderr)
        self.assertTrue(stat.S_ISFIFO(profile.lstat().st_mode))

    def test_changed_fstat_size_inode_and_mtime_are_rejected(self):
        profile = self.observers["profile"]
        expected = profile.stat()
        for changed in ("st_size", "st_ino", "st_mtime_ns"):
            with self.subTest(field=changed):
                fields = ("st_mode", "st_size", "st_dev", "st_ino",
                          "st_mtime_ns")
                altered = {name: getattr(expected, name) for name in fields}
                altered[changed] += 1
                after = types.SimpleNamespace(**altered)
                with mock.patch.object(c3.os, "fstat",
                                       side_effect=[expected, after]):
                    with self.assertRaises(ValueError):
                        c3.hash_regular_executable(profile, profile.name)

    @unittest.skipUnless(os.name == "posix", "POSIX nofollow flags")
    def test_nonblocking_nofollow_flags_on_regular_files(self):
        profile = self.observers["profile"]
        real_open = os.open
        seen = []
        def capture(path, flags):
            seen.append(flags)
            return real_open(path, flags)
        with mock.patch.object(c3.os, "open", side_effect=capture):
            self.assertEqual(self.expected["profile"],
                             c3.hash_regular_executable(profile, profile.name))
        self.assertEqual(1, len(seen))
        self.assertTrue(seen[0] & os.O_NONBLOCK)
        self.assertTrue(seen[0] & os.O_NOFOLLOW)

    def test_invalid_digest_syntax_and_wrong_schema_never_pass(self):
        for bad in (None, 23, "x" * 64, "A" * 64, "a" * 63):
            with self.subTest(bad=bad):
                self.expected["profile"] = bad
                r = self.run_review()
                self.assert_denied(r)
                self.assertFalse(r["observer_binary_bytes_match_bundle_and_reference"])
        self.expected["profile"] = hashlib.sha256(b"safe").hexdigest()
        self.summary["binary_manifest_checksums"] = {
            "profile_exe_sha256": self.expected["profile"]}
        self.save_summary()
        r = self.run_review()
        self.assert_denied(r)
        self.assertFalse(r["observer_binary_bytes_match_bundle_and_reference"])

    def test_real_c2_reconciliation_keeps_untrusted_and_collision_states(self):
        # Existing C2 synthetic fixtures, not a live VM or Windows probe.
        import meltia_t4a_preflight as t4a
        import test_meltia_t4c2_offline_bundle_review as c2_fixtures
        for collision in (False, True):
            fixture = c2_fixtures.BundleReviewTests(
                methodName="test_complete_bundle_is_only_internally_consistent")
            fixture.setUp()
            try:
                if collision:
                    fixture.registry = t4a.inspect(fixture.identity, lambda *_: True)
                summary = fixture.generate(codes=(1, 0, 2) if collision else (0, 0, 2))
                for kind, (key, _) in c3.OBSERVERS.items():
                    summary["binary_manifest_checksums"][key] = self.expected[kind]
                fixture.save_summary(summary)
                result = c3.assess(fixture.bundle, fixture.stage,
                                   self.observers, self.expected)
                self.assert_denied(result)
                self.assertEqual("internally-consistent-unverified",
                                 result["bundle_consistency"])
                self.assertTrue(result["observer_binary_bytes_match_bundle_and_reference"])
                self.assertEqual("blocked-occupied" if collision else "blocked-inconclusive",
                                 result["status"])
            finally:
                fixture.doCleanups()

    def test_cli_redacts_paths_digest_and_always_exits_nonzero(self):
        args = ["--bundle", str(self.bundle), "--staging-root", str(self.stage),
                "--profile-exe", str(self.observers["profile"]),
                "--attribute-exe", str(self.observers["attribute"]),
                "--expected-profile-sha256", self.expected["profile"],
                "--expected-attribute-sha256", self.expected["attribute"]]
        for status, code in (("blocked-inconclusive", 2), ("blocked-occupied", 1)):
            self.c2_report["status"] = status
            stdout = io.StringIO()
            with mock.patch.object(c3.c2, "assess_bundle", return_value=self.c2_report), \
                    contextlib.redirect_stdout(stdout):
                actual_code = c3.main(args)
            self.assertEqual(code, actual_code)
            self.assert_denied(json.loads(stdout.getvalue()))
            self.assertNotIn(str(self.root), stdout.getvalue())
            self.assertNotIn(self.expected["attribute"], stdout.getvalue())


if __name__ == "__main__":
    unittest.main(verbosity=2)
