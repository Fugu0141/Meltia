#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""Synthetic negative-path tests. No DLL build, loading, TSF calls or writes outside temp."""
import copy
import contextlib
import io
import hashlib
import json
from pathlib import Path
import struct
import tempfile
import unittest

import meltia_t4b4_native_artifact_gate as gate


MANIFEST = {
    "phase": "T2", "status": "design-only", "registrationEnabled": False,
    "distribution": {"assemblyBaseName": "MeltiaTip"},
    "deployment": {"performRegistration": False, "modifyMeltypeRegistration": False,
                   "enableTsfMode": False, "copyNativeArtifacts": False},
}


def synthetic_pe(machine, magic):
    """Only synthetic PE headers, not executable DLLs."""
    content = bytearray(0x200)
    content[0:2] = b"MZ"
    struct.pack_into("<I", content, 0x3C, 0x80)
    content[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<HHIIIHH", content, 0x84, machine, 1, 0, 0, 0,
                     0xF0 if magic == 0x20B else 0xE0, 0x2000)
    optional_start = 0x98
    struct.pack_into("<H", content, optional_start, magic)
    fixed_size = 96 if magic == 0x10B else 112
    struct.pack_into("<I", content, optional_start + fixed_size - 4, 16)
    return bytes(content)


class NativeArtifactGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "STAGING_NOTICE.txt").write_text(
            "T3a experimental build; NOT INSTALLABLE; SELFREG_E_CLASS\n", encoding="utf-8")
        for arch, machine, magic in gate.ARCHITECTURES:
            file = self.root / "bin" / arch / "MeltiaTip.dll"
            file.parent.mkdir(parents=True)
            file.write_bytes(synthetic_pe(machine, magic))

    def verify(self):
        return gate.verify_staging_directory(self.root, MANIFEST)

    def test_valid_architectures_and_sha(self):
        result = self.verify()
        self.assertEqual([a["architecture"] for a in result["artifacts"]], ["x64", "x86"])
        self.assertEqual(result["artifacts"][0]["sha256"], hashlib.sha256(
            (self.root / "bin/x64/MeltiaTip.dll").read_bytes()).hexdigest())
        self.assertFalse(result["registration_authorized"])
        self.assertFalse(result["registration_stubs_proven"])
        self.assertFalse(result["build_provenance_proven"])

    def test_swapped_architecture_rejected(self):
        x64 = self.root / "bin/x64/MeltiaTip.dll"
        x64.write_bytes(synthetic_pe(0x14C, 0x10B))
        with self.assertRaisesRegex(ValueError, "machine"):
            self.verify()

    def test_wrong_optional_header_magic_rejected(self):
        x86 = self.root / "bin/x86/MeltiaTip.dll"
        x86.write_bytes(synthetic_pe(0x14C, 0x20B))
        with self.assertRaisesRegex(ValueError, "magic"):
            self.verify()

    def test_two_byte_optional_header_rejected(self):
        # Reproduces review P2: correct Magic, but only two declared bytes.
        for arch in ("x64", "x86"):
            with self.subTest(arch=arch):
                file = self.root / "bin" / arch / "MeltiaTip.dll"
                original = file.read_bytes()
                data = bytearray(original)
                struct.pack_into("<H", data, 0x84 + 16, 2)
                file.write_bytes(data)
                with self.assertRaisesRegex(ValueError, "optional"):
                    self.verify()
                file.write_bytes(original)

    def test_fixed_optional_header_truncation_rejected(self):
        for arch, fixed in (("x64", 112), ("x86", 96)):
            with self.subTest(arch=arch):
                file = self.root / "bin" / arch / "MeltiaTip.dll"
                original = file.read_bytes()
                data = bytearray(original)
                struct.pack_into("<H", data, 0x84 + 16, fixed - 1)
                file.write_bytes(data)
                with self.assertRaisesRegex(ValueError, "optional"):
                    self.verify()
                file.write_bytes(original)

    def test_directory_count_requires_declared_space(self):
        for arch, fixed in (("x64", 112), ("x86", 96)):
            with self.subTest(arch=arch):
                file = self.root / "bin" / arch / "MeltiaTip.dll"
                original = file.read_bytes()
                data = bytearray(original)
                struct.pack_into("<H", data, 0x84 + 16, fixed + 8 * 15)
                file.write_bytes(data)
                with self.assertRaisesRegex(ValueError, "directory"):
                    self.verify()
                file.write_bytes(original)

    def test_directory_count_too_large_rejected(self):
        for arch, fixed in (("x64", 112), ("x86", 96)):
            with self.subTest(arch=arch):
                file = self.root / "bin" / arch / "MeltiaTip.dll"
                original = file.read_bytes()
                data = bytearray(original)
                struct.pack_into("<I", data, 0x98 + fixed - 4, 17)
                file.write_bytes(data)
                with self.assertRaisesRegex(ValueError, "directory"):
                    self.verify()
                file.write_bytes(original)

    def test_smaller_valid_directory_table_is_accepted(self):
        for arch, fixed in (("x64", 112), ("x86", 96)):
            with self.subTest(arch=arch):
                file = self.root / "bin" / arch / "MeltiaTip.dll"
                original = file.read_bytes()
                data = bytearray(original)
                # Valid minimal optional header with 0 directories.
                struct.pack_into("<I", data, 0x98 + fixed - 4, 0)
                struct.pack_into("<H", data, 0x84 + 16, fixed)
                file.write_bytes(data)
                self.assertEqual(self.verify()["status"],
                                 "structural-audit-only-not-authorized")
                file.write_bytes(original)

    def test_truncated_header_rejected(self):
        x64 = self.root / "bin/x64/MeltiaTip.dll"
        x64.write_bytes(b"MZ" + b"\0" * 63)
        with self.assertRaises(ValueError):
            self.verify()

    def test_missing_dll_rejected(self):
        (self.root / "bin/x86/MeltiaTip.dll").unlink()
        with self.assertRaisesRegex(ValueError, "missing x86"):
            self.verify()

    def test_symlink_rejected(self):
        dst = self.root / "bin/x86/MeltiaTip.dll"
        dst.unlink()
        try:
            dst.symlink_to(self.root / "bin/x64/MeltiaTip.dll")
        except (OSError, NotImplementedError):
            self.skipTest("symlink not available on this test host")
        with self.assertRaisesRegex(ValueError, "symlink"):
            self.verify()

    def test_malformed_notice_rejected(self):
        (self.root / "STAGING_NOTICE.txt").write_text("INSTALLABLE", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "notice"):
            self.verify()

    def test_manifest_optin_is_rejected(self):
        manifest = copy.deepcopy(MANIFEST)
        manifest["deployment"]["performRegistration"] = True
        with self.assertRaisesRegex(ValueError, "deployment"):
            gate.verify_staging_directory(self.root, manifest)

    def test_boolean_and_missing_manifest_fields_rejected(self):
        for key in ("copyNativeArtifacts", "enableTsfMode"):
            manifest = copy.deepcopy(MANIFEST)
            del manifest["deployment"][key]
            with self.subTest(key=key), self.assertRaises(ValueError):
                gate.validate_manifest(manifest)

    def test_incorrect_pe_offset_rejected(self):
        f = self.root / "bin/x64/MeltiaTip.dll"
        data = bytearray(f.read_bytes())
        struct.pack_into("<I", data, 0x3C, 0x1F0)
        f.write_bytes(data)
        with self.assertRaisesRegex(ValueError, "signature"):
            self.verify()

    def test_non_dll_pe_rejected(self):
        f = self.root / "bin/x86/MeltiaTip.dll"
        data = bytearray(f.read_bytes())
        struct.pack_into("<H", data, 0x80 + 4 + 18, 0)
        f.write_bytes(data)
        with self.assertRaisesRegex(ValueError, "not marked as DLL"):
            self.verify()

    def test_non_dict_distribution_rejected(self):
        manifest = copy.deepcopy(MANIFEST)
        manifest["distribution"] = None
        with self.assertRaisesRegex(ValueError, "assembly"):
            gate.validate_manifest(manifest)

    def test_non_pe_rejected(self):
        f = self.root / "bin/x86/MeltiaTip.dll"
        f.write_bytes(b"not a real DLL")
        with self.assertRaisesRegex(ValueError, "DOS/PE"):
            self.verify()

    def test_main_os_error_does_not_leak_stage_path(self):
        prior = gate.MANIFEST
        gate.MANIFEST = self.root / "fake-manifest.json"
        try:
            (self.root / "fake-manifest.json").write_text(json.dumps(MANIFEST), encoding="utf-8")
            (self.root / "STAGING_NOTICE.txt").unlink()
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                self.assertEqual(gate.main(["--staging-root", str(self.root)]), 2)
            self.assertNotIn(str(self.root), output.getvalue())
        finally:
            gate.MANIFEST = prior

    def test_main_failure_is_fail_closed(self):
        prior = gate.MANIFEST
        gate.MANIFEST = self.root / "fake-manifest.json"
        try:
            (self.root / "fake-manifest.json").write_text(json.dumps(MANIFEST), encoding="utf-8")
            self.assertEqual(gate.main(["--staging-root", str(self.root)]), 0)
            (self.root / "bin/x86/MeltiaTip.dll").unlink()
            self.assertEqual(gate.main(["--staging-root", str(self.root)]), 2)
        finally:
            gate.MANIFEST = prior


if __name__ == "__main__":
    unittest.main()
