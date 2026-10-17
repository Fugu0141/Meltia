# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T2 offline validation of proposed Meltia TSF identities (NO registration).

Checks JSON data and source-tree references only; no Windows registry, COM,
named-pipe, installer, regsvr32, or OS state is read or modified.
"""
import json
from pathlib import Path
import re
import unittest
from uuid import UUID, RFC_4122

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "config/tsf/meltia-t2-identity.json"
# The known original CLSID is explicitly checked in these two files.
LEGACY_SOURCES = (
    ROOT / "native/tip/Globals.h",
    ROOT / "src/Meltype/Tip/TipServer.cs",
)
# Scan all current inherited TIP implementation, registration scripts and C#
# TIP server files for literal GUIDs. This is NOT a repo-wide or OS registry scan.
LEGACY_GUID_SCAN_FILES = tuple(sorted(
    set((ROOT / "native/tip").glob("*.h"))
    | set((ROOT / "native/tip").glob("*.cpp"))
    | set((ROOT / "native/tip").glob("*.ps1"))
    | set((ROOT / "src/Meltype/Tip").glob("*.cs"))
))
LEGACY_CATEGORY_REGISTRATION = ROOT / "native/tip/Dll.cpp"
GUID_KEYS = (
    "clsid", "profileGuid", "inputAttributeGuid",
    "convertedAttributeGuid", "targetAttributeGuid",
)
GUID_RE = re.compile(
    r"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}"
    r"-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b"
)


def read_manifest():
    return json.loads(MANIFEST.read_text(encoding="utf-8"))


def legacy_guid_set():
    """Literal GUIDs in native/tip C++/headers/scripts and C# Tip server files."""
    if not LEGACY_GUID_SCAN_FILES:
        raise ValueError("TIP GUID scan source set is empty")
    return {
        UUID(value) for path in LEGACY_GUID_SCAN_FILES
        for value in GUID_RE.findall(path.read_text(encoding="utf-8-sig"))
    }


def inherited_category_associations():
    """Category identifiers paired with the original CLSID by Dll.cpp."""
    source = LEGACY_CATEGORY_REGISTRATION.read_text(encoding="utf-8-sig")
    match = re.search(r"const GUID\* const kCategories\[\]\s*=\s*\{(.*?)\};",
                      source, re.DOTALL)
    if match is None:
        raise ValueError("cannot identify inherited TSF category list")
    categories = re.findall(r"&([A-Z][A-Z0-9_]*)\s*,", match.group(1))
    if not categories or len(set(categories)) != len(categories):
        raise ValueError("inherited TSF category list is empty or duplicated")
    return categories


