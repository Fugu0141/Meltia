# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Fugu0141
"""Synthetic-only negative tests: no real text or sealed holdout data."""
import copy
from contextlib import redirect_stderr
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from tools.validate_h11_holdout import (
    HoldoutValidationError,
    known_issue_raw,
    read_jsonl,
    main,
    validate_rows,
)


def row(case_id, split, raw, source_id, spans):
    return {
        "case_id": case_id,
        "split": split,
        "raw": raw,
        "intents": [{"label_id": "intent-1", "english_spans": spans}],
        "source": {
            "source_id": source_id,
            "kind": "licensed-public",
            "license_or_consent": "synthetic-demo-only",
            "collected_utc": "2026-10-17T00:00:00Z",
        },
        "tags": {
            "english_dictionary_hit": False,
            "explicit_english": False,
            "japanese_protection": False,
            "seam": True,
            "compound": False,
            "script_group": "mixed-latin",
        },
        "annotation": {
            "adjudicated": True,
            "independent_submissions": [
                {
                    "annotator_id": "independent-1",
                    "intents": [{"label_id": "ind-a", "english_spans": copy.deepcopy(spans)}],
                },
                {
                    "annotator_id": "independent-2",
                    "intents": [{"label_id": "ind-b", "english_spans": copy.deepcopy(spans)}],
                },
            ],
            "adjudication": {
                "status": "complete", "method": "agreement",
                "reason": "Matching synthetic intent",
                "recorded_utc": "2026-10-17T00:00:00Z",
            },
        },
    }


