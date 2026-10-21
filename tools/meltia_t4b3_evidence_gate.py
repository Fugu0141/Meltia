#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-B3 negative-only, OFFLINE TSF category/display-attribute evidence gate.

No Windows API calls or writes. Operator evidence is not proof of exhaustiveness
or provenance. Even complete "absent" observations NEVER authorize registration.
"""
import argparse
import json
from pathlib import Path

from meltia_t4a_preflight import MANIFEST, validate_preflight_manifest
from meltia_t4b_audit_gate import normalized_guid, read_evidence_json
from meltia_t4b2_ownership_model import CATEGORIES, ATTRIBUTE_FIELDS

FIELDS = (
    "phase", "coverage", "registry_writes", "registration_authorized",
    "attributes", "categories",
)
MAX_BYTES = 131072


def expected_targets(manifest):
    identity = validate_preflight_manifest(manifest)
    return {
        "attributes": [{"field": key, "guid": identity[key]}
                       for key in ATTRIBUTE_FIELDS],
        "categories": list(CATEGORIES),
        "clsid": identity["clsid"],
    }


def validate_evidence(manifest, evidence):
    targets = expected_targets(manifest)
    if not isinstance(evidence, dict) or set(evidence) != set(FIELDS):
        raise ValueError("evidence requires exact schema fields")
    if (evidence["phase"] != "T4-B3" or
            evidence["coverage"] != "current-user-visible-tsf-only" or
            evidence["registry_writes"] is not False or
            evidence["registration_authorized"] is not False):
        raise ValueError("phase, scope or immutability claim invalid")
    attributes = evidence["attributes"]
    categories = evidence["categories"]
    if (not isinstance(attributes, list) or
            len(attributes) != len(ATTRIBUTE_FIELDS) or
            not isinstance(categories, list) or
            len(categories) != len(CATEGORIES)):
        raise ValueError("all 3 attributes and all 5 categories are required")

    existing = []
    unknown = []
    for item, expected in zip(attributes, targets["attributes"]):
        if not isinstance(item, dict) or set(item) != {
                "field", "guid", "lookup", "owner_clsid"}:
            raise ValueError("attribute evidence fields invalid")
        if item["field"] != expected["field"] or normalized_guid(
                item["guid"]) != expected["guid"]:
            raise ValueError("attribute GUID/order differs from T2")
        if item["lookup"] == "resolved":
            owner = normalized_guid(item["owner_clsid"])
            # Any pre-existing owner (including Meltia) blocks *fresh* installation.
            existing.append({"type": "display-attribute", "field": item["field"],
                             "owner_is_meltia": owner == targets["clsid"]})
        elif item["lookup"] in ("not-found", "failed"):
            if item["owner_clsid"] is not None:
                raise ValueError("unresolved attribute must not assert an owner")
            # Failure / not-found is NOT an authoritative global absence proof.
            unknown.append("attribute:" + item["field"])
        else:
            raise ValueError("invalid attribute lookup outcome")

    for item, category in zip(categories, targets["categories"]):
        if not isinstance(item, dict) or set(item) != {
                "category", "item_clsid", "complete", "present"}:
            raise ValueError("category evidence fields invalid")
        if item["category"] != category or normalized_guid(
                item["item_clsid"]) != targets["clsid"]:
            raise ValueError("category registration tuple differs from T2")
        if type(item["complete"]) is not bool or type(item["present"]) not in (
                bool, type(None)):
            raise ValueError("category completeness and presence need exact types")
        if item["complete"] is False and item["present"] is not None:
            raise ValueError("incomplete category enumeration cannot claim presence")
        if item["complete"] is True and item["present"] is None:
            raise ValueError("complete enumeration requires a presence observation")
        if item["present"] is True:
            existing.append({"type": "tsf-category", "category": category})
        elif item["present"] is None:
            unknown.append("category:" + category)

    return {"existing": existing, "unknown": unknown}


def assess(manifest, evidence):
    base = {
        "phase": "T4-B3", "status": "inconclusive",
        "registration_authorized": False, "registry_writes": False,
        "native_tsf_access": False, "automatic_rollback": False,
    }
    try:
        findings = validate_evidence(manifest, evidence)
    except (ValueError, TypeError, KeyError, AttributeError) as exc:
        return {**base, "reason": "Evidence rejected: " + str(exc)}
    if findings["existing"]:
        return {**base, "status": "occupied", "observed_existing": findings["existing"],
                "reason": "Visible existing attributes or category entries; halt installation"}
    return {**base, "unresolved": findings["unknown"],
            "reason": ("A negative or incomplete TSF observation cannot establish "
                       "cross-user/provider or full registry ownership; VM validation pending")}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", required=True, type=Path,
                        help="TSF operator observation JSON; validation only, no OS access")
    args = parser.parse_args()
    try:
        if args.evidence.stat().st_size > MAX_BYTES:
            raise ValueError("evidence file too large")
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        evidence = read_evidence_json(args.evidence)
        report = assess(manifest, evidence)
    except (OSError, ValueError, TypeError, UnicodeError) as exc:
        report = {
            "phase": "T4-B3", "status": "inconclusive",
            "registration_authorized": False, "registry_writes": False,
            "native_tsf_access": False, "automatic_rollback": False,
            "reason": "Unable to validate evidence: " + str(exc),
        }
    print(json.dumps(report, ensure_ascii=False, sort_keys=True))
    return 1 if report["status"] == "occupied" else 2


if __name__ == "__main__":
    raise SystemExit(main())
