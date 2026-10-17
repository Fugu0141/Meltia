# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""Offline cross-layer *source contract* checks for inherited Meltype TIP IPC.

No named pipes, TSF registrations, keyboard hooks, user data, or Windows API.
These checks validate literal protocol wiring and fail-closed paths, not
runtime transport security or an actual native/C# interoperability test.
"""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


SERVER = "src/Meltype/Tip/TipServer.cs"
SESSION = "src/Meltype.Core/Composition/MeltypeSession.cs"
CLIENT = "native/tip/TextService.cpp"
PIPE = "native/tip/Pipe.cpp"
PIPE_HEADER = "native/tip/Pipe.h"

OPERATIONS = ("hello", "key", "commit", "select", "close")


def assert_operations_match(server, client):
    for name in OPERATIONS:
        # Actual C++ string literal, including the escaped JSON quotation marks.
        wire_op = r'\"op\":\"' + name + r'\"'
        if wire_op not in client:
            raise AssertionError("native client no longer emits op=" + name)
        if 'case "' + name + '"' not in server:
            raise AssertionError("C# server no longer handles op=" + name)


def source_section(source, start, end):
    """Limit assertions to the actual method, not a string elsewhere in a file."""
    if source.count(start) != 1 or source.count(end) != 1:
        raise AssertionError("method boundary changed: " + start)
    tail = source.split(start, 1)[1]
    if end not in tail:
        raise AssertionError("method order changed: " + start)
    return tail.split(end, 1)[0]


def require_code(statement, section, reason):
    if statement not in section:
        raise AssertionError(reason + ": " + statement)


def assert_response_fields_match(session, server, client):
    serializer = source_section(session, "public string ToJson()",
                                "private static void AppendArray(")
    handle = source_section(server, "private string Handle(string request, Dictionary<string, MeltypeSession> sessions)",
                            "private static bool IsTrue(")
    native_key = source_section(client, "void TextService::HandleKey(",
                                "void TextService::Reinject(")
    native_apply = source_section(client, "void TextService::Apply(",
                                  "bool TextService::ExtendOverPrevious(")
    native_hello = source_section(client, "void TextService::Hello(",
                                  "void TextService::ForgetSecretField(")

    # C# result writer, not arbitrary similarly named fields elsewhere.
    for field in ("consumed", "commits", "view"):
        require_code(r'\"' + field + r'\"', serializer,
                     "SessionResult.ToJson does not write " + field)
    require_code("var json = result.ToJson();", handle,
                 "TipServer.Handle must serialize the actual SessionResult")
    require_code(
        r'return $"{{\"active\":{(active ? "true" : "false")},{json[1..]}";',
        handle, "TipServer.Handle must assemble the active flag and the result envelope")
    require_code(
        r'return greeting ? "{\"active\":true,\"consumed\":false,\"commits\":[],\"view\":null}" : Inactive;',
        handle, "hello must emit the complete response envelope")
    require_code(
        r'private const string Inactive = "{\"active\":false,\"consumed\":false,\"commits\":[],\"view\":null}";',
        server, "inactive response envelope changed")

    # Native key decision fields must be read in HandleKey, not just
    # somewhere else (Hello/Apply can legitimately read active as well).
    require_code('serverActive_ = reply[L"active"].Bool();', native_key,
                 "TextService.HandleKey must read active")
    require_code('consumed_ = reply[L"consumed"].Bool();', native_key,
                 "TextService.HandleKey must read consumed")
    require_code('reply[L"active"].Bool()', native_hello,
                 "TextService.Hello must read active")
    require_code('for (const JsonValue& edit : reply[L"commits"].array)', native_apply,
                 "TextService.Apply must iterate commits")
    require_code('const JsonValue& view = reply[L"view"];', native_apply,
                 "TextService.Apply must read view")
    require_code('if (reply[L"active"].Bool()', native_apply,
                 "TextService.Apply must preserve inactive composition handling")


def assert_workflow_watches_native_tip(workflow):
    for event, section in (
        ("pull_request", source_section(workflow, "  pull_request:", "  push:")),
        ("push", source_section(workflow, "  push:", "  workflow_dispatch:")),
    ):
        require_code("- 'native/tip/**'", section,
                     event + " must check native-only IPC changes")


class MeltiaTipIpcSourceContractTests(unittest.TestCase):
    def test_five_ops_have_both_endpoints(self):
        assert_operations_match(read(SERVER), read(CLIENT))

    def test_mutating_native_operation_is_detected(self):
        client = read(CLIENT)
        original = r'\"op\":\"select\"'
        self.assertIn(original, client)
        mutant = client.replace(original, r'\"op\":\"select2\"')
        with self.assertRaisesRegex(AssertionError, "op=select"):
            assert_operations_match(read(SERVER), mutant)

    def test_session_result_envelope_reaches_native_reader(self):
        assert_response_fields_match(read(SESSION), read(SERVER), read(CLIENT))

    def test_native_only_edits_trigger_ipc_guard(self):
        workflow = read(".github/workflows/meltia-windows-preview-ci.yml")
        assert_workflow_watches_native_tip(workflow)
        # Check each trigger independently: enabling one is insufficient.
        for event, original in (
            ("pull_request", "  pull_request:"),
            ("push", "  push:"),
        ):
            with self.subTest(event=event):
                if event == "pull_request":
                    before, after = workflow.split("  push:", 1)
                else:
                    before, after = workflow.split("  push:", 1)
                    before = "  push:"
                if event == "pull_request":
                    mutant = before.replace("- 'native/tip/**'", "- 'native/missing/**'") + "  push:" + after
                else:
                    mutant = workflow.split("  push:", 1)[0] + before + after.replace(
                        "- 'native/tip/**'", "- 'native/missing/**'", 1)
                with self.assertRaisesRegex(AssertionError, event):
                    assert_workflow_watches_native_tip(mutant)

    def test_mutating_native_reader_at_key_handler_is_detected(self):
        client = read(CLIENT)
        for field, destination in (("active", "enabled"), ("consumed", "used")):
            original = f'reply[L"{field}"].Bool()'
            section = source_section(client, "void TextService::HandleKey(",
                                     "void TextService::Reinject(")
            self.assertIn(original, section)
            prefix, remainder = client.split("void TextService::HandleKey(", 1)
            method, suffix = remainder.split("void TextService::Reinject(", 1)
            mutant = (prefix + "void TextService::HandleKey(" +
                      method.replace(original, f'reply[L"{destination}"].Bool()', 1) +
                      "void TextService::Reinject(" + suffix)
            with self.subTest(field=field):
                with self.assertRaisesRegex(AssertionError, field):
                    assert_response_fields_match(read(SESSION), read(SERVER), mutant)

    def test_mutating_native_apply_commit_and_view_is_detected(self):
        client = read(CLIENT)
        for old, new, field in (
            ('reply[L"commits"].array', 'reply[L"edits"].array', "commits"),
            ('reply[L"view"]', 'reply[L"candidateView"]', "view"),
        ):
            self.assertIn(old, source_section(client, "void TextService::Apply(",
                                               "bool TextService::ExtendOverPrevious("))
            mutant = client.replace(old, new, 1)
            with self.subTest(field=field):
                with self.assertRaisesRegex(AssertionError, field):
                    assert_response_fields_match(read(SESSION), read(SERVER), mutant)

    def test_mutating_server_active_response_assembly_is_detected(self):
        server = read(SERVER)
        original = r'return $"{{\"active\":{(active ? "true" : "false")},{json[1..]}";'
        self.assertIn(original, source_section(server,
            "private string Handle(string request, Dictionary<string, MeltypeSession> sessions)",
            "private static bool IsTrue("))
        mutant = server.replace(original, original.replace(r'\"active\"', r'\"enabled\"'), 1)
        with self.assertRaisesRegex(AssertionError, "TipServer.Handle"):
            assert_response_fields_match(read(SESSION), mutant, read(CLIENT))

    def test_mutating_server_result_serializer_connection_is_detected(self):
        server = read(SERVER)
        self.assertIn("var json = result.ToJson();", server)
        mutant = server.replace("var json = result.ToJson();",
                                'var json = "{\\"consumed\\":false,\\"commits\\":[],\\"view\\":null}";', 1)
        with self.assertRaisesRegex(AssertionError, "SessionResult"):
            assert_response_fields_match(read(SESSION), mutant, read(CLIENT))

    def test_mutating_session_json_writer_is_detected(self):
        session = read(SESSION)
        old = r'\"commits\"'
        serializer = source_section(session, "public string ToJson()",
                                    "private static void AppendArray(")
        self.assertIn(old, serializer)
        mutant = session.replace(old, r'\"edits\"', 1)
        with self.assertRaisesRegex(AssertionError, "commits"):
            assert_response_fields_match(mutant, read(SERVER), read(CLIENT))

    def test_corrective_delete_keeps_expected_text_validation(self):
        session = read(SESSION)
        native = read(CLIENT)
        self.assertIn('builder.Append(', session)
        self.assertIn(r'\"expect\"', session)
        self.assertIn('ExtendOverPrevious(', native)
        self.assertIn('edit[L"expect"]', native)
        self.assertIn('length == static_cast<ULONG>(count)', native)
        self.assertIn('text.compare(0, length, expect.Str()) == 0', native)

    def test_bounds_and_fail_closed_path_are_still_present(self):
        server = read(SERVER)
        pipe = read(PIPE)
        native = read(CLIENT)
        self.assertIn('MaxRequestBytes = 64 * 1024', server)
        self.assertIn('MaxSessionsPerConnection = 16', server)
        self.assertIn('message.Length > MaxRequestBytes', server)
        self.assertIn('sessions.Count >= MaxSessionsPerConnection', server)
        self.assertIn('kMaxResponse = 1024 * 1024', read(PIPE_HEADER))
        self.assertIn('response.size() > kMaxResponse', pipe)
        self.assertIn('pipe_.Transact(request, response, 800)', native)
        self.assertIn('private const string Inactive', server)
        self.assertIn('reply = Inactive', server)
        self.assertIn('if (!Request(request, reply))', native)
        self.assertIn('serverActive_ = false', native)
        self.assertIn('EndComposition(ec)', native)
        self.assertIn('if ((keyFailed_ && !insertedOnFailure) || !consumed_) Reinject(vk)', native)

    def test_session_sid_moved_and_composing_guards_still_present(self):
        server = read(SERVER)
        native = read(CLIENT)
        self.assertIn('sessions.TryGetValue(sid, out var session)', server)
        self.assertIn('IsTrue(root, "composing")', server)
        self.assertIn('IsTrue(root, "moved")', server)
        self.assertIn('session.ForgetLastCommit()', server)
        self.assertIn('if (caretMoved_) request +=', native)
        self.assertIn(r'\"composing\":true', native)

    def test_sample_wire_payloads_are_utf8_json_without_live_ipc(self):
        cases = (
            {"op": "hello", "process": "notepad"},
            {"op": "key", "sid": "example-session", "vk": 65, "ch": 97,
             "mods": 1, "before": "日本語😀", "after": "", "moved": True,
             "composing": False, "process": "notepad"},
            {"op": "commit", "sid": "example-session", "moved": True},
            {"op": "select", "sid": "example-session", "index": 2},
            {"op": "close", "sid": "example-session"},
        )
        for case in cases:
            with self.subTest(op=case["op"]):
                raw = json.dumps(case, ensure_ascii=False,
                                 separators=(",", ":")).encode("utf-8")
                self.assertLessEqual(len(raw), 64 * 1024)
                decoded = json.loads(raw.decode("utf-8"))
                self.assertEqual(case, decoded)


if __name__ == "__main__":
    unittest.main(verbosity=2)
