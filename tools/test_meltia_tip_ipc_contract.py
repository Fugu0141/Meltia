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


def assert_response_fields_match(session, server, client):
    for field in ("consumed", "commits", "view"):
        if r'\"' + field + r'\"' not in session:
            raise AssertionError("SessionResult no longer emits " + field)
    for field in ("active", "consumed", "commits", "view"):
        if field not in server:
            raise AssertionError("server no longer defines response field " + field)
        if 'L"' + field + '"' not in client:
            # Native reads view fields in different handlers; active/consumed
            # in HandleKey and commits/view in Apply.
            raise AssertionError("native client no longer reads " + field)


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

    def test_mutated_envelope_field_is_detected(self):
        client = read(CLIENT)
        original = 'reply[L"consumed"]'
        self.assertIn(original, client)
        mutant = client.replace(original, 'reply[L"used"]')
        with self.assertRaisesRegex(AssertionError, "consumed"):
            assert_response_fields_match(read(SESSION), read(SERVER), mutant)

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
