#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-B2 offline inverse-ownership model regression and adversarial tests."""
import copy
import json
from pathlib import Path
import unittest
from unittest import mock
import io
from contextlib import redirect_stdout

import meltia_t4b2_ownership_model as model
from meltia_t4a_preflight import MANIFEST


class OwnershipModelTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))

    def setUp(self):
        self.resources = model.owned_resources(self.manifest)
        self.ids = [r["id"] for r in self.resources]
        self.before = dict.fromkeys(self.ids, "absent")
        self.after = dict.fromkeys(self.ids, "absent")

    def check(self, executed=()):
        return model.evaluate_synthetic_rollback(
            self.resources, self.before, list(executed), self.after)

    def test_exact_ownership_resources_and_api_boundaries(self):
        inv = model.design_inventory(self.manifest)
        self.assertEqual(len(self.resources), 8)
        self.assertEqual(self.ids[:3], ["com:64", "com:32", "profile"])
        self.assertEqual(len(self.ids[3:]), 5)
        self.assertEqual(len(set(self.ids)), 8)
        for item in self.resources:
            self.assertIn(self.manifest["identity"]["clsid"].upper(), item["owner"])
        for item in self.resources[:2]:
            self.assertEqual(item["scope"], "HKLM")
            self.assertTrue(item["target"].startswith(r"Software\Classes\CLSID"))
        profile = self.resources[2]
        self.assertEqual(profile["profile_guid"],
                         "{" + self.manifest["identity"]["profileGuid"].upper() + "}")
        self.assertEqual(profile["language_id"], "0x0411")
        for item in self.resources[3:]:
            self.assertEqual(item["kind"], "tsf-category-association")
            self.assertEqual(item["owner"], item["item"])
        self.assertEqual(
            [r["category"] for r in self.resources[3:]],
            self.manifest["distribution"]["uninstallOwnership"]["categoryAssociations"])
        for key in ("registration_authorized", "registry_writes",
                    "delete_shared_category_guids", "delete_foreign_products",
                    "automatic_rollback"):
            self.assertIs(inv[key], False)

    def test_attributes_are_not_claimed_as_owned_resources(self):
        inv = model.design_inventory(self.manifest)
        self.assertEqual(
            [r["field"] for r in inv["display_attributes_unverified"]],
            ["inputAttributeGuid", "convertedAttributeGuid", "targetAttributeGuid"])
        self.assertTrue(all(r["deletable"] is False for r in inv["display_attributes_unverified"]))
        for key in self.manifest["identity"]:
            if key.endswith("AttributeGuid"):
                self.assertNotIn(key, self.ids)
        self.assertGreaterEqual(len(inv["unresolved"]), 6)

    def test_all_prefixes_inverse_order_and_never_authorize(self):
        for applied in range(9):
            with self.subTest(applied=applied):
                result = model.simulate_prefix(self.manifest, applied)
                self.assertEqual(result["status"], "simulation-reversible-not-authorized")
                self.assertEqual(result["candidate_rollback_order"],
                                 list(reversed(self.ids[:applied])))
                self.assertIs(result["registration_authorized"], False)
                self.assertIs(result["registry_writes"], False)
                self.assertIs(result["automatic_rollback"], False)

    def test_any_foreign_or_unknown_baseline_stops_before_changes(self):
        for existing in ("occupied", "unknown", "foreign", None):
            for target in self.ids:
                with self.subTest(state=existing, resource=target):
                    self.before = dict.fromkeys(self.ids, "absent")
                    self.before[target] = existing
                    outcome = self.check()
                    self.assertEqual(outcome["status"], "halt-manual-review")
                    self.assertEqual(outcome["candidate_rollback_order"], [])

    def test_rejects_undeclared_modified_or_unknown_after_state(self):
        self.after[self.ids[0]] = "created-owned"
        for target in self.ids:
            for bad in ("unknown", "foreign", "missing", None):
                with self.subTest(target=target, result=bad):
                    modified = dict(self.after)
                    modified[target] = bad
                    outcome = model.evaluate_synthetic_rollback(
                        self.resources, self.before, [self.ids[0]], modified)
                    self.assertEqual(outcome["status"], "halt-manual-review")
                    self.assertFalse(outcome["candidate_rollback_order"])

    def test_rejects_missing_extra_or_modified_snapshots(self):
        for selector, changed in (("before", self.before), ("after", self.after)):
            for action in ("missing", "extra"):
                with self.subTest(which=selector, action=action):
                    initial = dict(changed)
                    if action == "missing":
                        initial.pop(self.ids[0])
                    else:
                        initial["foreign:COM"] = "absent"
                    res = model.evaluate_synthetic_rollback(
                        self.resources, initial if selector == "before" else self.before,
                        [], initial if selector == "after" else self.after)
                    self.assertEqual(res["status"], "halt-manual-review")

    def test_rejects_nonprefix_duplicates_and_foreign_ledger(self):
        for bad in ([self.ids[1]], [self.ids[0], self.ids[0]],
                    [self.ids[1], self.ids[0]], ["foreign"],
                    [self.ids[0], "foreign"], [False], [None],
                    self.ids + [self.ids[0]]):
            with self.subTest(ledger=bad):
                res = self.check(bad)
                self.assertEqual(res["status"], "halt-manual-review")
                self.assertEqual(res["candidate_rollback_order"], [])

    def test_nonexecuted_external_change_stops(self):
        self.after[self.ids[-1]] = "created-owned"
        self.assertEqual(self.check()["status"], "halt-manual-review")

    def test_invalid_simulation_count_is_rejected(self):
        for bad in (-1, 9, True, False, 2.0, "2"):
            with self.subTest(count=bad), self.assertRaises(ValueError):
                model.simulate_prefix(self.manifest, bad)

    def test_manifest_change_or_enabled_deployment_stops(self):
        for key in ("registrationEnabled", "modifyMeltypeRegistration"):
            m = copy.deepcopy(self.manifest)
            if key == "registrationEnabled":
                m[key] = True
            else:
                m["deployment"][key] = True
            with self.subTest(key=key), self.assertRaises(ValueError):
                model.design_inventory(m)

    def test_manifest_category_ownership_change_rejected(self):
        for field, value in (
            ("categoryAssociations", []),
            ("categoryRegistrationOwner", "Meltype"),
            ("removeSharedCategoryGuids", True),
            ("removeForeignProductRegistrations", True),
            ("removeUserDataAutomatically", True),
        ):
            altered = copy.deepcopy(self.manifest)
            altered["distribution"]["uninstallOwnership"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                model.owned_resources(altered)

    def test_cli_synthetic_only_and_never_authorizes(self):
        with mock.patch("sys.argv", ["meltia_t4b2_ownership_model.py",
                                     "--simulate-prefix", "4"]):
            stdout = io.StringIO()
            with redirect_stdout(stdout):
                exit_code = model.main()
        self.assertEqual(exit_code, 0)
        parsed = json.loads(stdout.getvalue())
        self.assertEqual(parsed["simulation"]["candidate_rollback_order"],
                         list(reversed(self.ids[:4])))
        self.assertIs(parsed["registration_authorized"], False)
        self.assertIs(parsed["simulation"]["registration_authorized"], False)
        self.assertIs(parsed["automatic_rollback"], False)

    def test_cli_invalid_count_fails_closed(self):
        with mock.patch("sys.argv", ["meltia_t4b2_ownership_model.py",
                                     "--simulate-prefix", "30"]):
            output = io.StringIO()
            with redirect_stdout(output):
                exit_code = model.main()
        self.assertEqual(exit_code, 2)
        self.assertEqual(json.loads(output.getvalue())["status"], "inconclusive")

    def test_source_has_no_registration_or_mutation_path(self):
        source = Path(model.__file__).read_text(encoding="utf-8")
        for forbidden in ("winreg.", "subprocess.", "RegDeleteTree",
                          "RegCreateKeyEx", "RegSetValueEx", "regsvr32",
                          "RegisterProfile(", "UnregisterProfile(",
                          "RegisterCategory(", "UnregisterCategory(",
                          ".write_text(", ".write_bytes(", "os.system("):
            # Names may appear as descriptive strings, but *not* as invocations
            if forbidden in ("RegisterProfile(", "RegisterCategory("):
                continue
            self.assertNotIn(forbidden, source)


if __name__ == "__main__":
    unittest.main()
