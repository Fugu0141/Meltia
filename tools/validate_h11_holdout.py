#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Fugu0141
"""Offline schema and leakage audit for custodian-held H11 natural-text data.

Never uploads or prints raw text, source identifiers, or individual labels.
Not a substitute for provenance/license review or label adjudication.
"""
from __future__ import annotations

import argparse
from datetime import datetime
import json
from pathlib import Path
import re
import sys
import unicodedata


class HoldoutValidationError(ValueError):
    """Validation failure without reproducing user-held raw text."""


REPO_ROOT = Path(__file__).resolve().parents[1]
KNOWN_ISSUE_FILES = (
    REPO_ROOT / "tests/Meltia.H10Evaluation/issue_intents_v2.jsonl",
    REPO_ROOT / "tests/Meltia.H10Evaluation/issue_intents_extra_20261014.jsonl",
)
SCRIPT_GROUPS = frozenset({
    "romaji-only", "english-only", "mixed-latin", "mixed-script",
    "japanese-script", "code-url", "other",
})

REQUIRED_TAGS = (
    "english_dictionary_hit", "explicit_english", "japanese_protection",
    "seam", "compound",
)


def normalized_for_dedup(raw: str) -> str:
    """Audit only: do not mutate the original input sent into the engine."""
    value = unicodedata.normalize("NFC", raw)
    value = " ".join(value.split())
    return re.sub(r"[A-Z]", lambda m: m.group(0).lower(), value)


def known_issue_raw() -> set[str]:
    found: set[str] = set()
    for path in KNOWN_ISSUE_FILES:
        with path.open(encoding="utf-8") as stream:
            for line in stream:
                if not line.strip():
                    continue
                row = json.loads(line)
                raw = row.get("raw", row.get("Raw"))
                if not isinstance(raw, str):
                    raise HoldoutValidationError("Invalid public Issue corpus")
                found.add(normalized_for_dedup(raw))
    return found


def utf16_boundaries(raw: str) -> tuple[set[int], int]:
    offsets = {0}
    end = 0
    for character in raw:
        codepoint = ord(character)
        if 0xD800 <= codepoint <= 0xDFFF:
            raise HoldoutValidationError("Unpaired surrogate code point")
        end += 2 if codepoint > 0xFFFF else 1
        offsets.add(end)
    return offsets, end


def nonempty(value: object) -> bool:
    return isinstance(value, str) and bool(value.strip())


def check_source(source: object, allow_synthetic: bool) -> str:
    if not isinstance(source, dict):
        raise HoldoutValidationError("Missing source metadata")
    if not nonempty(source.get("source_id")):
        raise HoldoutValidationError("Missing source ID")
    if source.get("kind") not in ("licensed-public", "consented-submission"):
        raise HoldoutValidationError("Unapproved source kind")
    license_text = source.get("license_or_consent")
    if not nonempty(license_text):
        raise HoldoutValidationError("Source needs license/consent record")
    if license_text == "synthetic-demo-only" and not allow_synthetic:
        raise HoldoutValidationError("Synthetic sample is not a natural-text source")
    timestamp = source.get("collected_utc")
    if not isinstance(timestamp, str) or not timestamp.endswith("Z"):
        raise HoldoutValidationError("Collection time must be an explicit UTC timestamp")
    try:
        datetime.fromisoformat(timestamp.replace("Z", "+00:00"))
    except ValueError as exc:
        raise HoldoutValidationError("Invalid collection time") from exc
    return source["source_id"]


