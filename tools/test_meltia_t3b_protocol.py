#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Static and mutation checks for the isolated, unregistered T3b IPC source."""
import json
import unittest
from prepare_meltia_tip_stage import ROOT, MANIFEST, stage_text
from generate_meltia_tip_identity import DEST, render


class T3bContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        cls.cpp = (ROOT / "native/tip/TextService.cpp").read_text(encoding="utf-8-sig")

    def test_csharp_constants_are_generated_from_manifest(self):
        self.assertEqual(DEST.read_text(encoding="utf-8"), render(self.manifest))

    def test_native_request_and_response_versions_are_checked(self):
        native = stage_text("TextService.cpp", self.cpp, self.manifest)
        v = self.manifest["protocolVersion"]
        self.assertIn('R"(,"version":' + str(v) + ')"', native)
        self.assertIn('reply[L"version"].type == JsonValue::Type::Number', native)
        self.assertIn('reply[L"version"].number == ' + str(v), native)
        self.assertIn('request.insert(request.size() - 1,', native)

    def test_pipe_comes_from_manifest(self):
        original = (ROOT / "native/tip/Pipe.cpp").read_text(encoding="utf-8-sig")
        staged = stage_text("Pipe.cpp", original, self.manifest)
        self.assertIn(self.manifest["ipc"]["pipePrefix"], staged)
        self.assertIn("Meltype.Tip.", original)
        self.assertNotIn("Meltype.Tip.", staged.split("std::wstring PipeName()", 1)[1])

    def test_native_rejects_unrecognised_upstream_shape(self):
        modified = self.cpp.replace("    std::string request = json;", "    auto request = json;")
        with self.assertRaisesRegex(ValueError, "TextService request version"):
            stage_text("TextService.cpp", modified, self.manifest)
        modified = self.cpp.replace("    return true;\n}\n\nvoid TextService::SurroundingText",
                                    "    return false;\n}\n\nvoid TextService::SurroundingText")
        with self.assertRaisesRegex(ValueError, "TextService response version"):
            stage_text("TextService.cpp", modified, self.manifest)

    def test_legacy_native_endpoint_remains_unmodified(self):
        self.assertIn("Meltype.Tip.", (ROOT / "native/tip/Pipe.cpp").read_text(encoding="utf-8-sig"))
        self.assertIn('L"Meltype.exe"', self.cpp)

    def test_registration_and_preview_remain_disabled(self):
        native = stage_text("Dll.cpp",
            (ROOT / "native/tip/Dll.cpp").read_text(encoding="utf-8-sig"), self.manifest)
        self.assertIn("STDAPI DllRegisterServer() { return SELFREG_E_CLASS; }", native)
        self.assertIn("STDAPI DllUnregisterServer() { return SELFREG_E_CLASS; }", native)
        self.assertFalse(self.manifest["deployment"]["enableTsfMode"])
        self.assertFalse(self.manifest["deployment"]["performRegistration"])

    def test_managed_server_uses_separate_per_connection_wire_gate(self):
        server = (ROOT / "src/Meltype/Tip/TipServer.cs").read_text(encoding="utf-8")
        self.assertIn("new MeltiaTipWire()", server)
        self.assertIn("wire.Process(request,", server)
        self.assertIn("NamedPipeServerStreamAcl.Create(EndpointName,", server)
        self.assertIn("meltiaExperimental: false", server)
        self.assertIn("MeltiaTipIdentity.PipePrefix", server)


if __name__ == "__main__":
    unittest.main()
