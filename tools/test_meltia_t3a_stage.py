# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""Regression/mutation checks for staging only; no COM or Windows API."""
from pathlib import Path
import json
import tempfile
import unittest

from prepare_meltia_tip_stage import (
    ROOT, ORIGINAL_GUIDS, GUID_IDENTITIES, prepare, stage_text, replace_one,
)


class MeltiaT3aStagingTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="meltia-t3a-test-")
        self.addCleanup(self.tmp.cleanup)
        self.dest = Path(self.tmp.name) / "stage"

    def test_staging_is_independent_and_nonregistrable(self):
        output = prepare(self.dest)
        native = (output / "Globals.h").read_text(encoding="utf-8")
        manifest = json.loads((ROOT / "config/tsf/meltia-t2-identity.json").read_text(encoding="utf-8"))
        for guid in manifest["identity"].values():
            self.assertIn(guid.upper(), native.upper())
        for guid in ORIGINAL_GUIDS.values():
            self.assertNotIn(guid.upper(), native.upper())
        self.assertIn('kDisplayName[] = L"Meltia"', native)
        self.assertIn('Meltia.Tip.v1.', (output / "Pipe.cpp").read_text(encoding="utf-8"))
        self.assertNotIn('L"\\\\\\\\.\\\\pipe\\\\Meltype.Tip."', (output / "Pipe.cpp").read_text(encoding="utf-8"))
        self.assertIn('return _wcsicmp(name, L"Meltia.exe") == 0;',
                      (output / "TextService.cpp").read_text(encoding="utf-8"))
        self.assertIn('LIBRARY MeltiaTip', (output / "MeltiaTip.def").read_text(encoding="utf-8"))
        self.assertNotIn('101 ICON "meltype.ico"', (output / "MeltiaTip.rc").read_text(encoding="utf-8"))
        self.assertFalse((output / "meltype.ico").exists())
        self.assertFalse((output / "Register-Tip.ps1").exists(), "no inherited installer copied")
        self.assertFalse((output / "build.ps1").exists(), "no inherited compiler script copied")
        self.assertIn("NOT INSTALLABLE", (output / "STAGING_NOTICE.txt").read_text(encoding="utf-8"))

    def test_registration_exports_return_error_without_invoking_registry(self):
        native = prepare(self.dest) / "Dll.cpp"
        staged = native.read_text(encoding="utf-8")
        self.assertIn("STDAPI DllRegisterServer() { return SELFREG_E_CLASS; }", staged)
        self.assertIn("STDAPI DllUnregisterServer() { return SELFREG_E_CLASS; }", staged)
        self.assertNotIn("STDAPI DllRegisterServer() {\n    HRESULT hr = RegisterComServer();", staged)
        self.assertNotIn("STDAPI DllUnregisterServer() {\n    RegisterCategories(false);", staged)
        self.assertIn('L"MELTIA_TIP_LOG"', staged)
        self.assertNotIn('L"MELTYPE_TIP_LOG"', staged)

    def test_original_tree_is_not_modified(self):
        path = ROOT / "native/tip/Globals.h"
        before = path.read_bytes()
        prepare(self.dest)
        self.assertEqual(before, path.read_bytes())
        self.assertIn("417D801B-A9BD-4C26-BD16-356A825A6998", before.decode("utf-8-sig").upper())

    def test_existing_staging_dir_is_rejected_without_deleting_it(self):
        self.dest.mkdir()
        marker = self.dest / "precious.txt"
        marker.write_text("do not erase", encoding="utf-8")
        with self.assertRaises(FileExistsError):
            prepare(self.dest)
        self.assertEqual("do not erase", marker.read_text(encoding="utf-8"))

    def test_repo_staging_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "outside repository"):
            prepare(ROOT / "native/tip/.t3a-staging")

    def test_source_mutation_is_rejected_not_silently_patched(self):
        manifest = json.loads((ROOT / "config/tsf/meltia-t2-identity.json").read_text(encoding="utf-8"))
        pipe = (ROOT / "native/tip/Pipe.cpp").read_text(encoding="utf-8-sig")
        mutated = pipe.replace("Meltype.Tip.", "Meltype.Tip2.")
        with self.assertRaisesRegex(ValueError, "Pipe.cpp endpoint"):
            stage_text("Pipe.cpp", mutated, manifest)
        with self.assertRaisesRegex(ValueError, "exactly one"):
            replace_one("aa", "x", "y", "explicit change")

    def test_extra_native_tail_or_modified_registration_is_rejected(self):
        manifest = json.loads((ROOT / "config/tsf/meltia-t2-identity.json").read_text(encoding="utf-8"))
        dll = (ROOT / "native/tip/Dll.cpp").read_text(encoding="utf-8-sig")
        # A newly added export after unregister must not silently disappear.
        with self.assertRaisesRegex(ValueError, "registration tail changed"):
            stage_text("Dll.cpp", dll + "\nvoid UnexpectedExport() {}\n", manifest)
        # Registration semantics changing upstream require renewed scrutiny.
        with self.assertRaisesRegex(ValueError, "registration tail changed"):
            stage_text("Dll.cpp", dll.replace("return S_OK;\n}\n", "return E_FAIL;\n}\n"), manifest)

    def test_guid_comments_match_named_fields_with_shuffled_json_order(self):
        manifest = json.loads((ROOT / "config/tsf/meltia-t2-identity.json").read_text(encoding="utf-8"))
        shuffled = dict(manifest)
        shuffled["identity"] = dict(reversed(list(manifest["identity"].items())))
        origin = (ROOT / "native/tip/Globals.h").read_text(encoding="utf-8-sig")
        native = stage_text("Globals.h", origin, shuffled)
        for constant, field in GUID_IDENTITIES:
            declaration = "inline constexpr " + ("CLSID" if constant == "CLSID_TextService" else "GUID") + " " + constant
            preceding = native[:native.index(declaration)].splitlines()[-1]
            self.assertEqual("// {" + manifest["identity"][field].upper() + "}", preceding,
                             "GUID comment must match its named initializer field: " + constant)
        for old in ORIGINAL_GUIDS.values():
            self.assertNotIn(old.upper(), native.upper())

    def test_staged_native_ui_identity_is_meltia_only(self):
        output = prepare(self.dest)
        expectations = {
            "DisplayAttributes.cpp": (
                ("Meltype: 入力中", "Meltia: 入力中"),
                ("Meltype: 変換した文節", "Meltia: 変換した文節"),
                ("Meltype: 選んでいる文節", "Meltia: 選んでいる文節"),
            ),
            "LangBar.cpp": (
                ("Meltype の入力モード", "Meltia の入力モード"),
                ("Meltype: 日本語 (クリックで英数)", "Meltia: 日本語 (クリックで英数)"),
                ("Meltype: 英数 (クリックで日本語)", "Meltia: 英数 (クリックで日本語)"),
            ),
            "CandidateWindow.cpp": (
                ("MeltypeCandidateWindow", "MeltiaCandidateWindow"),
                ("MeltypeMeaningWindow", "MeltiaMeaningWindow"),
            ),
        }
        for path, names in expectations.items():
            content = (output / path).read_text(encoding="utf-8")
            for old, new in names:
                self.assertNotIn('L"' + old + '"', content, path)
                self.assertIn('L"' + new + '"', content, path)
        manifest = json.loads((ROOT / "config/tsf/meltia-t2-identity.json").read_text(encoding="utf-8"))
        legacy = (ROOT / "native/tip/LangBar.cpp").read_text(encoding="utf-8-sig")
        with self.assertRaisesRegex(ValueError, "LangBar.cpp display"):
            stage_text("LangBar.cpp", legacy.replace("Meltype の入力モード", "Other の入力モード"), manifest)

    def test_manifest_does_not_unlock_registration(self):
        manifest = json.loads((ROOT / "config/tsf/meltia-t2-identity.json").read_text(encoding="utf-8"))
        self.assertEqual("design-only", manifest["status"])
        self.assertIs(manifest["registrationEnabled"], False)
        self.assertTrue(all(x is False for x in manifest["deployment"].values()))


if __name__ == "__main__":
    unittest.main(verbosity=2)
