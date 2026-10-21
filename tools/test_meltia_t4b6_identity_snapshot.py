#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""Offline negative tests for the T4-B6 native snapshot generator (NO COM)."""
import json
import re
import unittest

from generate_meltia_t4b6_snapshot import FIELDS, generate
from meltia_t4a_preflight import MANIFEST


class T4B6SnapshotTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.raw = MANIFEST.read_bytes()
        cls.manifest = json.loads(cls.raw)

    def test_snapshot_is_bound_to_exact_manifest_bytes_and_roles(self):
        header = generate(self.raw)
        self.assertIn("kManifestBytes[]", header)
        # Reconstruct encoded bytes; C++ lexical newline folding must not
        # change LF/CRLF in the snapshotted manifest.
        escaped = re.findall(r"\\\\x([0-9a-f]{2})", header)
        self.assertEqual(self.raw, bytes(int(part, 16) for part in escaped))
        self.assertEqual(1, header.count("kExpectedGuids[4]"))
        for field in FIELDS:
            guid = self.manifest["identity"][field].upper()
            self.assertEqual(1, header.count(guid))

    def test_all_deployment_enablers_are_rejected(self):
        for key in self.manifest["deployment"]:
            changed = json.loads(self.raw)
            changed["deployment"][key] = True
            with self.subTest(field=key), self.assertRaises(ValueError):
                generate(json.dumps(changed).encode("utf-8"))

    def test_disabled_and_design_only_flags_are_mandatory(self):
        mutations = (
            ("registrationEnabled", True),
            ("status", "active"),
            ("phase", "T4"),
        )
        for key, value in mutations:
            changed = json.loads(self.raw)
            changed[key] = value
            with self.subTest(field=key), self.assertRaises(ValueError):
                generate(json.dumps(changed).encode("utf-8"))

    def test_swapped_roles_and_duplicate_guid_fail(self):
        changed = json.loads(self.raw)
        a, b = FIELDS[1:3]
        changed["identity"][a], changed["identity"][b] = (
            changed["identity"][b], changed["identity"][a])
        # A valid *different* T2 layout would require a fresh review/build;
        # never assume swapped roles are accepted by the previous binary.
        swapped = generate(json.dumps(changed).encode("utf-8"))
        original = generate(self.raw)
        self.assertNotEqual(original, swapped)
        changed["identity"][a] = changed["identity"][b]
        with self.assertRaises(ValueError):
            generate(json.dumps(changed).encode("utf-8"))

    def test_duplicate_json_key_and_invalid_bytes_fail(self):
        bad_json = self.raw.decode("utf-8").replace(
            '"registrationEnabled": false,',
            '"registrationEnabled": false, "registrationEnabled": false,', 1)
        with self.assertRaisesRegex(ValueError, "duplicate"):
            generate(bad_json.encode("utf-8"))
        for bad in (b"", b"not-json", b"\xff", b" " * 131073):
            with self.subTest(data=bad[:8]), self.assertRaises(
                    (ValueError, UnicodeError, json.JSONDecodeError)):
                generate(bad)


if __name__ == "__main__":
    unittest.main(verbosity=2)