def validate_identity(data, legacy_guids):
    if data.get("schemaVersion") != 1 or data.get("phase") != "T2":
        raise ValueError("manifest schemaVersion/phase not approved")
    if data.get("status") != "design-only" or data.get("registrationEnabled") is not False:
        raise ValueError("T2 registration must stay disabled")
    if data.get("protocolVersion") != 1:
        raise ValueError("protocolVersion must start at 1")
    if data.get("product") != "Meltia" or data.get("displayName") != "Meltia":
        raise ValueError("Meltia-only product/display name required")
    if data.get("languageId") != "0x0411":
        raise ValueError("T2 is the Japanese language profile")

    ids = data.get("identity")
    if not isinstance(ids, dict) or set(ids) != set(GUID_KEYS):
        raise ValueError("exactly five TSF GUIDs are required")
    parsed = []
    for key in GUID_KEYS:
        try:
            value = ids[key]
            guid = UUID(value)
        except (ValueError, TypeError, AttributeError) as exc:
            raise ValueError(f"invalid {key} GUID") from exc
        if str(guid) != value or guid.version != 4 or guid.variant != RFC_4122:
            raise ValueError(f"{key} must be a canonical random UUIDv4")
        parsed.append(guid)
    if len(set(parsed)) != len(GUID_KEYS):
        raise ValueError("duplicate Meltia TSF GUID")
    if any(guid in legacy_guids for guid in parsed):
        raise ValueError("Meltia GUID collides with inherited Meltype/Windows identity")

    ipc = data.get("ipc", {})
    if ipc.get("pipePrefix") != "Meltia.Tip.v1." or ipc.get("userSuffix") != "<SID>":
        raise ValueError("independent versioned SID-scoped pipe name required")
    if ipc.get("wireContract") != "T1-B" or ipc.get("requireVersionHandshake") is not True:
        raise ValueError("T1-B IPC contract with version gate is required")
    if any(ipc.get(key) != "inactive" for key in
           ("unknownVersionPolicy", "unknownOperationPolicy")):
        raise ValueError("unknown operations/versions must fail closed")

    dst = data.get("distribution", {})
    expected_paths = {
        "assemblyBaseName": "MeltiaTip",
        "installDirectory": r"%ProgramFiles%\Meltia\tip",
        "dataDirectory": r"%LOCALAPPDATA%\Meltia",
        "logDirectory": r"%LOCALAPPDATA%\Meltia\logs",
        "uninstallScope": (
            "Meltia-owned COM CLSID, language profile, TSF category associations, "
            "and installed files only"
        ),
    }
    for key, value in expected_paths.items():
        if dst.get(key) != value:
            raise ValueError(f"Meltia-only distribution.{key} required")

    ownership = dst.get("uninstallOwnership", {})
    if not isinstance(ownership, dict):
        raise ValueError("uninstallOwnership requires explicit limits")
    expected_ownership = {
        "comClsid": "identity.clsid",
        "languageProfile": "identity.profileGuid",
        "categoryRegistrationOwner": "identity.clsid",
        "categoryAssociations": inherited_category_associations(),
        "installedFileRoot": dst["installDirectory"],
        "removeSharedCategoryGuids": False,
        "removeForeignProductRegistrations": False,
        "removeUserDataAutomatically": False,
    }
    if set(ownership) != set(expected_ownership):
        raise ValueError("uninstallOwnership fields must enumerate exactly the owned resources")
    for key, expected in expected_ownership.items():
        actual = ownership.get(key)
        # 'False' is mandatory; 0/None/"false" must not pass as a boolean.
        valid = actual is False if expected is False else actual == expected
        if not valid:
            raise ValueError(f"uninstallOwnership.{key} must preserve Meltia-only ownership")

    deployment = data.get("deployment", {})
    for flag in ("performRegistration", "modifyMeltypeRegistration",
                 "enableTsfMode", "copyNativeArtifacts"):
        if deployment.get(flag) is not False:
            raise ValueError(f"registration/activation guard {flag} must be false")


