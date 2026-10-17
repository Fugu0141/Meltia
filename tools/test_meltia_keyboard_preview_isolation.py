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


def csharp_effective_source(text, *, strip_literals=False):
    """Blank C# comments while preserving offsets/newlines.

    Literal masking is used for executable-code assertions so a quoted fake
    assignment cannot satisfy a guard. This is a narrow source-level check,
    not a substitute for a C# compiler/semantic analysis.
    """
    result = list(text)
    length = len(text)

    def blank(start, end):
        for index in range(start, end):
            if text[index] not in "\r\n":
                result[index] = " "

    i = 0
    while i < length:
        if text.startswith("//", i):
            end = text.find("\n", i)
            if end < 0:
                end = length
            blank(i, end)
            i = end
            continue
        if text.startswith("/*", i):
            end = text.find("*/", i + 2)
            if end < 0:
                raise AssertionError("unclosed C# block comment")
            end += 2
            blank(i, end)
            i = end
            continue
        # Preserve raw quoted strings as single literals. Quoted text is not
        # executable C# and must never be allowed to satisfy a guard.
        if text.startswith('"""', i):
            n = 3
            while i + n < length and text[i + n] == '"':
                n += 1
            end = text.find('"' * n, i + n)
            if end < 0:
                raise AssertionError("unclosed raw C# string")
            end += n
            if strip_literals:
                blank(i, end)
            i = end
            continue
        if text[i] in ("'", '"'):
            quote = text[i]
            verbatim = quote == '"' and i > 0 and text[i - 1] == "@"
            end = i + 1
            while end < length:
                if verbatim and text.startswith('""', end):
                    end += 2
                elif not verbatim and text[end] == "\\":
                    end += 2
                elif text[end] == quote:
                    end += 1
                    break
                else:
                    end += 1
            else:
                raise AssertionError("unclosed C# literal")
            if strip_literals:
                blank(i, min(end, length))
            i = end
            continue
        i += 1
    return "".join(result)


def assert_preview_program_safe(raw):
    """Fail if a critical Preview guard is missing or in a comment/string."""
    code = csharp_effective_source(preview_active(raw), strip_literals=True)
    initialize = re.search(
        r"\bMeltiaPreviewSafety\s*\.\s*Initialize\s*\(\s*\)\s*;", code
    )
    if initialize is None:
        raise AssertionError("Preview safety initialization is not executable")

    # Any AppPaths member can trigger static DataDirectory initialization,
    # not just AppPaths.DataDirectory. Check all executable references.
    if re.search(r"\bAppPaths\s*\.", code[:initialize.start()]):
        raise AssertionError("AppPaths accessed before Meltia Preview safety init")

    # The later "TIP is unregistered" fallback also assigns Keyboard, but
    # cannot substitute for the unconditional Preview setting. Require the
    # assignments in the initialization window after settings are loaded and
    # before they can be persisted or used to start the engine.
    load = re.search(
        r"\bvar\s+settings\s*=\s*Settings\s*\.\s*Load\s*\(\s*"
        r"AppPaths\s*\.\s*ConfigFile\s*\)\s*;", code
    )
    first_save = re.search(
        r"\bif\s*\(\s*!\s*File\s*\.\s*Exists\s*\(\s*"
        r"AppPaths\s*\.\s*ConfigFile\s*\)\s*\)", code
    )
    if load is None or first_save is None or first_save.start() <= load.end():
        raise AssertionError("Preview settings initialization window changed")
    initialization = code[load.end():first_save.start()]
    for pattern in (
        r"\bsettings\s*\.\s*Mode\s*=\s*InputMode\s*\.\s*Keyboard\s*;",
        r"\bsettings\s*\.\s*AutoUpdate\s*=\s*false\s*;",
    ):
        if not re.search(pattern, initialization):
            raise AssertionError("Missing executable Preview guard: " + pattern)
    if not re.search(r"\bMeltiaPreviewSelfTest\s*\.\s*Run\s*\(", code):
        raise AssertionError("Missing executable Preview self-test entry")
    forbidden = (
        r"\bUpdater\s*\.\s*ApplyStagedAtStartup\s*\(",
        r"\bAppPaths\s*\.\s*MigrateFromOldName\s*\(",
    )
    for pattern in forbidden:
        if re.search(pattern, code):
            raise AssertionError("Unsafe Preview operation: " + pattern)


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
        assert_preview_program_safe(source("src/Meltype/Program.cs"))

    def test_mutation_commenting_out_keyboard_assignment_is_rejected(self):
        original = source("src/Meltype/Program.cs")
        assignment = "settings.Mode = InputMode.Keyboard;"
        self.assertIn(assignment, original)
        for commented in ("// " + assignment, "/* " + assignment + " */"):
            with self.subTest(commented=commented):
                mutant = original.replace(assignment, commented, 1)
                with self.assertRaisesRegex(AssertionError, "Missing executable"):
                    assert_preview_program_safe(mutant)

    def test_mutation_commented_out_updater_guard_and_init_are_rejected(self):
        original = source("src/Meltype/Program.cs")
        for statement in ("settings.AutoUpdate = false;",
                          "MeltiaPreviewSafety.Initialize();"):
            self.assertIn(statement, original)
            for commented in ("// " + statement, "/* " + statement + " */"):
                with self.subTest(statement=statement, commented=commented):
                    mutant = original.replace(statement, commented, 1)
                    with self.assertRaises(AssertionError):
                        assert_preview_program_safe(mutant)

    def test_mutation_any_early_apppaths_member_is_rejected(self):
        original = source("src/Meltype/Program.cs")
        init = "MeltiaPreviewSafety.Initialize();"
        self.assertIn(init, original)
        for path in ("AppPaths.ConfigFile", "AppPaths.ModelFile",
                     "AppPaths.DataDirectory"):
            with self.subTest(path=path):
                mutant = original.replace(init,
                    "var earlyPath = " + path + ";\n        " + init, 1)
                with self.assertRaisesRegex(AssertionError, "before Meltia"):
                    assert_preview_program_safe(mutant)

    def test_mutation_fake_guards_inside_literals_are_rejected(self):
        original = source("src/Meltype/Program.cs")
        assignment = "settings.Mode = InputMode.Keyboard;"
        self.assertIn(assignment, original)
        # String literals, including comments-looking text, are not code.
        mutant = original.replace(assignment,
            '// ' + assignment + '\n        '
            'string fake = "settings.Mode = InputMode.Keyboard;";', 1)
        with self.assertRaisesRegex(AssertionError, "Missing executable"):
            assert_preview_program_safe(mutant)

    def test_comments_and_strings_do_not_trigger_apppaths_false_positives(self):
        original = source("src/Meltype/Program.cs")
        init = "MeltiaPreviewSafety.Initialize();"
        with_comments = original.replace(init,
            "// AppPaths.ConfigFile\n        "
            "/* AppPaths.ModelFile */\n        "
            'var fake = "AppPaths.DataDirectory";\n        ' + init, 1)
        assert_preview_program_safe(with_comments)

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
