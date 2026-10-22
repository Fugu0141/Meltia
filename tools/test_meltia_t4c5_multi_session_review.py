#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""Synthetic T4-C5 multi-session tests; no OS, TSF or VM observation."""
import contextlib
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import meltia_t4c4_vm_snapshot_review as c4
import meltia_t4c5_multi_session_review as c5


class MultiSessionTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.plan_path = self.root / "private-plan.json"
        self.sessions = []
        self.records = []
        self.origin = "f" * 64
        for i, cell in enumerate(c5.CELLS):
            arch, privilege = cell.split("/")
            folder = self.root / f"session-{i+1:02d}"
            folder.mkdir()
            record_path = folder / "private-record.json"
            record = {
                "schema": "meltia-t4c4-private-record-v1",
                "origin": "operator-supplied-unverified",
                "environment": {
                    "guest_os": "windows-11", "guest_build": "10.0.26100",
                    "guest_arch": "x64", "observer_arch": arch,
                    "privilege": privilege, "visibility": "current-user-only",
                    "network_isolated": True, "snapshot_restorable": True,
                    "restore_drill_recorded": True,
                    "provider_activation_risk_acknowledged": True,
                },
                "snapshot": {
                    "pre_capture_reference_sha256": self.origin,
                    "restore_drill_reference_sha256": self.origin,
                },
                "capture": {
                    "operator_role": "designated-vm-tester",
                    "observation_utc": "2026-10-22T04:00:00Z",
                },
                "evidence_sha256": {"review-summary.json": "a" * 64},
            }
            record_path.write_text(json.dumps(record), encoding="utf-8")
            self.records.append(record)
            self.sessions.append({
                "id": f"session-{i+1:02d}",
                "record": str(record_path),
                "bundle": str(folder),
                "staging_root": str(self.root / "staging"),
                "profile_exe": str(self.root / "MeltiaT4bProfileInventory.exe"),
                "attribute_exe": str(self.root / "MeltiaT4b6VmTsfObserver.exe"),
                "expected_profile_sha256": "b" * 64,
                "expected_attribute_sha256": "c" * 64,
            })
        (self.root / "staging").mkdir()
        self.write_plan()

    @staticmethod
    def mock_report(status="blocked-inconclusive", consistent=True):
        r = c4.blocked()
        r.update(status=status,
                 private_record_consistent_unverified=consistent,
                 snapshot_reference_consistent_unverified=consistent,
                 upstream_bundle_consistency=("internally-consistent-unverified"
                                              if consistent else "inconsistent"),
                 upstream_observer_bytes_match=consistent)
        return r

    def write_plan(self):
        self.plan_path.write_text(json.dumps({
            "schema": c5.SCHEMA, "sessions": self.sessions,
        }), encoding="utf-8")

    def write_record(self, index):
        Path(self.sessions[index]["record"]).write_text(
            json.dumps(self.records[index]), encoding="utf-8")

    def review(self, mocked=None):
        with mock.patch.object(c5.c4, "assess",
                               side_effect=mocked if callable(mocked) else None,
                               return_value=None if callable(mocked) else
                               (mocked or self.mock_report())):
            return c5.assess(self.plan_path)

    def assert_denied(self, report):
        self.assertIn(report["status"], ("blocked-inconclusive", "blocked-occupied"))
        for flag in ("registration_authorized", "registry_writes", "native_tsf_access",
                     "native_dll_executed", "automatic_rollback",
                     "independent_vm_provenance_verified",
                     "snapshot_restoration_verified",
                     "cross_user_architecture_coverage_verified",
                     "operator_identity_authenticated"):
            self.assertIs(report[flag], False, flag)
        self.assertNotIn(str(self.root), str(report))
        self.assertNotIn(self.origin, str(report))

    def test_four_distinct_self_reports_are_never_registration_authority(self):
        r = self.review()
        self.assert_denied(r)
        self.assertTrue(r["claimed_matrix_consistent_unverified"])
        self.assertTrue(r["declared_guest_snapshot_consistent_unverified"])
        self.assertEqual(list(c5.CELLS), r["claimed_cells_unverified"])
        self.assertEqual([], r["missing_claimed_cells"])
        self.assertEqual("all-four-session-claims-consistent-not-authenticated",
                         r["reason_code"])

    def test_partial_matrix_never_complete(self):
        self.sessions.pop()
        self.write_plan()
        r = self.review()
        self.assert_denied(r)
        self.assertFalse(r["claimed_matrix_consistent_unverified"])
        self.assertEqual(["x86/elevated"], r["missing_claimed_cells"])

    def test_duplicate_id_record_or_bundle_cannot_double_count(self):
        for field in ("id", "record", "bundle"):
            with self.subTest(field=field):
                original = self.sessions[1][field]
                self.sessions[1][field] = self.sessions[0][field]
                self.write_plan()
                r = self.review()
                self.assert_denied(r)
                self.assertFalse(r["claimed_matrix_consistent_unverified"])
                self.assertEqual(1, r["sessions_invalid_or_unverified"])
                self.sessions[1][field] = original

    def test_broken_session_does_not_hide_other_c4_conflict(self):
        self.sessions[0]["expected_profile_sha256"] = "bad"
        self.write_plan()
        def occupied_in_second(record_path, *_args):
            return self.mock_report(
                "blocked-occupied" if Path(record_path) ==
                Path(self.sessions[1]["record"]) else "blocked-inconclusive")
        r = self.review(occupied_in_second)
        self.assert_denied(r)
        self.assertEqual("blocked-occupied", r["status"])
        self.assertFalse(r["claimed_matrix_consistent_unverified"])
        self.assertEqual("potential-identity-conflict-in-one-or-more-sessions",
                         r["reason_code"])

    def test_inconsistent_upstream_never_counts(self):
        def bad_last(record_path, *_args):
            return self.mock_report(consistent=Path(record_path) !=
                                    Path(self.sessions[-1]["record"]))
        r = self.review(bad_last)
        self.assert_denied(r)
        self.assertFalse(r["claimed_matrix_consistent_unverified"])
        self.assertEqual(3, r["sessions_reconciled_unverified"])

    def test_upstream_illegal_authorization_or_missing_contract_never_counts(self):
        for field, value in [
            ("registration_authorized", True),
            ("registry_writes", True),
            ("phase", "T4-C3"),
            ("snapshot_reference_consistent_unverified", False),
        ]:
            with self.subTest(field=field):
                previous = self.mock_report()
                previous[field] = value
                r = self.review(previous)
                self.assert_denied(r)
                self.assertFalse(r["claimed_matrix_consistent_unverified"])
                self.assertEqual(4, r["sessions_invalid_or_unverified"])
        self.assertFalse(self.review(lambda *_args: None)[
            "claimed_matrix_consistent_unverified"])

    def test_guest_build_and_snapshot_mismatch(self):
        for section, key, replacement in [
            ("environment", "guest_build", "10.0.19045"),
            ("environment", "guest_os", "windows-10"),
            ("snapshot", "pre_capture_reference_sha256", "a" * 64),
        ]:
            with self.subTest(key=key):
                previous = self.records[2][section][key]
                self.records[2][section][key] = replacement
                if key == "pre_capture_reference_sha256":
                    self.records[2]["snapshot"]["restore_drill_reference_sha256"] = replacement
                self.write_record(2)
                r = self.review()
                self.assert_denied(r)
                self.assertFalse(r["claimed_matrix_consistent_unverified"])
                self.assertEqual("claimed-vm-or-snapshot-disagreement", r["reason_code"])
                self.records[2][section][key] = previous
                if key == "pre_capture_reference_sha256":
                    self.records[2]["snapshot"]["restore_drill_reference_sha256"] = previous
                self.write_record(2)

    def test_missing_record_or_changed_mid_review_fails(self):
        record = Path(self.sessions[3]["record"])
        old = record.read_bytes()
        record.unlink()
        self.assertFalse(self.review()["claimed_matrix_consistent_unverified"])
        record.write_bytes(old)
        def modify_on_last(record_path, *_args):
            if Path(record_path) == record:
                record.write_bytes(record.read_bytes() + b" ")
            return self.mock_report()
        r = self.review(modify_on_last)
        self.assert_denied(r)
        self.assertFalse(r["claimed_matrix_consistent_unverified"])
        self.assertEqual(1, r["sessions_invalid_or_unverified"])

    def test_extra_fields_invalid_digest_and_relative_path_fail_closed(self):
        changes = [
            ("expected_attribute_sha256", "A" * 64),
            ("staging_root", "relative/path"),
            ("record", "../escape"),
        ]
        for key, value in changes:
            with self.subTest(key=key):
                old = self.sessions[0][key]
                self.sessions[0][key] = value
                self.write_plan()
                r = self.review()
                self.assert_denied(r)
                self.assertFalse(r["claimed_matrix_consistent_unverified"])
                self.sessions[0][key] = old
        self.sessions[0]["unexpected"] = True
        self.write_plan()
        self.assertFalse(self.review()["claimed_matrix_consistent_unverified"])

    def test_private_plan_checkout_alias_duplicate_keys_and_oversize_fail(self):
        checkout = self.root / "checkout"
        checkout.mkdir()
        plan_in_checkout = checkout / "private-plan.json"
        plan_in_checkout.write_bytes(self.plan_path.read_bytes())
        with mock.patch.object(c5.c4.c1, "ROOT", checkout):
            r = c5.assess(plan_in_checkout)
        self.assert_denied(r)
        self.assertEqual("private-session-plan-invalid", r["reason_code"])

        self.plan_path.write_text('{"schema":"a","schema":"b"}', encoding="utf-8")
        self.assertEqual("private-session-plan-invalid",
                         c5.assess(self.plan_path)["reason_code"])
        self.plan_path.write_bytes(b"x" * (c4.c1.MAX_BYTES + 1))
        self.assertEqual("private-session-plan-invalid",
                         c5.assess(self.plan_path)["reason_code"])

    def test_cli_nonzero_private_redaction(self):
        for occupied, expected in ((False, 2), (True, 1)):
            r = self.mock_report("blocked-occupied" if occupied else "blocked-inconclusive")
            out = io.StringIO()
            with (mock.patch.object(c5.c4, "assess", return_value=r),
                  contextlib.redirect_stdout(out)):
                code = c5.main(["--private-plan", str(self.plan_path)])
            self.assertEqual(expected, code)
            self.assert_denied(json.loads(out.getvalue()))

    def test_real_c4_c3_c2_b5_chain_offline(self):
        # No mocks: C4 -> C3 -> C2 -> B5 using synthetic C1 captures,
        # synthetic PE stage and preserved observer EXE bytes.
        import meltia_t4a_preflight as t4a
        from test_meltia_t4c2_offline_bundle_review import BundleReviewTests
        fixture = BundleReviewTests(
            methodName="test_complete_bundle_is_only_internally_consistent")
        fixture.setUp()
        try:
            summary = fixture.generate()
            observers = {}
            expected = {}
            for kind, (_, name) in c4.c3.OBSERVERS.items():
                exe = fixture.root / name
                raw = ("synthetic " + kind).encode("ascii")
                exe.write_bytes(raw)
                observers[kind] = exe
                expected[kind] = hashlib.sha256(raw).hexdigest()
                key = c4.c3.OBSERVERS[kind][0]
                summary["binary_manifest_checksums"][key] = expected[kind]
            fixture.save_summary(summary)
            private_record = fixture.root / "private-record.json"
            claim = json.loads(json.dumps(self.records[0]))
            claim["evidence_sha256"] = {
                p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                for p in fixture.bundle.iterdir()
            }
            private_record.write_text(json.dumps(claim), encoding="utf-8")
            self.sessions = [{
                **self.sessions[0], "record": str(private_record),
                "bundle": str(fixture.bundle), "staging_root": str(fixture.stage),
                "profile_exe": str(observers["profile"]),
                "attribute_exe": str(observers["attribute"]),
                "expected_profile_sha256": expected["profile"],
                "expected_attribute_sha256": expected["attribute"],
            }]
            self.write_plan()
            result = c5.assess(self.plan_path)
            self.assert_denied(result)
            self.assertEqual(1, result["sessions_reconciled_unverified"])
            self.assertFalse(result["claimed_matrix_consistent_unverified"])
            # Occupied t4a still beats an invalid extra later-stage file.
            fixture.registry = t4a.inspect(fixture.identity, lambda *_: True)
            summary = fixture.generate(codes=(1, 0, 2))
            summary["binary_manifest_checksums"].update({
                c4.c3.OBSERVERS[kind][0]: digest
                for kind, digest in expected.items()
            })
            fixture.save_summary(summary)
            (fixture.bundle / "t4b3.json").write_bytes(b"not-json")
            result = c5.assess(self.plan_path)
            self.assert_denied(result)
            self.assertEqual("blocked-occupied", result["status"])
        finally:
            fixture.doCleanups()


if __name__ == "__main__":
    unittest.main(verbosity=2)