def check_labels(
    intents: object, boundaries: set[int], max_len: int,
) -> frozenset[tuple[tuple[int, int], ...]]:
    if not isinstance(intents, list) or not intents:
        raise HoldoutValidationError("Missing interpretation labels")
    label_ids: set[str] = set()
    distinct_interpretations: set[tuple[tuple[int, int], ...]] = set()
    for intent in intents:
        if not isinstance(intent, dict) or not nonempty(intent.get("label_id")):
            raise HoldoutValidationError("Invalid intent label")
        label_id = intent["label_id"]
        if label_id in label_ids:
            raise HoldoutValidationError("Duplicated intent label ID")
        label_ids.add(label_id)
        spans = intent.get("english_spans")
        if not isinstance(spans, list):
            raise HoldoutValidationError("English spans must be a list")
        previous_end = 0
        parsed: list[tuple[int, int]] = []
        for item in spans:
            if not isinstance(item, list) or len(item) != 2 or any(
                type(value) is not int for value in item
            ):
                raise HoldoutValidationError("Invalid UTF-16 English span shape")
            start, end = item
            if not 0 <= start < end <= max_len or start < previous_end:
                raise HoldoutValidationError("Overlapping or out-of-range English span")
            if start not in boundaries or end not in boundaries:
                raise HoldoutValidationError("English span splits a surrogate pair")
            parsed.append((start, end))
            previous_end = end
        fingerprint = tuple(parsed)
        if fingerprint in distinct_interpretations:
            raise HoldoutValidationError("Duplicate interpretation spans")
        distinct_interpretations.add(fingerprint)
    return frozenset(distinct_interpretations)


def check_annotation(
    annotation: object, final_labels: frozenset[tuple[tuple[int, int], ...]],
    boundaries: set[int], max_len: int, mode: str,
) -> None:
    if not isinstance(annotation, dict) or type(annotation.get("adjudicated")) is not bool:
        raise HoldoutValidationError("Missing annotation status")
    if mode == "draft":
        # An unreviewed draft is never silently accepted as sealed test data.
        if annotation["adjudicated"] or not nonempty(annotation.get("annotator_id")):
            raise HoldoutValidationError("Draft requires one pending annotation")
        return
    if annotation["adjudicated"] is not True:
        raise HoldoutValidationError("Final holdout needs completed adjudication")
    submissions = annotation.get("independent_submissions")
    if not isinstance(submissions, list) or len(submissions) < 2:
        raise HoldoutValidationError("Final holdout needs two independent annotations")
    annotators: set[str] = set()
    decisions: list[frozenset[tuple[tuple[int, int], ...]]] = []
    for submission in submissions:
        if not isinstance(submission, dict) or not nonempty(submission.get("annotator_id")):
            raise HoldoutValidationError("Missing independent annotator ID")
        annotator_id = submission["annotator_id"]
        if annotator_id in annotators:
            raise HoldoutValidationError("Duplicated independent annotator")
        annotators.add(annotator_id)
        decisions.append(check_labels(submission.get("intents"), boundaries, max_len))
    decision = annotation.get("adjudication")
    if not isinstance(decision, dict) or decision.get("status") != "complete" or \
            decision.get("method") not in ("agreement", "third-party") or \
            not nonempty(decision.get("recorded_utc")) or \
            not nonempty(decision.get("reason")):
        raise HoldoutValidationError("Final adjudication audit record missing")
    recorded = decision["recorded_utc"]
    if not isinstance(recorded, str) or not recorded.endswith("Z"):
        raise HoldoutValidationError("Final adjudication timestamp must be UTC")
    try:
        datetime.fromisoformat(recorded.replace("Z", "+00:00"))
    except ValueError:
        raise HoldoutValidationError("Invalid adjudication timestamp") from None
    same = all(item == decisions[0] for item in decisions[1:])
    if decision["method"] == "agreement":
        if not same or final_labels != decisions[0]:
            raise HoldoutValidationError("Agreement does not match submitted labels")
    else:
        if same or not nonempty(decision.get("adjudicator_id")) or \
                decision["adjudicator_id"] in annotators:
            raise HoldoutValidationError("Disagreement requires an independent third party")
        # The top-level intents are the third party\x27s adjudicated outcome.


