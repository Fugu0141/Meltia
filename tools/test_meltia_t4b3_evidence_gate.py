#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-B3 evidence gate: 3 attributes, 5 category associations; no registration."""
import copy
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import meltia_t4b3_evidence_gate as gate
from meltia_t4a_preflight import MANIFEST


class EvidenceGateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        cls.targets = gate.expected_targets(cls.manifest)

    def setUp(self):
        self.data = {
            "phase": "T4-B3",
            "coverage": "current-user-visible-tsf-only",
            "registry_writes": False,
            "registration_authorized": False,
            "attributes": [
                {**field, "lookup": "not-found", "owner_clsid": None}
                for field in self.targets["attributes"]
            ],
            "categories": [
                {"category": category, "item_clsid": self.targets["clsid"],
                 "complete": True, "present": False}
                for category in self.targets["categories"]
            ],
        }

    def test_all_negative_observations_remain_inconclusive(self):
        result = gate.assess(self.manifest, self.data)
        self.assertEqual(result["status"], "inconclusive")
        self.assertEqual(len(result["unresolved"]), 3)
        for flag in ("registration_authorized", "registry_writes",
                     "native_tsf_access", "automatic_rollback"):
            self.assertIs(result[flag], False)

    def test_foreign_display_attribute_owner_stops(self):
        for i in range(3):
            with self.subTest(i=i):
                report = copy.deepcopy(self.data)
                report["attributes"][i]["lookup"] = "resolved"
                report["attributes"][i]["owner_clsid"] = (
                    "417d801b-a9bd-4c26-bd16-356a825a6998")
                result = gate.assess(self.manifest, report)
                self.assertEqual(result["status"], "occupied")
                self.assertEqual(result["observed_existing"][0]["field"],
                                 report["attributes"][i]["field"])
                self.assertIs(result["observed_existing"][0]["owner_is_meltia"], False)

    def test_prior_own_attribute_is_not_fresh_installable(self):
        self.data["attributes"][1]["lookup"] = "resolved"
        self.data["attributes"][1]["owner_clsid"] = self.targets["clsid"]
        result = gate.assess(self.manifest, self.data)
        self.assertEqual(result["status"], "occupied")
        self.assertIs(result["observed_existing"][0]["owner_is_meltia"], True)

    def test_any_existing_category_stops(self):
        for i in range(5):
            with self.subTest(i=i):
                report = copy.deepcopy(self.data)
                report["categories"][i]["present"] = True
                result = gate.assess(self.manifest, report)
                self.assertEqual(result["status"], "occupied")
                self.assertEqual(result["observed_existing"][0]["category"],
                                 self.targets["categories"][i])

    def test_incomplete_categories_remain_unresolved(self):
        self.data["categories"][0]["complete"] = False
        self.data["categories"][0]["present"] = None
        result = gate.assess(self.manifest, self.data)
        self.assertEqual(result["status"], "inconclusive")
        self.assertEqual(len(result["unresolved"]), 4)

    def test_schema_and_immutability_mutations_rejected(self):
        for key, value in (
            ("phase", "T4-B2"),
            ("coverage", "global-verified"),
            ("registry_writes", True),
            ("registry_writes", 0),
            ("registration_authorized", True),
            ("attributes", self.data["attributes"][:-1]),
            ("categories", self.data["categories"][:-1]),
        ):
            with self.subTest(key=key, value=str(value)[:30]):
                bad = copy.deepcopy(self.data)
                bad[key] = value
                self.assertEqual(gate.assess(self.manifest, bad)["status"],
                                 "inconclusive")
                with self.assertRaises((ValueError, TypeError, KeyError, AttributeError)):
                    gate.validate_evidence(self.manifest, bad)
        for key in self.data:
            bad = copy.deepcopy(self.data)
            del bad[key]
            with self.subTest(missing=key):
                self.assertEqual(gate.assess(self.manifest, bad)["status"], "inconclusive")
                with self.assertRaises(ValueError):
                    gate.validate_evidence(self.manifest, bad)
        bad = copy.deepcopy(self.data)
        bad["extraneous"] = "present"
        with self.assertRaises(ValueError):
            gate.validate_evidence(self.manifest, bad)

    def test_guid_and_category_identity_mutations_rejected(self):
        other = "417d801b-a9bd-4c26-bd16-356a825a6998"
        cases = (
            ("attributes", 0, "guid", other),
            ("attributes", 1, "field", "convertedAttributeMistyped"),
            ("categories", 0, "item_clsid", other),
            ("categories", 1, "category", "GUID_TFCAT_USER_DEFINED"),
        )
        for field, position, key, value in cases:
            with self.subTest(field=field, key=key):
                report = copy.deepcopy(self.data)
                report[field][position][key] = value
                with self.assertRaises(ValueError):
                    gate.validate_evidence(self.manifest, report)

    def test_invalid_owner_and_lookup_rejected(self):
        for lookup, owner in (("resolved", None),
                              ("resolved", "broken"),
                              ("not-found", self.targets["clsid"]),
                              ("failed", self.targets["clsid"]),
                              ("clear", None),
                              (False, None)):
            with self.subTest(lookup=lookup, owner=owner):
                report = copy.deepcopy(self.data)
                report["attributes"][0].update(lookup=lookup, owner_clsid=owner)
                with self.assertRaises((ValueError, TypeError)):
                    gate.validate_evidence(self.manifest, report)

    def test_category_boolean_and_completeness_inconsistency_rejected(self):
        for complete, present in ((1, False), (0, None), ("true", True),
                                  (True, 0), (True, 1), (True, None),
                                  (False, True), (False, False),
                                  (False, "unknown")):
            with self.subTest(complete=complete, present=present):
                report = copy.deepcopy(self.data)
                report["categories"][0].update(complete=complete, present=present)
                with self.assertRaises(ValueError):
                    gate.validate_evidence(self.manifest, report)

    def test_bad_manifest_cannot_authorize(self):
        bad = copy.deepcopy(self.manifest)
        bad["deployment"]["performRegistration"] = True
        with self.assertRaises(ValueError):
            gate.validate_evidence(bad, self.data)

    def test_cli_bom_encodings_and_exit_codes(self):
        for encoding, bom in (
            ("utf-8", b""),
            ("utf-8", b"\xef\xbb\xbf"),
            ("utf-16-le", b"\xff\xfe"),
            ("utf-16-be", b"\xfe\xff"),
        ):
            for collide in (False, True):
                data = copy.deepcopy(self.data)
                if collide:
                    data["categories"][0]["present"] = True
                raw = bom + json.dumps(data).encode(encoding)
                with tempfile.TemporaryDirectory() as tmp:
                    path = Path(tmp) / "evidence.json"
                    path.write_bytes(raw)
                    with self.subTest(encoding=encoding, occupied=collide):
                        stdout = io.StringIO()
                        with patch("sys.argv", ["gate", "--evidence", str(path)]):
                            with redirect_stdout(stdout):
                                exit_code = gate.main()
                        result = json.loads(stdout.getvalue())
                        self.assertEqual(exit_code, 1 if collide else 2)
                        self.assertEqual(result["status"],
                                         "occupied" if collide else "inconclusive")
                        self.assertIs(result["registration_authorized"], False)

    def test_cli_corrupt_missing_or_oversized_evidence_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "evidence.json"
            for data in (b"\x00\xff", b"{broken", b" " * (gate.MAX_BYTES + 1)):
                path.write_bytes(data)
                with patch("sys.argv", ["gate", "--evidence", str(path)]):
                    output = io.StringIO()
                    with redirect_stdout(output):
                        self.assertEqual(gate.main(), 2)
                self.assertEqual(json.loads(output.getvalue())["status"],
                                 "inconclusive")
            with patch("sys.argv", ["gate", "--evidence", str(path) + "-absent"]):
                output = io.StringIO()
                with redirect_stdout(output):
                    self.assertEqual(gate.main(), 2)

    def test_no_os_registration_or_access_in_gate_source(self):
        content = Path(gate.__file__).read_text(encoding="utf-8")
        for fragment in ("import winreg", "import subprocess", "CoCreateInstance",
                         "RegSetValueEx", "RegDeleteTreeW", "RegisterGUID(",
                         "RegisterProfile(", "UnregisterProfile(",
                         "RegisterCategory(", "UnregisterCategory(",
                         "os.system(", ".write_text(", ".write_bytes("):
            self.assertNotIn(fragment, content)


if __name__ == "__main__":
    unittest.main()
