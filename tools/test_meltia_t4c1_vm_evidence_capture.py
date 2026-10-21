#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-C1 tests: no TSF/registry calls; temp dirs and harmless child commands only."""
import contextlib
import copy
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

import meltia_t4c1_vm_evidence_capture as cli

from meltia_t4a_preflight import MANIFEST, inspect, validate_preflight_manifest
from meltia_t4b3_evidence_gate import expected_targets
from meltia_t4c1_vm_evidence_capture import (
    capture, checked_output, guarded_platform, native_runner,
    read_json_bytes, validate_observation,
)


class T4C1Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        cls.identity = validate_preflight_manifest(cls.manifest)

    def registry(self, state):
        return inspect(self.identity, lambda hive, view, key: state)

    def profile(self, *, occupied=False, complete=True):
        return {
            "phase": "T4-B1",
            "coverage": "current-user-visible-tsf-profiles",
            "registry_writes": False, "registration_authorized": False,
            "expected_clsid": self.identity["clsid"],
            "expected_profile_guid": self.identity["profileGuid"],
            "complete": complete, "profile_count": 1,
            "clsid_matches": 1 if occupied else 0,
            "profile_guid_matches": 0,
            "status": "inconclusive" if not complete else
                      "occupied" if occupied else "enumerated-clear",
        }

    def attributes(self, *, occupied=False):
        targets = expected_targets(self.manifest)
        attrs = [{
            "field": item["field"], "guid": item["guid"],
            "lookup": "not-found", "owner_clsid": None,
        } for item in targets["attributes"]]
        if occupied:
            attrs[0]["lookup"] = "resolved"
            attrs[0]["owner_clsid"] = "417d801b-a9bd-4c26-bd16-356a825a6998"
        return {
            "phase": "T4-B3", "coverage": "current-user-visible-tsf-only",
            "registry_writes": False, "registration_authorized": False,
            "attributes": attrs,
            "categories": [{
                "category": category, "item_clsid": self.identity["clsid"],
                "complete": True, "present": False,
            } for category in targets["categories"]],
        }

    def cases(self, *, registry=None, profile=None, attribute=None,
              codes=(0, 0, 2)):
        samples = (
            registry or self.registry(False),
            profile or self.profile(),
            attribute or self.attributes(),
        )
        return dict(zip(("t4a", "t4b1", "t4b3"), [
            (code, json.dumps(item).encode("utf-8"))
            for code, item in zip(codes, samples)
        ]))

    def run_case(self, samples):
        seen = []
        def runner(step):
            seen.append(step)
            return samples[step]
        evidence, status = capture(self.manifest, runner, {name: name for name in ("t4a", "t4b1", "t4b3")})
        self.assertFalse(status["registration_authorized"])
        self.assertFalse(status["independent_vm_provenance_verified"])
        return seen, evidence, status

    def test_all_three_clearish_still_blocked(self):
        seen, evidence, result = self.run_case(self.cases())
        self.assertEqual(["t4a", "t4b1", "t4b3"], seen)
        self.assertEqual(3, len(evidence))
        self.assertEqual("blocked-inconclusive", result["status"])
        self.assertEqual("inconclusive", result["stage_checks"]["t4b3"])
        self.assertEqual("read-only-observations-never-authorize-registration",
                         result["reason_code"])

    def test_registry_occupied_aborts_before_any_tsf_com(self):
        seen, evidence, result = self.run_case(
            self.cases(registry=self.registry(True), codes=(1, 0, 2)))
        self.assertEqual(["t4a"], seen)
        self.assertEqual("blocked-occupied", result["status"])
        self.assertEqual("not-run", result["stage_checks"]["t4b1"])
        self.assertNotIn("t4b3", evidence)

    def test_registry_unknown_aborts(self):
        seen, evidence, result = self.run_case(
            self.cases(registry=self.registry(None), codes=(2, 0, 2)))
        self.assertEqual(["t4a"], seen)
        self.assertEqual("blocked-inconclusive", result["status"])
        self.assertEqual("registry-not-conclusively-clear", result["reason_code"])

    def test_profile_occupied_does_not_activate_display_provider(self):
        seen, _, result = self.run_case(self.cases(
            profile=self.profile(occupied=True), codes=(0, 1, 2)))
        self.assertEqual(["t4a", "t4b1"], seen)
        self.assertEqual("blocked-occupied", result["status"])

    def test_profile_inconclusive_aborts_before_b3(self):
        seen, _, result = self.run_case(self.cases(
            profile=self.profile(complete=False), codes=(0, 2, 2)))
        self.assertEqual(["t4a", "t4b1"], seen)
        self.assertEqual("blocked-inconclusive", result["status"])

    def test_attribute_conflict_is_blocked(self):
        seen, _, result = self.run_case(self.cases(
            attribute=self.attributes(occupied=True), codes=(0, 0, 1)))
        self.assertEqual(3, len(seen))
        self.assertEqual("blocked-occupied", result["status"])

    def test_mismatched_exit_is_invalid_and_stops(self):
        seen, _, result = self.run_case(self.cases(codes=(2, 0, 2)))
        self.assertEqual(["t4a"], seen)
        self.assertEqual("t4a-invalid-or-unavailable", result["reason_code"])

    def test_invalid_b1_does_not_query_external_provider(self):
        bad = self.profile()
        bad["profile_guid_matches"] = 10
        seen, _, result = self.run_case(
            self.cases(profile=bad))
        self.assertEqual(["t4a", "t4b1"], seen)
        self.assertEqual("invalid-or-unavailable", result["stage_checks"]["t4b1"])

    def test_invalid_b3_exit_rejected(self):
        seen, _, result = self.run_case(self.cases(codes=(0, 0, 0)))
        self.assertEqual(3, len(seen))
        self.assertEqual("t4b3-invalid-or-unavailable", result["reason_code"])

    def test_runner_failure_remains_blocked(self):
        def runner(_):
            raise OSError("private path must not be exposed")
        _, result = capture(self.manifest, runner,
                            {name: name for name in ("t4a", "t4b1", "t4b3")})
        self.assertEqual("blocked-inconclusive", result["status"])
        self.assertNotIn("private path", str(result))

    def test_invalid_manifest_blocks_before_runner(self):
        broken = copy.deepcopy(self.manifest)
        broken["deployment"]["performRegistration"] = True
        called = []
        def runner(x):
            called.append(x)
            return (2, b"{}")
        with self.assertRaises(ValueError):
            capture(broken, runner,
                    {name: name for name in ("t4a", "t4b1", "t4b3")})
        self.assertFalse(called)

    def test_json_evidence_bounds_and_duplicates(self):
        for value in (b"", b" " * 131073, b'{"a":0,"a":1}', b"not json"):
            with self.subTest(value=value[:12]), self.assertRaises(ValueError):
                read_json_bytes(value)


    def test_non_object_json_stops_at_exact_stage_without_invoking_next(self):
        for stage_index, stage in enumerate(("t4a", "t4b1", "t4b3")):
            for raw in (b"[]", b"null", b'"oops"'):
                with self.subTest(stage=stage, raw=raw):
                    cases = self.cases()
                    cases[stage] = (0, raw)
                    seen, evidence, result = self.run_case(cases)
                    self.assertEqual(list(("t4a", "t4b1", "t4b3")[:stage_index + 1]), seen)
                    self.assertNotIn(stage, evidence)
                    self.assertEqual("blocked-inconclusive", result["status"])
                    self.assertEqual(stage + "-invalid-or-unavailable", result["reason_code"])
                    self.assertEqual("invalid-or-unavailable", result["stage_checks"][stage])

    def test_direct_validation_rejects_non_object(self):
        for raw in ([], None, "oops"):
            with self.subTest(raw=raw), self.assertRaises(ValueError):
                validate_observation("t4a", raw, 0, self.manifest)

    def test_cli_non_object_emits_fixed_anonymized_json_and_exit2(self):
        for raw in (b"[]", b"null", b'"oops"'):
            with self.subTest(raw=raw), tempfile.TemporaryDirectory() as private:
                output = Path(private) / "new-evidence"
                buffer = io.StringIO()
                with (mock.patch.object(cli, "guarded_platform", return_value=True),
                      mock.patch.object(cli, "checked_observer", return_value=Path(__file__)),
                      mock.patch.object(cli, "sha256_file", return_value="synthetic-sha"),
                      mock.patch.object(cli, "checked_output", return_value=output),
                      mock.patch.object(cli, "native_runner", return_value=(0, raw)),
                      contextlib.redirect_stdout(buffer)):
                    code = cli.main(["--profile-exe", "synthetic-profile",
                                     "--attribute-exe", "synthetic-attribute",
                                     "--output", str(output)])
                self.assertEqual(2, code)
                displayed = json.loads(buffer.getvalue())
                self.assertEqual("T4-C1", displayed["phase"])
                self.assertEqual("blocked-inconclusive", displayed["status"])
                self.assertEqual("t4a-invalid-or-unavailable", displayed["reason_code"])
                self.assertFalse(displayed["registration_authorized"])
                self.assertNotIn("traceback", buffer.getvalue().lower())
                self.assertTrue((output / "review-summary.json").exists())
                self.assertFalse((output / "t4a.json").exists())

    def test_output_rejects_symlink_parent_and_windows_junction(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            repo = base / "checkout"
            repo.mkdir()
            alias = base / "alias"
            if os.name == "nt":
                # Junction creation does not need Developer Mode/admin rights.
                try:
                    subprocess.run(["cmd", "/d", "/c", "mklink", "/J",
                                    str(alias), str(repo)],
                                   check=True, capture_output=True)
                except (OSError, subprocess.CalledProcessError) as exc:
                    self.skipTest("cannot create Windows junction: " + type(exc).__name__)
            else:
                alias.symlink_to(repo, target_is_directory=True)
            with mock.patch.object(cli, "ROOT", repo):
                with self.assertRaises(ValueError):
                    checked_output(alias / "fresh-evidence")
                self.assertFalse((repo / "fresh-evidence").exists())

    def test_output_accepts_canonical_fresh_destination(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            repo = base / "checkout"
            repo.mkdir()
            external = base / "private"
            external.mkdir()
            with mock.patch.object(cli, "ROOT", repo):
                self.assertEqual((external / "fresh").resolve(),
                                 checked_output(external / "fresh"))
                (external / "fresh").mkdir()
                with self.assertRaises(ValueError):
                    checked_output(external / "fresh")
                with self.assertRaises(ValueError):
                    checked_output(repo / "fresh")

    def test_native_runner_bounds_output_while_reading(self):
        command = [sys.executable, "-c",
                   "import sys;sys.stdout.buffer.write(b'{}')"]
        code, output = native_runner(command)
        self.assertEqual(0, code)
        self.assertEqual(b"{}", output)
        oversized = [sys.executable, "-c",
                     "import sys;sys.stdout.buffer.write(b'x' * 131073);"
                     "sys.stderr.buffer.write(b'y' * 500000)"]
        with self.assertRaises(ValueError):
            native_runner(oversized)

    def test_platform_and_ack_controls(self):
        self.assertFalse(guarded_platform("linux", {}, vm_readonly=True,
                                         snapshot_confirmed=True,
                                         provider_consent=True))
        self.assertFalse(guarded_platform("win32", {"GITHUB_ACTIONS": "true"},
                                         vm_readonly=True, snapshot_confirmed=True,
                                         provider_consent=True))
        for key in ("vm_readonly", "snapshot_confirmed", "provider_consent"):
            inputs = {"vm_readonly": True, "snapshot_confirmed": True,
                      "provider_consent": True}
            inputs[key] = False
            self.assertFalse(guarded_platform("win32", {}, **inputs))
        self.assertTrue(guarded_platform(
            "win32", {}, vm_readonly=True, snapshot_confirmed=True,
            provider_consent=True))

    def test_exit_classification_never_authorizes(self):
        state = validate_observation("t4a", self.registry(False), 0, self.manifest)
        self.assertEqual("precheck-clear", state)


if __name__ == "__main__":
    unittest.main(verbosity=2)