def validate_rows(
    rows: list[object], reference_raw: set[str] | None = None,
    *, allow_synthetic: bool = False, mode: str = "final",
) -> dict[str, int]:
    """Audit both splits together. A custodian runs this only on private storage."""
    if mode not in ("draft", "final"):
        raise HoldoutValidationError("Unknown holdout validation mode")
    if not rows:
        raise HoldoutValidationError("Empty holdout corpus")
    if reference_raw is None:
        reference_raw = known_issue_raw()

    counts = {"dev": 0, "test": 0}
    case_ids: set[str] = set()
    raw_owners: dict[str, str] = {}
    source_splits: dict[str, str] = {}
    for row_number, row in enumerate(rows, 1):
        try:
            if not isinstance(row, dict) or not nonempty(row.get("case_id")):
                raise HoldoutValidationError("Missing case ID")
            case_id = row["case_id"]
            if case_id in case_ids:
                raise HoldoutValidationError("Duplicated case ID")
            case_ids.add(case_id)
            split = row.get("split")
            if not isinstance(split, str) or split not in counts:
                raise HoldoutValidationError("Split must be dev or test")
            raw = row.get("raw")
            if not nonempty(raw):
                raise HoldoutValidationError("Empty or invalid raw input")
            boundaries, size = utf16_boundaries(raw)
            if size > 1024:
                raise HoldoutValidationError("Input exceeds 1024 UTF-16 units")
            dedup = normalized_for_dedup(raw)
            if dedup in reference_raw:
                raise HoldoutValidationError("Overlap with known Issue diagnostic corpus")
            if dedup in raw_owners:
                raise HoldoutValidationError("Duplicated normalized raw across cases")
            raw_owners[dedup] = split
            source_id = check_source(row.get("source"), allow_synthetic)
            if source_id in source_splits and source_splits[source_id] != split:
                raise HoldoutValidationError("Source crosses dev/test boundary")
            source_splits[source_id] = split
            labels = check_labels(row.get("intents"), boundaries, size)
            check_annotation(row.get("annotation"), labels, boundaries, size, mode)
            tags = row.get("tags")
            if not isinstance(tags, dict) or not isinstance(tags.get("script_group"), str) or \
                    tags["script_group"] not in SCRIPT_GROUPS:
                raise HoldoutValidationError("Invalid script-group tag")
            if any(type(tags.get(key)) is not bool for key in REQUIRED_TAGS):
                raise HoldoutValidationError("Missing or invalid strata tags")
            counts[split] += 1
        except HoldoutValidationError as error:
            # Print only row index/category, never the raw or source content.
            raise HoldoutValidationError(f"Row {row_number}: {error}") from None
    if counts["dev"] == 0 or counts["test"] == 0:
        raise HoldoutValidationError("Both dev and test must be present")
    return {"dev": counts["dev"], "test": counts["test"], "total": len(rows)}


def read_jsonl(path: Path) -> list[object]:
    try:
        with path.open(encoding="utf-8") as stream:
            output = []
            for number, line in enumerate(stream, 1):
                if not line.strip():
                    continue
                try:
                    output.append(json.loads(line))
                except json.JSONDecodeError:
                    raise HoldoutValidationError(f"Row {number}: malformed JSON") from None
            return output
    except UnicodeError:
        # UnicodeDecodeError otherwise exposes potentially private byte values.
        raise HoldoutValidationError("Local corpus is not valid UTF-8") from None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path,
                        help="Private JSONL path on custodian's local machine")
    parser.add_argument("--mode", choices=("draft", "final"), default="final",
                        help="final requires two independent labels and completed adjudication")
    args = parser.parse_args()
    try:
        summary = validate_rows(read_jsonl(args.input), mode=args.mode)
    except (HoldoutValidationError, OSError) as exc:
        if isinstance(exc, OSError):
            # File path may reveal private folders: don't echo it.
            print("H11_HOLDOUT_AUDIT_FAIL: cannot read local corpus", file=sys.stderr)
        else:
            print(f"H11_HOLDOUT_AUDIT_FAIL: {exc}", file=sys.stderr)
        return 1
    print("H11_HOLDOUT_AUDIT_PASS: " + json.dumps(summary, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
