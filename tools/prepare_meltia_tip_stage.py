#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T3a: generate an isolated NO-REGISTRATION native TIP build tree.

This program NEVER writes to the original native/tip, registry, installation
folders or user data. A generated DLL is not a functional Meltia IME: TSF
registration and the production Meltia host are deliberately absent.
"""
import argparse
import json
from pathlib import Path
import re
from uuid import UUID

ROOT = Path(__file__).resolve().parents[1]
ORIGIN = ROOT / "native/tip"
MANIFEST = ROOT / "config/tsf/meltia-t2-identity.json"
SOURCES = (
    "CandidateWindow.cpp", "CandidateWindow.h",
    "DisplayAttributes.cpp", "DisplayAttributes.h",
    "Dll.cpp", "Globals.h", "Json.cpp", "Json.h",
    "LangBar.cpp", "LangBar.h", "Pipe.cpp", "Pipe.h",
    "TextService.cpp", "TextService.h", "MeltypeTip.rc",
    "MeltypeTip.def",
)
ORIGINAL_GUIDS = {
    "clsid": "417D801B-A9BD-4C26-BD16-356A825A6998",
    "profileGuid": "21F643F4-72D5-4946-BF70-0A126AB52D05",
    "inputAttributeGuid": "C8107D36-2806-4D8F-A183-FD0AEB8ED862",
    "convertedAttributeGuid": "E17AA399-C5EB-42E6-BE45-C44F0651F2E2",
    "targetAttributeGuid": "CEF94813-C47F-48C1-B4EF-D4582D5D4054",
}
GUID_IDENTITIES = (
    ("CLSID_TextService", "clsid"),
    ("GUID_Profile", "profileGuid"),
    ("GUID_AttrInput", "inputAttributeGuid"),
    ("GUID_AttrConverted", "convertedAttributeGuid"),
    ("GUID_AttrTarget", "targetAttributeGuid"),
)


def replace_one(source, old, new, context):
    if source.count(old) != 1:
        raise ValueError(f"{context}: expected exactly one occurrence of {old!r}, found {source.count(old)}")
    return source.replace(old, new, 1)


def guid_initializer(value):
    guid = UUID(value)
    h = guid.hex
    numbers = ", ".join(f"0x{h[i:i + 2]}" for i in range(16, 32, 2))
    return f"{{0x{h[0:8]}, 0x{h[8:12]}, 0x{h[12:16]}, {{{numbers}}}}}"


def stage_text(name, source, manifest):
    ids = manifest["identity"]
    if name == "Globals.h":
        for key, field in GUID_IDENTITIES:
            pattern = re.compile(
                r"(inline constexpr (?:CLSID|GUID) " + key + r" = )\{[^;\n]+\}(;)")
            if len(pattern.findall(source)) != 1:
                raise ValueError(f"Globals.h: cannot find unique {key} declaration")
            source = pattern.sub(lambda m: m.group(1) + guid_initializer(ids[field]) + m.group(2), source)
        # Refer to named identity fields, never JSON object insertion order.
        for _, field in GUID_IDENTITIES:
            source = replace_one(source, ORIGINAL_GUIDS[field],
                                 ids[field].upper(), "Globals.h GUID comment")
        source = replace_one(source, 'kDisplayName[] = L"Meltype"', 'kDisplayName[] = L"Meltia"', "Globals.h display")
    elif name == "Pipe.h":
        source = replace_one(source, "#include <string>", "#include <cstdint>\n#include <string>", "Pipe.h int64")
        source = replace_one(source, "    void Close();",
                             "    void Close();\n    bool EnsureConnected() { return Connect(); }\n"
                             "    std::uint64_t ConnectionGeneration() const { return generation_; }",
                             "Pipe.h public connection generation")
        source = replace_one(source, "    HANDLE pipe_ = INVALID_HANDLE_VALUE;",
                             "    HANDLE pipe_ = INVALID_HANDLE_VALUE;\n    std::uint64_t generation_ = 0;",
                             "Pipe.h epoch state")
    elif name == "Pipe.cpp":
        # Derive the endpoint from the T2 manifest, not a second literal.
        pipe_prefix = manifest["ipc"]["pipePrefix"]
        source = replace_one(source, 'L"\\\\\\\\.\\\\pipe\\\\Meltype.Tip."',
                             'L"\\\\\\\\.\\\\pipe\\\\' + pipe_prefix + '"', "Pipe.cpp endpoint")
        source = replace_one(
            source,
            "    if (event_ == nullptr) event_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);\n    return true;",
            "    if (event_ == nullptr) event_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);\n"
            "    ++generation_;  // New accepted server connection.\n    return true;",
            "Pipe.cpp epoch increment")
    elif name == "TextService.h":
        source = replace_one(source, '#include "Pipe.h"',
                             '#include "Pipe.h"\n#include "MeltiaReconnectPolicy.h"',
                             "TextService.h reconnect policy")
        # Test access exists only in the isolated staging header and only
        # when building the no-registration T3d executable with its macro.
        source = replace_one(source,
            "private:\n    ~TextService();",
            "private:\n#ifdef MELTIA_T3D_REQUEST_TEST\n    friend struct MeltiaRequestProbe;\n#endif\n    ~TextService();",
            "TextService.h test-only Request probe")
        source = replace_one(source, "    PipeClient pipe_;",
                             "    PipeClient pipe_;\n    MeltiaReconnectPolicy handshake_;",
                             "TextService.h reconnect state")
    elif name == "TextService.cpp":
        source = replace_one(source, 'return _wcsicmp(name, L"Meltype.exe") == 0;',
                             'return _wcsicmp(name, L"Meltia.exe") == 0;', "TextService own process")
        source = replace_one(source, "kReinjectMarker = 0x4D4C5450", "kReinjectMarker = 0x4D544941",
                             "TextService replay marker")
        # Every Native request carries the version. Only a versioned response
        # can drive TSF edits, even when a peer spoofs active=true.
        request_marker = "    std::string request = json;\n"
        request_guard = (
            request_marker
            + "    if (request.empty() || request.back() != '}') return false;\n"
            + '    const bool isHello = json.rfind(R"({"op":"hello")", 0) == 0;\n'
            + '    request.insert(request.size() - 1, R"(,"version":'
            + str(manifest["protocolVersion"]) + ')");\n'
        )
        source = replace_one(source, request_marker, request_guard, "TextService request version")
        source = replace_one(
            source, "    if (!pipe_.Transact(request, response, 800)) return false;",
            '''    // A newly connected pipe starts without any authorization on the C# side.
    // Perform hello before the FIRST key/commit/select/close on that connection.
    if (!pipe_.EnsureConnected()) { serverActive_ = false; return false; }
    const auto epoch = pipe_.ConnectionGeneration();
    if (!isHello && handshake_.NeedsHello(epoch)) {
        JsonValue helloReply;
        if (!Request(R"({"op":"hello","process":)" + JsonString(ProcessName()) + "}", helloReply) ||
            !helloReply[L"active"].Bool() || pipe_.ConnectionGeneration() != epoch) {
            serverActive_ = false;
            return false;  // Never send a normal request on an unhandshaken pipe.
        }
        serverActive_ = true;
    }
    if (!pipe_.Transact(request, response, 800)) { serverActive_ = false; return false; }''',
            "TextService automatic hello before regular operations")
        parse_end = (
            '    if (!ParseJson(response, reply)) {\n'
            '        TipLog(L"応答を読めません");\n'
            '        return false;\n'
            '    }\n'
            '    return true;\n'
        )
        checked = parse_end.replace(
            '    return true;\n',
            '    const bool versionOk = reply[L"version"].type == JsonValue::Type::Number && '
            'reply[L"version"].number == ' + str(manifest["protocolVersion"]) + ';\n'
            '    if (isHello && versionOk && reply[L"active"].Bool()) '
            'handshake_.AcceptHello(pipe_.ConnectionGeneration());\n'
            '    return versionOk;\n'
        )
        source = replace_one(source, parse_end, checked, "TextService response version")

    elif name == "Dll.cpp":
        source = replace_one(source, 'L"MELTYPE_TIP_LOG"', 'L"MELTIA_TIP_LOG"', "Dll logging flag")
        source = replace_one(source, 'L"\\\\Meltype\\\\tip-log"', 'L"\\\\Meltia\\\\tip-log"', "Dll log sentinel")
        source = replace_one(source, 'L"\\\\Meltype\\\\tip.log"', 'L"\\\\Meltia\\\\tip.log"', "Dll log path")
        # Staged DLL cannot register OR unregister. Refuse any upstream change
        # to either method or any appended code rather than silently truncating.
        expected_tail = (
            "STDAPI DllRegisterServer() {\n"
            "    HRESULT hr = RegisterComServer();\n"
            "    if (SUCCEEDED(hr)) hr = RegisterProfile();\n"
            "    if (SUCCEEDED(hr)) hr = RegisterCategories(true);\n"
            "    if (FAILED(hr)) {\n"
            "        RegisterCategories(false);\n"
            "        UnregisterProfile();\n"
            "        UnregisterComServer();\n"
            "    }\n"
            "    return hr;\n"
            "}\n"
            "\n"
            "STDAPI DllUnregisterServer() {\n"
            "    RegisterCategories(false);\n"
            "    UnregisterProfile();\n"
            "    UnregisterComServer();\n"
            "    return S_OK;\n"
            "}\n"
        )
        if not source.endswith(expected_tail) or source.count(expected_tail) != 1:
            raise ValueError("Dll.cpp registration tail changed or extra source appended")
        source = source[:-len(expected_tail)] + (
            "STDAPI DllRegisterServer() { return SELFREG_E_CLASS; }\n"
            "STDAPI DllUnregisterServer() { return SELFREG_E_CLASS; }\n")
    elif name == "DisplayAttributes.cpp":
        for original in ("Meltype: 入力中", "Meltype: 変換した文節",
                         "Meltype: 選んでいる文節"):
            source = replace_one(source, 'L"' + original + '"',
                                 'L"' + original.replace("Meltype", "Meltia") + '"',
                                 "DisplayAttributes.cpp display")
    elif name == "LangBar.cpp":
        for original in ("Meltype の入力モード", "Meltype: 日本語 (クリックで英数)",
                         "Meltype: 英数 (クリックで日本語)"):
            source = replace_one(source, 'L"' + original + '"',
                                 'L"' + original.replace("Meltype", "Meltia") + '"',
                                 "LangBar.cpp display")
    elif name == "CandidateWindow.cpp":
        for original in ("MeltypeCandidateWindow", "MeltypeMeaningWindow"):
            source = replace_one(source, 'L"' + original + '"',
                                 'L"' + original.replace("Meltype", "Meltia") + '"',
                                 "CandidateWindow.cpp class")
    elif name == "MeltypeTip.rc":
        source = replace_one(source, '101 ICON "meltype.ico"', '// T3a: no inherited Meltype icon or other brand asset', "RC icon")
        source = replace_one(source, '"Meltype IME (TSF)"', '"Meltia TIP (T3a non-registrable test only)"', "RC description")
        source = replace_one(source, 'VALUE "ProductName", "Meltype"', 'VALUE "ProductName", "Meltia"', "RC product")
        source = replace_one(source, 'VALUE "CompanyName", "lnkiai"', 'VALUE "CompanyName", "Meltia contributors"', "RC manufacturer")
    elif name == "MeltypeTip.def":
        source = replace_one(source, "LIBRARY MeltypeTip", "LIBRARY MeltiaTip", "DEF library")
    return source


def prepare(output):
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    if manifest["phase"] != "T2" or manifest["status"] != "design-only" or manifest["registrationEnabled"] is not False:
        raise ValueError("T3a requires an inactive T2 identity manifest")
    if any(value is not False for value in manifest["deployment"].values()):
        raise ValueError("T3a cannot stage a registration-enabled manifest")
    # Only create fresh staging directories outside the tracked source.
    root = ROOT.resolve()
    output = output.resolve()
    if output == root or root in output.parents or output in root.parents:
        raise ValueError("staging must be outside repository and its parents")
    if output.exists():
        raise FileExistsError(f"staging destination already exists: {output}")
    output.mkdir(parents=True)
    for name in SOURCES:
        input_path = ORIGIN / name
        content = input_path.read_text(encoding="utf-8-sig")
        transformed = stage_text(name, content, manifest)
        target = ("MeltiaTip" + Path(name).suffix) if name in ("MeltypeTip.rc", "MeltypeTip.def") else name
        (output / target).write_text(transformed, encoding="utf-8")
    # Only the isolated Meltia staging tree receives this portable policy header.
    policy = ROOT / "tools/MeltiaReconnectPolicy.h"
    (output / "MeltiaReconnectPolicy.h").write_bytes(policy.read_bytes())
    # No build, registry, installer, or original Meltype artwork copied.
    (output / "STAGING_NOTICE.txt").write_text(
        "Meltia T3a experimental build staging; NOT INSTALLABLE. "
        "DllRegisterServer and DllUnregisterServer always return SELFREG_E_CLASS. "
        "T3b versioned IPC is experimental; NOT INSTALLABLE. Never distribute as an IME.\n",
        encoding="utf-8",
    )
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="new external staging directory")
    args = parser.parse_args()
    print(prepare(args.output))


if __name__ == "__main__":
    main()