class MeltiaT2IdentityContractTests(unittest.TestCase):
    def setUp(self):
        self.data = read_manifest()
        self.legacy = legacy_guid_set()

    def test_manifest_is_isolated_and_never_activates_tsf(self):
        validate_identity(self.data, self.legacy)

    def test_generated_ids_do_not_collide_with_known_upstream(self):
        ids = {UUID(value) for value in self.data["identity"].values()}
        self.assertEqual(5, len(ids))
        self.assertFalse(ids & self.legacy)

    def test_legacy_code_still_has_its_original_native_and_csharp_guid(self):
        # T2 is a plan and must not silently replace upstream binary wiring.
        for path in LEGACY_SOURCES:
            source = path.read_text(encoding="utf-8-sig").lower()
            self.assertIn("417d801b-a9bd-4c26-bd16-356a825a6998", source)
        native = LEGACY_SOURCES[0].read_text(encoding="utf-8-sig").lower()
        self.assertIn("21f643f4-72d5-4946-bf70-0a126ab52d05", native)

    def test_guid_scan_includes_native_registration_and_server_sources(self):
        # Register-Tip.ps1 contains additional literal GUID references that the
        # previous two-file scan could miss. This is scoped, not a repo-wide scan.
        paths = {p.relative_to(ROOT).as_posix() for p in LEGACY_GUID_SCAN_FILES}
        for path in (
            "native/tip/Globals.h",
            "native/tip/Dll.cpp",
            "native/tip/Register-Tip.ps1",
            "src/Meltype/Tip/TipServer.cs",
        ):
            self.assertIn(path, paths)
        self.assertGreaterEqual(len(paths), 4)

    def test_uninstall_categories_match_inherited_registration_targets(self):
        owning = self.data["distribution"]["uninstallOwnership"]
        self.assertEqual(inherited_category_associations(), owning["categoryAssociations"])
        self.assertEqual("identity.clsid", owning["categoryRegistrationOwner"])
        self.assertFalse(owning["removeSharedCategoryGuids"])
        self.assertFalse(owning["removeForeignProductRegistrations"])

    def test_mutating_uninstall_targets_or_cleanup_scope_is_rejected(self):
        import copy
        mutations = (
            ("categoryAssociations", []),
            ("categoryRegistrationOwner", "Meltype CLSID"),
            ("comClsid", "legacy CLSID"),
            ("languageProfile", "legacy language profile"),
            ("installedFileRoot", r"%ProgramFiles%\\Meltype\\tip"),
            ("removeSharedCategoryGuids", True),
            ("removeForeignProductRegistrations", True),
            ("removeUserDataAutomatically", True),
        )
        for field, value in mutations:
            bad = copy.deepcopy(self.data)
            bad["distribution"]["uninstallOwnership"][field] = value
            with self.subTest(field=field), self.assertRaisesRegex(
                ValueError, "uninstallOwnership"
            ):
                validate_identity(bad, self.legacy)
        bad = copy.deepcopy(self.data)
        bad["distribution"]["uninstallScope"] = "Meltia CLSID and profile only"
        with self.assertRaisesRegex(ValueError, "Meltia-only distribution.uninstallScope"):
            validate_identity(bad, self.legacy)

    def test_duplicate_or_upstream_guid_is_rejected(self):
        import copy
        dup = copy.deepcopy(self.data)
        dup["identity"]["profileGuid"] = dup["identity"]["clsid"]
        with self.assertRaisesRegex(ValueError, "duplicate"):
            validate_identity(dup, self.legacy)

        colliding = copy.deepcopy(self.data)
        colliding["identity"]["clsid"] = "417d801b-a9bd-4c26-bd16-356a825a6998"
        with self.assertRaisesRegex(ValueError, "collides"):
            validate_identity(colliding, self.legacy)

    def test_wrong_phase_or_activation_is_rejected(self):
        import copy
        for field, value in (
            ("registrationEnabled", True),
            ("status", "production"),
        ):
            bad = copy.deepcopy(self.data)
            bad[field] = value
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, "disabled"):
                validate_identity(bad, self.legacy)
        for flag in self.data["deployment"]:
            bad = copy.deepcopy(self.data)
            bad["deployment"][flag] = True
            with self.subTest(flag=flag), self.assertRaisesRegex(ValueError, "guard"):
                validate_identity(bad, self.legacy)

    def test_pipe_or_installation_namespace_regression_is_rejected(self):
        import copy
        for key, value in (
            ("pipePrefix", "Meltype.Tip."),
            ("userSuffix", ""),
            ("requireVersionHandshake", False),
            ("unknownVersionPolicy", "ignore"),
        ):
            bad = copy.deepcopy(self.data)
            bad["ipc"][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                validate_identity(bad, self.legacy)

        for key, value in (
            ("installDirectory", r"%ProgramFiles%\Meltype\tip"),
            ("dataDirectory", r"%LOCALAPPDATA%\Meltype"),
            ("assemblyBaseName", "MeltypeTip"),
        ):
            bad = copy.deepcopy(self.data)
            bad["distribution"][key] = value
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "Meltia-only"):
                validate_identity(bad, self.legacy)


if __name__ == "__main__":
    unittest.main(verbosity=2)
