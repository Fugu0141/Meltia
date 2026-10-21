#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-B5 synthetic read-only gate tests; never touches Windows registry or TSF."""
import copy
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import meltia_t4a_preflight as t4a
import meltia_t4b3_evidence_gate as b3
import meltia_t4b5_offline_review_gate as gate
from test_meltia_t4b4_native_artifact_gate import synthetic_pe


class OfflineReviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.manifest = json.loads(t4a.MANIFEST.read_text(encoding="utf-8"))
        self.identity = t4a.validate_preflight_manifest(self.manifest)
        self.t4a = t4a.inspect(self.identity, lambda *_: False)
        self.tsf = {
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
                {**item, "lookup": "not-found", "owner_clsid": None}
                for item in targets["attributes"]
            ],
            "categories": [
                {"category": category, "item_clsid": targets["clsid"],
                 "complete": True, "present": False}
                for category in targets["categories"]
            ],
        }
        self.stage = self.root / "stage"
        self.stage.mkdir()
        (self.stage / "STAGING_NOTICE.txt").write_text(
            "T3a; NOT INSTALLABLE; SELFREG_E_CLASS\n", encoding="utf-8")
        for arch, machine, magic in (("x64", 0x8664, 0x20b),
                                     ("x86", 0x14c, 0x10b)):
            file = self.stage / "bin" / arch / "MeltiaTip.dll"
            file.parent.mkdir(parents=True)
            file.write_bytes(synthetic_pe(machine, magic))

    def assess(self):
        return gate.assess(self.manifest, self.t4a, self.tsf,
                           self.attributes, self.stage)

    def test_clean_bounded_evidence_never_authorizes(self):
        result = self.assess()
        self.assertEqual(result["status"], "blocked-inconclusive")
        self.assertEqual(result["reason_code"],
                         "partial-evidence-never-authorizes-registration")
        self.assertEqual(result["checks"]["registry_and_profile"],
                         "audit-clear-not-authorized")
        self.assertEqual(result["checks"]["staged_native_artifacts"],
                         "structural-audit-only")
        for field in ("registration_authorized", "registry_writes",
                      "native_tsf_access", "native_dll_executed",
                      "automatic_rollback", "independent_vm_provenance_verified"):
            self.assertIs(result[field], False)

    def test_registry_collision_halts(self):
        self.t4a = t4a.inspect(self.identity, lambda *_: True)
        result = self.assess()
        self.assertEqual(result["status"], "blocked-occupied")
        self.assertEqual(result["reason_code"], "observed-identity-conflict")

    def test_tsf_profile_collision_halts(self):
        self.tsf.update(profile_count=1, clsid_matches=1, status="occupied")
        self.assertEqual(self.assess()["status"], "blocked-occupied")

    def test_foreign_display_attribute_owner_halts(self):
        self.attributes["attributes"][0].update(
            lookup="resolved",
            owner_clsid="417d801b-a9bd-4c26-bd16-356a825a6998")
        self.assertEqual(self.assess()["status"], "blocked-occupied")

    def test_own_attribute_existing_also_halts_fresh_install(self):
        self.attributes["attributes"][1].update(
            lookup="resolved", owner_clsid=self.identity["clsid"])
        self.assertEqual(self.assess()["status"], "blocked-occupied")

    def test_registered_category_collision_halts(self):
        self.attributes["categories"][0]["present"] = True
        self.assertEqual(self.assess()["status"], "blocked-occupied")

    def test_x86_artifact_missing_remains_blocked(self):
        (self.stage / "bin/x86/MeltiaTip.dll").unlink()
        result = self.assess()
        self.assertEqual(result["checks"]["staged_native_artifacts"],
                         "inconclusive")
        self.assertEqual(result["status"], "blocked-inconclusive")

    def test_swapped_architecture_remains_blocked(self):
        f = self.stage / "bin/x64/MeltiaTip.dll"
        f.write_bytes(synthetic_pe(0x14c, 0x10b))
        self.assertEqual(self.assess()["checks"]["staged_native_artifacts"],
                         "inconclusive")

    def test_tampered_t2_opt_in_refused(self):
        self.manifest["deployment"]["performRegistration"] = True
        result = self.assess()
        self.assertEqual(result["checks"]["registry_and_profile"],
                         "not-evaluated")
        self.assertFalse(result["registration_authorized"])

    def test_missing_or_mismatched_registry_probes_refused(self):
        self.t4a["findings"].pop()
        self.assertEqual(self.assess()["checks"]["registry_and_profile"],
                         "inconclusive")
        self.assertFalse(self.assess()["registration_authorized"])

    def test_stale_tsf_guid_refused(self):
        self.tsf["expected_profile_guid"] = "21f643f4-72d5-4946-bf70-0a126ab52d05"
        self.assertEqual(self.assess()["checks"]["registry_and_profile"],
                         "inconclusive")

    def test_duplicate_json_keys_rejected(self):
        p = self.root / "duplicate.json"
        p.write_text('{"phase":"T4-A","phase":"T4-B1"}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate"):
            gate.read_evidence(p)

    def test_utf16le_and_utf8_bom_supported(self):
        p = self.root / "power-shell.json"
        for data in ('{"example":true}'.encode("utf-8-sig"),
                     '{"example":true}'.encode("utf-16")):
            p.write_bytes(data)
            self.assertEqual(gate.read_evidence(p), {"example": True})

    def test_oversize_input_and_symlinks_rejected(self):
        p = self.root / "too-large.json"
        p.write_bytes(b"x" * (gate.MAX_EVIDENCE_BYTES + 1))
        with self.assertRaisesRegex(ValueError, "size"):
            gate.read_evidence(p)
        link = self.root / "link.json"
        try:
            link.symlink_to(p)
        except (NotImplementedError, OSError):
            return
        with self.assertRaisesRegex(ValueError, "ordinary"):
            gate.read_evidence(link)

    def test_cli_exit_is_never_zero_and_redacts_input_paths(self):
        paths = []
        for name, report in (("t4a", self.t4a), ("tsf", self.tsf),
                             ("attributes", self.attributes)):
            file = self.root / (name + ".json")
            file.write_text(json.dumps(report), encoding="utf-8")
            paths.append(file)
        args = ["--t4a", str(paths[0]), "--tsf", str(paths[1]),
                "--attributes", str(paths[2]),
                "--staging-root", str(self.stage)]
        out = io.StringIO()
        with patch.object(gate, "MANIFEST", t4a.MANIFEST), redirect_stdout(out):
            self.assertEqual(gate.main(args), 2)
        self.assertNotIn(str(self.root), out.getvalue())
        self.assertFalse(json.loads(out.getvalue())["registration_authorized"])
        paths[0].unlink()
        out = io.StringIO()
        with redirect_stdout(out):
            self.assertEqual(gate.main(args), 2)
        self.assertEqual(json.loads(out.getvalue())["reason_code"],
                         "invalid-or-unreadable-input")
        self.assertNotIn(str(self.root), out.getvalue())


if __name__ == "__main__":
    unittest.main()
