# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""Source-only regression guard for the *Keyboard Preview*, not a live TSF test.

This test deliberately does NOT load/register native TIPs, start any input
hooks, modify the registry or touch user configuration. It establishes only
structural invariants that must survive upstream Meltype source refactoring.
"""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def source(relative_path):
    return (ROOT / relative_path).read_text(encoding="utf-8-sig")


def preview_active(text):
    """Interpret the MELTIA_PREVIEW conditionals relevant to shared Program.cs.

    Unrecognised #if symbols are rejected, rather than silently granting a
    false sense that the guard exercises the actual Preview compilation path.
    """
    frames = []
    active = True
    kept = []
    for line in text.splitlines():
        match = re.fullmatch(r"\s*#(if|elif|else|endif)\b\s*(.*)", line)
        if match is None:
            if active:
                kept.append(line)
            continue
        action, expression = match.groups()
        if action == "if":
            if expression not in ("MELTIA_PREVIEW", "!MELTIA_PREVIEW"):
                raise AssertionError("unknown preprocessor symbol: " + expression)
            cond = expression == "MELTIA_PREVIEW"
            frames.append((active, cond))
            active = active and cond
        elif action == "else":
            if not frames or expression.strip():
                raise AssertionError("invalid #else")
            parent, branch = frames[-1]
            frames[-1] = (parent, not branch)
            active = parent and not branch
        elif action == "elif":
            raise AssertionError("unsupported #elif")
        elif action == "endif":
            if not frames or expression.strip():
                raise AssertionError("invalid #endif")
            active, _ = frames.pop()
    if frames:
        raise AssertionError("unclosed #if")
    return "\n".join(kept)


class KeyboardPreviewIsolationContractTests(unittest.TestCase):
    def test_preview_is_distinct_windows_artifact_without_native_tip(self):
        project = ET.fromstring(source("src/Meltia.Windows/Meltia.Windows.csproj"))
        props = {n.tag: (n.text or "").strip()
                 for group in project.findall("PropertyGroup") for n in group}
        self.assertEqual(props.get("AssemblyName"), "Meltia")
        self.assertIn("MELTIA_PREVIEW", props.get("DefineConstants", ""))
        self.assertEqual(props.get("OutputType"), "WinExe")
        compile_paths = [node.attrib["Include"] for node in project.findall(".//Compile")]
        self.assertIn("../Meltype/**/*.cs", compile_paths)
        self.assertFalse(any("native/" in p.lower() or "register-tip" in p.lower()
                             for p in compile_paths))
        refs = [n.attrib["Include"] for n in project.findall(".//ProjectReference")]
        self.assertTrue(any("Meltia.CompositionBridge" in p for p in refs))
        self.assertTrue(any("Meltia.Core" in p for p in refs))

    def test_program_forces_keyboard_and_disables_upstream_auto_update(self):
        raw = source("src/Meltype/Program.cs")
        program = preview_active(raw)
        self.assertIn("MeltiaPreviewSafety.Initialize();", program)
        self.assertLess(program.index("MeltiaPreviewSafety.Initialize();"),
                        program.index("AppPaths.DataDirectory"))
        self.assertIn("settings.Mode = InputMode.Keyboard;", program)
        self.assertIn("settings.AutoUpdate = false;", program)
        self.assertNotIn("Updater.ApplyStagedAtStartup(", program)
        self.assertNotIn("AppPaths.MigrateFromOldName(", program)
        self.assertIn("MeltiaPreviewSelfTest.Run(", program)

    def test_startup_entry_is_not_meltype(self):
        startup = preview_active(source("src/Meltype/Startup.cs"))
        self.assertRegex(startup, r'ValueName\s*=\s*"MeltiaPreview"')
        self.assertIn('"MeltiaPreview.lnk"', startup)
        self.assertNotRegex(startup, r'ValueName\s*=\s*"Meltype"')
        self.assertNotIn('"Meltype.lnk"', startup)

    def test_preview_data_directory_explicitly_refuses_meltype_location(self):
        safety = source("src/Meltia.Windows/MeltiaPreviewSafety.cs")
        self.assertIn('Path.Combine(local, "Meltia", "Preview")', safety)
        self.assertIn('Path.Combine(local, "Meltype")', safety)
        self.assertIn("Path.GetFullPath(directory)", safety)
        self.assertIn("normalized.StartsWith(normalizedMeltype", safety)
        self.assertIn("StringComparison.OrdinalIgnoreCase", safety)
        self.assertIn("throw new InvalidOperationException(", safety)
        self.assertIn('Environment.SetEnvironmentVariable("MELTYPE_DATA_DIR"', safety)

    def test_native_tip_is_not_a_preview_install_step(self):
        # Meltype's original native code deliberately remains in the source
        # tree, but the Preview *packaging* must not register or ship it.
        script = source("tools/build-meltia-windows-preview.ps1")
        self.assertNotRegex(script, r"(?i)(?<![a-z])regsvr32(?:\.exe)?\b")
        self.assertNotIn("Register-Tip.ps1", script)
        self.assertNotIn("MeltypeTip.dll", script)
        self.assertNotRegex(script, r"(?i)\bsetup-ime\.ps1\b")
        project = source("src/Meltia.Windows/Meltia.Windows.csproj")
        self.assertNotIn("native/tip/", project.lower())


if __name__ == "__main__":
    unittest.main(verbosity=2)
