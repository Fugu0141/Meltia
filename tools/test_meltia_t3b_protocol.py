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
        self.assertNotEqual(DEST.parent, ROOT / "src/Meltype/Tip",
                            "generated Meltia GUIDs must stay out of the inherited T2 source scan")

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

    def test_native_epoch_guarantees_hello_before_reconnected_key_or_commit(self):
        header = (ROOT / "native/tip/Pipe.h").read_text(encoding="utf-8-sig")
        pipe = (ROOT / "native/tip/Pipe.cpp").read_text(encoding="utf-8-sig")
        text_header = (ROOT / "native/tip/TextService.h").read_text(encoding="utf-8-sig")
        staged_header = stage_text("Pipe.h", header, self.manifest)
        staged_pipe = stage_text("Pipe.cpp", pipe, self.manifest)
        staged_text_header = stage_text("TextService.h", text_header, self.manifest)
        staged_text = stage_text("TextService.cpp", self.cpp, self.manifest)
        self.assertIn("std::uint64_t ConnectionGeneration() const", staged_header)
        self.assertIn("++generation_;", staged_pipe)
        self.assertIn("MeltiaReconnectPolicy handshake_", staged_text_header)
        self.assertIn("handshake_.NeedsHello(epoch)", staged_text)
        self.assertIn("handshake_.AcceptHello(pipe_.ConnectionGeneration())", staged_text)
        self.assertIn("serverActive_ = false", staged_text)
        # Request must check the new connection, and complete hello, before
        # any normal request can reach the transport.
        start = staged_text.index("bool TextService::Request(")
        stop = staged_text.index("void TextService::SurroundingText(", start)
        method = staged_text[start:stop]
        self.assertLess(method.index("pipe_.EnsureConnected()"),
                        method.index("handshake_.NeedsHello(epoch)"))
        self.assertLess(method.index("handshake_.NeedsHello(epoch)"),
                        method.index("pipe_.Transact(request, response, 800)"))
        self.assertIn('if (!isHello && handshake_.NeedsHello(epoch))', method)
        self.assertIn('if (!Request(R"({"op":"hello","process":)"', method)
        self.assertIn("return false;  // Never send a normal request", method)

    def test_native_epoch_mutations_fail_stage_instead_of_silently_bypassing(self):
        src_pipe = (ROOT / "native/tip/Pipe.cpp").read_text(encoding="utf-8-sig")
        changed = src_pipe.replace("    return true;\n}\n\nvoid PipeClient::Close()",
                                   "    return false;\n}\n\nvoid PipeClient::Close()")
        self.assertNotEqual(src_pipe, changed)
        with self.assertRaisesRegex(ValueError, "Pipe.cpp epoch increment"):
            stage_text("Pipe.cpp", changed, self.manifest)
        changed_text = self.cpp.replace("    if (!pipe_.Transact(request, response, 800)) return false;",
                                       "    if (!pipe_.Transact(request, response, 801)) return false;")
        self.assertNotEqual(self.cpp, changed_text)
        with self.assertRaisesRegex(ValueError, "TextService automatic hello"):
            stage_text("TextService.cpp", changed_text, self.manifest)

    def test_t3d_request_probe_is_test_macro_only(self):
        original = (ROOT / "native/tip/TextService.h").read_text(encoding="utf-8-sig")
        staged = stage_text("TextService.h", original, self.manifest)
        self.assertNotIn("MeltiaRequestProbe", original)
        self.assertIn("#ifdef MELTIA_T3D_REQUEST_TEST", staged)
        self.assertIn("friend struct MeltiaRequestProbe;", staged)
        self.assertIn("MeltiaReconnectPolicy handshake_", staged)

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
