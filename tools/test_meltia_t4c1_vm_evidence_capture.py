#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-C1 hermetic tests: NO Windows API, no subprocess, no file writes."""
import copy
import json
import unittest

from meltia_t4a_preflight import MANIFEST, inspect, validate_preflight_manifest
from meltia_t4b3_evidence_gate import expected_targets
from meltia_t4c1_vm_evidence_capture import (
    capture, guarded_platform, read_json_bytes, validate_observation,
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
