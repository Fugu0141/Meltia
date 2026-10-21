#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-C2: synthetic private evidence only; no Windows/TSF/registry calls."""
import contextlib
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import meltia_t4a_preflight as t4a
import meltia_t4b3_evidence_gate as b3
import meltia_t4c1_vm_evidence_capture as c1
import meltia_t4c2_offline_bundle_review as c2
from test_meltia_t4b4_native_artifact_gate import synthetic_pe


class BundleReviewTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.bundle = self.root / "private-evidence"
        self.bundle.mkdir()
        self.manifest = json.loads(t4a.MANIFEST.read_text(encoding="utf-8"))
        self.identity = t4a.validate_preflight_manifest(self.manifest)
        self.registry = t4a.inspect(self.identity, lambda *_: False)
        self.profile = {
            "phase": "T4-B1", "coverage": "current-user-visible-tsf-profiles",
            "registry_writes": False, "registration_authorized": False,
            "expected_clsid": self.identity["clsid"],
            "expected_profile_guid": self.identity["profileGuid"],
            "complete": True, "status": "enumerated-clear",
            "profile_count": 0, "clsid_matches": 0, "profile_guid_matches": 0,
        }
        targets = b3.expected_targets(self.manifest)
        self.attributes = {
            "phase": "T4-B3", "coverage": "current-user-visible-tsf-only",
            "registry_writes": False, "registration_authorized": False,
            "attributes": [
                {**entry, "lookup": "not-found", "owner_clsid": None}
                for entry in targets["attributes"]
            ],
            "categories": [
                {"category": category, "item_clsid": targets["clsid"],
                 "complete": True, "present": False}
                for category in targets["categories"]
            ],
        }
        self.stage = self.root / "synthetic-stage"
        self.stage.mkdir()
        (self.stage / "STAGING_NOTICE.txt").write_text(
            "T3a; NOT INSTALLABLE; SELFREG_E_CLASS\n", encoding="utf-8")
        for arch, machine, magic in (("x64", 0x8664, 0x20b),
                                     ("x86", 0x14c, 0x10b)):
            dll = self.stage / "bin" / arch / "MeltiaTip.dll"
            dll.parent.mkdir(parents=True)
            dll.write_bytes(synthetic_pe(machine, magic))

    def generate(self, codes=(0, 0, 2)):
        records = dict(zip(c2.STAGES, [
            (code, json.dumps(entry).encode("utf-8"))
            for code, entry in zip(codes, (
                self.registry, self.profile, self.attributes))
        ]))
        evidence, summary = c1.capture(
            self.manifest, lambda stage: records[stage],
            {stage: stage for stage in c2.STAGES})
        hashes = {}
        for stage, value in evidence.items():
            body = (json.dumps(value, sort_keys=True, indent=2,
                               ensure_ascii=False) + "\n").encode("utf-8")
            (self.bundle / (stage + ".json")).write_bytes(body)
            hashes[stage] = hashlib.sha256(body).hexdigest()
        summary.update(
            evidence_sha256=hashes,
            binary_manifest_checksums={
                "profile_exe_sha256": "a" * 64,
                "attribute_exe_sha256": "b" * 64,
                "t2_manifest_sha256": c1.sha256_file(t4a.MANIFEST),
            },
            actual_vm_snapshot_verified=False,
        )
        self.save_summary(summary)
        return summary

    def save_summary(self, summary):
        (self.bundle / "review-summary.json").write_text(
            json.dumps(summary), encoding="utf-8")

    def review(self):
        return c2.assess_bundle(self.bundle, self.stage)

    def assert_blocked(self, report):
        self.assertIs(report["registration_authorized"], False)
        self.assertIs(report["registry_writes"], False)
        self.assertIs(report["native_tsf_access"], False)
        self.assertIs(report["native_dll_executed"], False)
        self.assertIs(report["automatic_rollback"], False)
        self.assertIs(report["independent_vm_provenance_verified"], False)
        self.assertIs(report["bundle_origin_authenticated"], False)

    def test_complete_bundle_is_only_internally_consistent(self):
        self.generate()
        r = self.review()
        self.assert_blocked(r)
        self.assertEqual("blocked-inconclusive", r["status"])
        self.assertEqual("internally-consistent-unverified", r["bundle_consistency"])
        self.assertEqual("structural-audit-only", r["staged_native_artifacts"])

    def test_occupied_t4a_preserved_when_later_stages_not_run(self):
        self.registry = t4a.inspect(self.identity, lambda *_: True)
        self.generate(codes=(1, 0, 2))
        r = self.review()
        self.assert_blocked(r)
        self.assertEqual("blocked-occupied", r["status"])
        self.assertEqual("internally-consistent-unverified", r["bundle_consistency"])

    def test_occupied_t4b1_preserved_without_t4b3(self):
        self.profile.update(profile_count=1, clsid_matches=1, status="occupied")
        self.generate(codes=(0, 1, 2))
        r = self.review()
        self.assertEqual("blocked-occupied", r["status"])
        self.assertEqual("invalid-evidence", r["checks"]["t4b3"])

    def test_occupied_t4b3_preserved(self):
        self.attributes["categories"][0]["present"] = True
        self.generate(codes=(0, 0, 1))
        self.assertEqual("blocked-occupied", self.review()["status"])

    def test_changed_observation_digest_is_rejected(self):
        self.generate()
        path = self.bundle / "t4a.json"
        path.write_bytes(path.read_bytes() + b" ")
        r = self.review()
        self.assert_blocked(r)
        self.assertEqual("inconsistent", r["bundle_consistency"])

    def test_hash_mismatch_does_not_mask_possible_collision(self):
        self.registry = t4a.inspect(self.identity, lambda *_: True)
        self.generate(codes=(1, 0, 2))
        path = self.bundle / "t4a.json"
        path.write_bytes(path.read_bytes() + b" ")
        r = self.review()
        self.assertEqual("blocked-occupied", r["status"])
        self.assertEqual("potential-identity-conflict-in-unverified-bundle",
                         r["reason_code"])

    def test_missing_summary_preserves_independent_occupied_finding(self):
        self.registry = t4a.inspect(self.identity, lambda *_: True)
        self.generate(codes=(1, 0, 2))
        (self.bundle / "review-summary.json").unlink()
        r = self.review()
        self.assertEqual("blocked-occupied", r["status"])
        self.assertEqual("inconsistent", r["bundle_consistency"])

    def test_invalid_duplicate_key_summary_preserves_collision(self):
        self.registry = t4a.inspect(self.identity, lambda *_: True)
        self.generate(codes=(1, 0, 2))
        (self.bundle / "review-summary.json").write_text(
            '{"phase":"T4-C1","phase":"T4-C2"}', encoding="utf-8")
        self.assertEqual("blocked-occupied", self.review()["status"])

    def test_lost_b1_when_summary_expects_b1_is_inconsistent(self):
        self.generate()
        (self.bundle / "t4b1.json").unlink()
        self.assertEqual("inconsistent", self.review()["bundle_consistency"])

    def test_summary_forged_authorization_rejected(self):
        summary = self.generate()
        summary["registration_authorized"] = True
        self.save_summary(summary)
        r = self.review()
        self.assertEqual("inconsistent", r["bundle_consistency"])
        self.assert_blocked(r)

    def test_manifest_digest_mismatch_rejected(self):
        summary = self.generate()
        summary["binary_manifest_checksums"]["t2_manifest_sha256"] = "0" * 64
        self.save_summary(summary)
        self.assertEqual("inconsistent", self.review()["bundle_consistency"])

    def test_absent_digest_map_rejected_as_legacy_bundle(self):
        summary = self.generate()
        del summary["evidence_sha256"]
        self.save_summary(summary)
        self.assertEqual("inconsistent", self.review()["bundle_consistency"])

    def test_stage_order_or_exit_mismatch_rejected(self):
        summary = self.generate()
        summary["stage_checks"]["t4b1"] = "occupied"
        self.save_summary(summary)
        self.assertEqual("inconsistent", self.review()["bundle_consistency"])

    def test_added_later_stage_after_occupied_is_inconsistent(self):
        self.registry = t4a.inspect(self.identity, lambda *_: True)
        summary = self.generate(codes=(1, 0, 2))
        body = json.dumps(self.profile).encode("utf-8")
        (self.bundle / "t4b1.json").write_bytes(body)
        summary["evidence_sha256"]["t4b1"] = hashlib.sha256(body).hexdigest()
        self.save_summary(summary)
        self.assertEqual("inconsistent", self.review()["bundle_consistency"])
        self.assertEqual("blocked-occupied", self.review()["status"])

    def test_invalid_stage_is_not_treated_as_a_clear_observation(self):
        self.generate()
        (self.bundle / "t4a.json").write_text("[]", encoding="utf-8")
        self.assertEqual("inconsistent", self.review()["bundle_consistency"])

    def test_symlink_observation_ignored_but_other_conflict_retained(self):
        self.attributes["categories"][0]["present"] = True
        self.generate(codes=(0, 0, 1))
        target = self.bundle / "t4a.json"
        target.unlink()
        outside = self.root / "outside.json"
        outside.write_text("{}", encoding="utf-8")
        try:
            target.symlink_to(outside)
        except (NotImplementedError, OSError):
            self.skipTest("symlink unavailable")
        r = self.review()
        self.assertEqual("blocked-occupied", r["status"])
        self.assertEqual("inconsistent", r["bundle_consistency"])

    def test_missing_stage_artifact_is_not_authorized(self):
        self.generate()
        (self.stage / "bin/x86/MeltiaTip.dll").unlink()
        r = self.review()
        self.assert_blocked(r)
        self.assertEqual("inconclusive", r["staged_native_artifacts"])

    def test_cli_always_nonzero_and_no_private_paths(self):
        self.generate()
        stream = io.StringIO()
        with contextlib.redirect_stdout(stream):
            code = c2.main(["--bundle", str(self.bundle),
                            "--staging-root", str(self.stage)])
        self.assertEqual(2, code)
        self.assertNotIn(str(self.root), stream.getvalue())
        self.assert_blocked(json.loads(stream.getvalue()))

    def test_cli_collision_exit_one(self):
        self.registry = t4a.inspect(self.identity, lambda *_: True)
        self.generate(codes=(1, 0, 2))
        with contextlib.redirect_stdout(io.StringIO()):
            code = c2.main(["--bundle", str(self.bundle),
                            "--staging-root", str(self.stage)])
        self.assertEqual(1, code)

    def test_c1_cli_writes_matching_digest_for_each_saved_stage(self):
        records = dict(zip(c2.STAGES, [
            (code, json.dumps(entry).encode("utf-8"))
            for code, entry in zip((0, 0, 2), (
                self.registry, self.profile, self.attributes))
        ]))
        seen = []
        def fake_runner(command):
            stage = c2.STAGES[len(seen)]
            seen.append(stage)
            return records[stage]
        with (mock.patch.object(c1, "guarded_platform", return_value=True),
              mock.patch.object(c1, "checked_observer", return_value=Path(__file__)),
              mock.patch.object(c1, "checked_output",
                                return_value=(self.root / "c1-written").resolve()),
              mock.patch.object(c1, "sha256_file", return_value="a" * 64),
              mock.patch.object(c1, "native_runner", side_effect=fake_runner)):
            with contextlib.redirect_stdout(io.StringIO()):
                code = c1.main(["--profile-exe", "synthetic-profile",
                                "--attribute-exe", "synthetic-attribute",
                                "--output", str(self.root / "c1-written")])
        self.assertEqual(2, code)
        self.assertEqual(list(c2.STAGES), seen)
        folder = self.root / "c1-written"
        summary = json.loads((folder / "review-summary.json").read_text(encoding="utf-8"))
        for stage in c2.STAGES:
            self.assertEqual(hashlib.sha256((folder / (stage + ".json")).read_bytes()).hexdigest(),
                             summary["evidence_sha256"][stage])
        self.assertIs(summary["registration_authorized"], False)


if __name__ == "__main__":
    unittest.main(verbosity=2)
