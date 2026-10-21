#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""Offline T4-B1 adversarial evidence tests; no Windows registry/COM calls."""
import copy
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import meltia_t4a_preflight as t4a
import meltia_t4b_audit_gate as gate


class T4bProfileAuditTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads(t4a.MANIFEST.read_text(encoding="utf-8"))
        cls.identities = t4a.validate_preflight_manifest(cls.manifest)

    def setUp(self):
        self.registry = t4a.inspect(self.identities, lambda *_: False)
        self.profiles = {
            "phase": "T4-B1",
            "status": "enumerated-clear",
            "coverage": "current-user-visible-tsf-profiles",
            "complete": True,
            "expected_clsid": "{" + self.identities["clsid"].upper() + "}",
            "expected_profile_guid": "{" + self.identities["profileGuid"].upper() + "}",
            "profile_count": 17,
            "clsid_matches": 0,
            "profile_guid_matches": 0,
            "registry_writes": False,
            "registration_authorized": False,
        }

    def assess(self):
        return gate.assess(self.registry, self.profiles, self.manifest)

    def test_clear_still_refuses_registration(self):
        result = self.assess()
        self.assertEqual(result["status"], "audit-clear-not-authorized")
        self.assertIs(result["registry_writes"], False)
        self.assertIs(result["registration_authorized"], False)

    def test_clsid_collision_blocks(self):
        self.profiles["clsid_matches"] = 1
        self.profiles["status"] = "occupied"
        self.assertEqual(self.assess()["status"], "occupied")

    def test_profile_collision_under_different_text_service_blocks(self):
        # Profile GUID match does not depend on its owning CLSID.
        self.profiles["profile_guid_matches"] = 1
        self.profiles["status"] = "occupied"
        self.assertEqual(self.assess()["status"], "occupied")

    def test_t4a_existing_key_blocks_even_if_profiles_clear(self):
        self.registry = t4a.inspect(
            self.identities, lambda h, v, k: k.endswith(
                "{" + self.identities["clsid"].upper() + "}"
            )
        )
        self.assertEqual(self.assess()["status"], "occupied")

    def test_unknown_registry_view_is_inconclusive(self):
        self.registry = t4a.inspect(
            self.identities, lambda h, v, k: None if v == "32" else False
        )
        self.assertEqual(self.assess()["status"], "inconclusive")

    def test_partial_tsf_enumeration_is_inconclusive_even_with_matches(self):
        self.profiles.update(complete=False, status="inconclusive",
                             clsid_matches=2)
        self.assertEqual(self.assess()["status"], "inconclusive")

    def test_malformed_or_missing_registry_findings_fail_closed(self):
        for kind in ("missing", "wrong-path", "false-count", "flag", "phase"):
            with self.subTest(kind=kind):
                self.registry = t4a.inspect(self.identities, lambda *_: False)
                if kind == "missing":
                    self.registry["findings"].pop()
                elif kind == "wrong-path":
                    self.registry["findings"][0]["key"] = "other"
                elif kind == "false-count":
                    self.registry["counts"]["absent"] = 40_000
                elif kind == "flag":
                    self.registry["registration_authorized"] = True
                else:
                    self.registry["phase"] = "T4-A-faked"
                self.assertEqual(self.assess()["status"], "inconclusive")

    def test_t4a_counts_reject_bool_float_string_negative_and_extra_keys(self):
        # False == 0, True == 1, 0.0 == 0 must not bypass validation.
        mutations = (
            {"occupied": False, "absent": 40, "unknown": False},
            {"occupied": 0.0, "absent": 40, "unknown": 0},
            {"occupied": 0, "absent": True, "unknown": 0},
            {"occupied": 0, "absent": "40", "unknown": 0},
            {"occupied": -1, "absent": 40, "unknown": 0},
            {"occupied": 0, "absent": 40, "unknown": 0, "extra": 0},
            {"occupied": 0, "absent": 40},
            {"occupied": 0, "absent": 41, "unknown": 0},
        )
        for counts in mutations:
            with self.subTest(counts=counts):
                self.registry = t4a.inspect(self.identities, lambda *_: False)
                self.registry["counts"] = counts
                self.assertEqual(self.assess()["status"], "inconclusive")
                self.assertIs(self.assess()["registration_authorized"], False)

    def test_ps51_and_ps7_evidence_files_are_accepted_with_matching_guid(self):
        # Actual capture is only performed by an operator on a separate VM.
        # These bytes reproduce PowerShell 5.1 redirection (UTF-16LE BOM),
        # PowerShell 7 redirection (UTF-8), and explicitly encoded BOM files.
        formats = (
            ("ps51-utf16le-bom", "utf-16"),
            ("ps7-utf8", "utf-8"),
            ("utf8-bom", "utf-8-sig"),
            ("utf16be-bom", "utf-16-be"),
        )
        with tempfile.TemporaryDirectory() as root:
            for label, encoding in formats:
                with self.subTest(format=label):
                    t4a_path = Path(root) / "t4a.json"
                    tsf_path = Path(root) / "tsf.json"
                    for file, report in ((t4a_path, self.registry),
                                         (tsf_path, self.profiles)):
                        payload = json.dumps(report, ensure_ascii=False)
                        if encoding == "utf-16-be":
                            data = b"\xfe\xff" + payload.encode("utf-16-be")
                        else:
                            data = payload.encode(encoding)
                        file.write_bytes(data)
                        self.assertEqual(gate.read_evidence_json(file), report)
                    stdout = io.StringIO()
                    argv = ["meltia_t4b_audit_gate.py",
                            "--t4a", str(t4a_path), "--tsf", str(tsf_path)]
                    with mock.patch("sys.argv", argv), redirect_stdout(stdout):
                        code = gate.main()
                    response = json.loads(stdout.getvalue())
                    self.assertEqual(code, 0)
                    self.assertEqual(response["status"], "audit-clear-not-authorized")
                    self.assertIs(response["registration_authorized"], False)

    def test_invalid_evidence_encoding_fails_closed(self):
        with tempfile.TemporaryDirectory() as root:
            valid = Path(root) / "tsf.json"
            t4a_path = Path(root) / "t4a.json"
            valid.write_bytes(json.dumps(self.profiles).encode("utf-8"))
            for invalid_bytes in (
                json.dumps(self.registry).encode("utf-16-le"),  # no BOM
                b"\xff\xff" + json.dumps(self.registry).encode("utf-8"),
                b"\xff\xfe\x00\x00" + json.dumps(self.registry).encode("utf-32-le"),
            ):
                with self.subTest(evidence=invalid_bytes[:6]):
                    t4a_path.write_bytes(invalid_bytes)
                    stdout = io.StringIO()
                    argv = ["meltia_t4b_audit_gate.py",
                            "--t4a", str(t4a_path), "--tsf", str(valid)]
                    with mock.patch("sys.argv", argv), redirect_stdout(stdout):
                        code = gate.main()
                    response = json.loads(stdout.getvalue())
                    self.assertEqual(code, 2)
                    self.assertEqual(response["status"], "inconclusive")
                    self.assertIs(response["registration_authorized"], False)

    def test_invalid_profile_report_fails_closed(self):
        for change in (
            {"expected_profile_guid": "{00000000-0000-4000-8000-000000000001}"},
            {"expected_clsid": "invalid"},
            {"complete": "true"},
            {"profile_count": -1},
            {"profile_count": True},
            {"profile_guid_matches": 18},
            {"clsid_matches": 2, "status": "enumerated-clear"},
            {"registration_authorized": True},
            {"coverage": "whole-system"},
            {"phase": "T4-B2"},
        ):
            with self.subTest(change=change):
                self.profiles = {**self.profiles, **change}
                self.assertEqual(self.assess()["status"], "inconclusive")
                self.profiles = {**self.profiles, **{k: (
                    "{" + self.identities["profileGuid"].upper() + "}"
                    if k == "expected_profile_guid" else
                    "{" + self.identities["clsid"].upper() + "}"
                    if k == "expected_clsid" else
                    True if k == "complete" else
                    17 if k == "profile_count" else
                    0 if k in ("profile_guid_matches", "clsid_matches") else
                    False if k == "registration_authorized" else
                    "current-user-visible-tsf-profiles" if k == "coverage" else
                    "T4-B1" if k == "phase" else "enumerated-clear"
                ) for k in change}}

    def test_tampered_manifest_cannot_clear(self):
        changed = copy.deepcopy(self.manifest)
        changed["registrationEnabled"] = True
        with self.assertRaises(ValueError):
            gate.assess(self.registry, self.profiles, changed)

    def test_unknown_profile_failure_is_inconclusive(self):
        self.profiles["status"] = "inconclusive"
        self.profiles["complete"] = False
        self.assertEqual(self.assess()["status"], "inconclusive")

    def test_no_mutating_calls_in_native_helper(self):
        source = (Path(__file__).parent / "meltia_t4b_profile_inventory.cpp"
                  ).read_text(encoding="utf-8")
        for forbidden in ("RegisterProfile(", "UnregisterProfile(",
                          "DllRegisterServer(", "RegCreateKey",
                          "RegSetValue", "RegDeleteKey", "CreateProcess",
                          "ActivateProfile(", "GetProfileDescription("):
            self.assertNotIn(forbidden, source)

    def test_no_subprocess_or_os_mutation_in_gate(self):
        source = Path(gate.__file__).read_text(encoding="utf-8")
        for forbidden in ("subprocess.", "winreg.", "ctypes.", "os.system(",
                          ".write_text(", ".write_bytes("):
            self.assertNotIn(forbidden, source)


if __name__ == "__main__":
    unittest.main()