class H11HoldoutValidatorTests(unittest.TestCase):
    def setUp(self):
        self.data = [
            row("example-dev", "dev", "🌸GitHub", "source-a", [[2, 8]]),
            row("example-test", "test", "nihongo🌸OpenAI", "source-b", [[9, 15]]),
        ]

    def audit(self, data=None, **kwargs):
        return validate_rows(self.data if data is None else data,
                             reference_raw=set(), allow_synthetic=True, **kwargs)

    def reject(self, change):
        data = copy.deepcopy(self.data)
        change(data)
        with self.assertRaises(HoldoutValidationError):
            self.audit(data)

    def test_valid_synthetic_split_and_non_bmp_offsets(self):
        self.assertEqual({"dev": 1, "test": 1, "total": 2}, self.audit())

    def test_multiple_different_intents_for_one_raw(self):
        data = copy.deepcopy(self.data)
        data[0]["intents"].append({
            "label_id": "intent-2", "english_spans": []
        })
        for submission in data[0]["annotation"]["independent_submissions"]:
            submission["intents"].append({
                "label_id": "ind-other", "english_spans": []
            })
        self.assertEqual(2, self.audit(data)["total"])

    def test_reject_surrogate_split(self):
        self.reject(lambda d: d[0]["intents"][0].update(english_spans=[[1, 8]]))

    def test_reject_out_of_range_span(self):
        self.reject(lambda d: d[0]["intents"][0].update(english_spans=[[2, 99]]))

    def test_reject_overlapping_spans(self):
        self.reject(lambda d: d[0]["intents"][0].update(
            english_spans=[[0, 3], [2, 8]]))

    def test_reject_duplicate_intent_labels(self):
        self.reject(lambda d: d[0]["intents"].append(
            {"label_id": "intent-2", "english_spans": [[2, 8]]}))

    def test_reject_duplicate_case_id(self):
        self.reject(lambda d: d[1].update(case_id="example-dev"))

    def test_reject_casefolded_whitespace_duplicate(self):
        self.reject(lambda d: d[1].update(raw="🌸github"))

    def test_reject_split_leakage_from_shared_source(self):
        self.reject(lambda d: d[1]["source"].update(source_id="source-a"))

    def test_reject_missing_permission_record(self):
        self.reject(lambda d: d[0]["source"].update(license_or_consent=""))

    def test_reject_synthetic_placeholder_in_real_audit(self):
        with self.assertRaises(HoldoutValidationError):
            validate_rows(self.data, reference_raw=set())

    def test_reject_missing_stratification(self):
        self.reject(lambda d: d[1]["tags"].pop("english_dictionary_hit"))

    def test_reject_input_limit(self):
        self.reject(lambda d: d[0].update(raw="a" * 1025))

    def test_reject_empty_split(self):
        self.reject(lambda d: d[1].update(split="dev"))

    def test_reject_unhashable_split_type(self):
        self.reject(lambda d: d[1].update(split=[]))

    def test_reject_known_issue_overlap(self):
        data = copy.deepcopy(self.data)
        data[0]["raw"] = "known Issue diagnostic"
        with self.assertRaises(HoldoutValidationError):
            validate_rows(data, reference_raw={"known issue diagnostic"},
                          allow_synthetic=True)


    def test_reject_incomplete_final_annotation(self):
        self.reject(lambda d: d[1]["annotation"].update(adjudicated=False))

    def test_draft_accepts_incomplete_annotations_but_final_rejects(self):
        data = copy.deepcopy(self.data)
        for item in data:
            item["annotation"] = {"annotator_id": "pending-1", "adjudicated": False}
        self.assertEqual(2, self.audit(data, mode="draft")["total"])
        with self.assertRaises(HoldoutValidationError):
            self.audit(data, mode="final")

    def test_draft_rejects_false_final_flag(self):
        data = copy.deepcopy(self.data)
        with self.assertRaises(HoldoutValidationError):
            self.audit(data, mode="draft")

    def test_reject_single_independent_annotator(self):
        self.reject(lambda d: d[0]["annotation"]["independent_submissions"].pop())

    def test_reject_duplicate_independent_annotator(self):
        self.reject(lambda d: d[0]["annotation"]["independent_submissions"][1]
                    .update(annotator_id="independent-1"))

    def test_reject_disagreement_labeled_as_agreement(self):
        self.reject(lambda d: d[0]["annotation"]["independent_submissions"][1]
                    ["intents"][0].update(english_spans=[]))

    def test_reject_consensus_mismatch_with_canonical(self):
        self.reject(lambda d: d[0]["intents"][0].update(english_spans=[]))

    def test_disagreement_requires_independent_third_party(self):
        data = copy.deepcopy(self.data)
        annotation = data[0]["annotation"]
        annotation["independent_submissions"][1]["intents"][0]["english_spans"] = []
        annotation["adjudication"].update({
            "method": "third-party", "adjudicator_id": "third-reviewer",
            "reason": "Two different valid intentions; retain first",
        })
        self.assertEqual(2, self.audit(data)["total"])
        annotation["adjudication"]["adjudicator_id"] = "independent-1"
        with self.assertRaises(HoldoutValidationError):
            self.audit(data)

    def test_reject_missing_adjudication_record(self):
        self.reject(lambda d: d[0]["annotation"].pop("adjudication"))

    def test_reject_wrong_script_group(self):
        self.reject(lambda d: d[0]["tags"].update(script_group="anything"))

    def test_reject_unhashable_script_group(self):
        self.reject(lambda d: d[0]["tags"].update(script_group=[]))

    def test_utf8_decode_failure_has_private_safe_error(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "private_holdout.jsonl"
            path.write_bytes(b'{"raw":"\xff"}\n')
            with self.assertRaisesRegex(
                HoldoutValidationError, "Local corpus is not valid UTF-8"
            ):
                read_jsonl(path)
            with patch("sys.argv", ["validator", "--input", str(path)]):
                message = io.StringIO()
                with redirect_stderr(message):
                    self.assertEqual(1, main())
                self.assertIn("H11_HOLDOUT_AUDIT_FAIL", message.getvalue())
                self.assertNotIn("Traceback", message.getvalue())
                self.assertNotIn(str(path), message.getvalue())
                self.assertNotIn("\\xff", message.getvalue())

    def test_original_reference_files_present(self):
        self.assertTrue(known_issue_raw(), "Existing 103-case corpora must exist")


if __name__ == "__main__":
    unittest.main()
