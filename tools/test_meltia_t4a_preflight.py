#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-A pure/negative tests: never access a real Windows registry."""
import copy
import json
from pathlib import Path
import unittest

import meltia_t4a_preflight as preflight


class T4aReadOnlyPreflightTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads(preflight.MANIFEST.read_text(encoding="utf-8"))
        cls.identities = preflight.validate_preflight_manifest(cls.manifest)

    def test_probe_plan_only_covers_documented_guids_hives_views(self):
        probes = list(preflight.planned_probes(self.identities))
        self.assertEqual(len(probes), 5 * 2 * 2 * 2)
        self.assertEqual(len({(x["identity"], x["hive"], x["view"], x["key"])
                              for x in probes}), len(probes))
        for item in probes:
            self.assertIn(item["hive"], ("HKLM", "HKCU"))
            self.assertIn(item["view"], ("32", "64"))
            self.assertTrue(item["key"].endswith(
                "{" + self.identities[item["identity"]].upper() + "}"
            ))
            self.assertIn(
                item["key"].rsplit("\\", 1)[0], preflight.REGISTRY_ROOTS
            )

    def test_absent_registry_keys_only_mean_bounded_precheck_clear(self):
        result = preflight.inspect(self.identities, lambda *_: False)
        self.assertEqual(result["status"], "precheck-clear")
        self.assertEqual(result["counts"], {
            "occupied": 0, "absent": 40, "unknown": 0,
        })
        self.assertIs(result["registration_authorized"], False)
        self.assertIs(result["registry_writes"], False)

    def test_existing_com_or_tsf_key_blocks(self):
        for needle in ("CLSID", "CTF"):
            with self.subTest(root=needle):
                result = preflight.inspect(
                    self.identities,
                    lambda _h, _v, key: needle in key,
                )
                self.assertEqual(result["status"], "occupied")
                self.assertGreater(result["counts"]["occupied"], 0)
                self.assertIs(result["registration_authorized"], False)

    def test_denied_or_unsupported_view_is_inconclusive(self):
        def probe(_hive, view, _path):
            if view == "32":
                raise PermissionError("access denied")
            return False
        result = preflight.inspect(self.identities, probe)
        self.assertEqual(result["status"], "inconclusive")
        self.assertEqual(result["counts"]["unknown"], 20)
        self.assertIs(result["registration_authorized"], False)

    def test_occupied_and_unknown_remains_inconclusive(self):
        def probe(_hive, view, _path):
            if view == "32":
                return None
            return True
        result = preflight.inspect(self.identities, probe)
        self.assertEqual(result["status"], "inconclusive")
        self.assertEqual(result["counts"]["occupied"], 20)

    def test_t2_manifest_must_remain_disabled(self):
        bads = []
        for path in (("registrationEnabled",),
                     ("deployment", "performRegistration"),
                     ("deployment", "enableTsfMode"),
                     ("deployment", "copyNativeArtifacts"),
                     ("deployment", "modifyMeltypeRegistration")):
            bad = copy.deepcopy(self.manifest)
            obj = bad
            for part in path[:-1]:
                obj = obj[part]
            obj[path[-1]] = True
            bads.append(bad)
        for bad in bads:
            with self.subTest(changed=str(bad["deployment"])):
                with self.assertRaises(ValueError):
                    preflight.validate_preflight_manifest(bad)

    def test_duplicate_proposed_identity_is_rejected(self):
        bad = copy.deepcopy(self.manifest)
        bad["identity"]["profileGuid"] = bad["identity"]["clsid"]
        with self.assertRaises(ValueError):
            preflight.validate_preflight_manifest(bad)

    def test_source_contains_no_registry_mutation_or_process_launch(self):
        source = Path(preflight.__file__).read_text(encoding="utf-8")
        # Include bracket forms, so adding a forbidden operation to real
        # preflight logic cannot be silently overlooked.
        for forbidden in (
            "winreg.CreateKey", "winreg.SetValue", "winreg.DeleteKey",
            "winreg.DeleteValue", "winreg.SaveKey", "winreg.LoadKey",
            "winreg.ConnectRegistry", "subprocess.", "os.system(",
            "ctypes.windll", "regsvr32", "RegisterProfile(",
            "DllRegisterServer(",
        ):
            # The source does not invoke these APIs.
            self.assertNotIn(forbidden, source)

    def test_rejects_invalid_probe_return_type(self):
        with self.assertRaises(ValueError):
            preflight.inspect(self.identities, lambda *_: "unverified")


if __name__ == "__main__":
    unittest.main()
