# SPDX-License-Identifier: GPL-3.0-or-later
"""Synthetic-only negative tests: no real text or sealed holdout data."""
import copy
import unittest

from tools.validate_h11_holdout import (
    HoldoutValidationError,
    known_issue_raw,
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
        "annotation": {"annotator_id": "test-custodian", "adjudicated": True},
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

    def test_reject_known_issue_overlap(self):
        data = copy.deepcopy(self.data)
        data[0]["raw"] = "known Issue diagnostic"
        with self.assertRaises(HoldoutValidationError):
            validate_rows(data, reference_raw={"known issue diagnostic"},
                          allow_synthetic=True)

    def test_original_reference_files_present(self):
        self.assertTrue(known_issue_raw(), "Existing 103-case corpora must exist")


if __name__ == "__main__":
    unittest.main()
