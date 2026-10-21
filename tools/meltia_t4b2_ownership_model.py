#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-B2 OFFLINE registration ownership / inverse-order rollback model.

This module neither registers nor unregisters TSF, touches Windows registry,
launches subprocesses, reads user state, nor writes files. All observations
below are *synthetic*; successful simulation NEVER authorizes registration.
"""
import argparse
import json
from pathlib import Path

from meltia_t4a_preflight import MANIFEST, validate_preflight_manifest

SOURCE = "T2 design-only manifest"
CATEGORIES = (
    "GUID_TFCAT_TIP_KEYBOARD",
    "GUID_TFCAT_DISPLAYATTRIBUTEPROVIDER",
    "GUID_TFCAT_TIPCAP_INPUTMODECOMPARTMENT",
    "GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT",
    "GUID_TFCAT_TIPCAP_SYSTRAYSUPPORT",
)
ATTRIBUTE_FIELDS = (
    "inputAttributeGuid", "convertedAttributeGuid", "targetAttributeGuid",
)
# No installer, executable registry mutation commands, or automatic cleanup.
UNRESOLVED = (
    "TSF display attribute GUID registration/ownership outside T4-A/T4-B1 coverage",
    "Windows per-user and per-machine COM/TSF scope and 32/64-bit view inspection",
    "Separate x64/x86 installed binary layout, file hashes and provenance",
    "Independent identity and ownership witness from isolated VM pre/post snapshots",
    "Atomicity across COM registry, TSF profile and category association APIs",
    "Rollback after external mutations, shared resources or partial API failures",
)


def owned_resources(manifest):
    """Return strictly scoped abstract resource IDs; no actual registry operations."""
    identity = validate_preflight_manifest(manifest)
    ownership = manifest["distribution"]["uninstallOwnership"]
    if ownership["categoryAssociations"] != list(CATEGORIES):
        raise ValueError("T2 categories differ from the inherited TIP contract")
    clsid = "{" + identity["clsid"].upper() + "}"
    profile = "{" + identity["profileGuid"].upper() + "}"
    language = manifest["languageId"]
    resources = []
    for view in ("64", "32"):
        resources.append({
            "id": "com:" + view,
            "kind": "com-class",
            "owner": clsid,
            "scope": "HKLM",
            "registry_view": view,
            "target": r"Software\Classes\CLSID" + "\\" + clsid,
        })
    resources.append({
        "id": "profile",
        "kind": "tsf-language-profile",
        "owner": clsid,
        "language_id": language,
        "profile_guid": profile,
        "scope": "TSF API: RegisterProfile(clsid, LANGID, profile)",
    })
    for category in CATEGORIES:
        resources.append({
            "id": "category:" + category,
            "kind": "tsf-category-association",
            "owner": clsid,
            "category": category,
            "item": clsid,
            "scope": "TSF API: RegisterCategory(clsid, category, clsid)",
        })
    assert len(resources) == 8
    return resources


def design_inventory(manifest):
    """Expose three non-owned-until-proven display attribute identifiers."""
    identity = validate_preflight_manifest(manifest)
    return {
        "phase": "T4-B2",
        "source": SOURCE,
        "registration_authorized": False,
        "registry_writes": False,
        "delete_shared_category_guids": False,
        "delete_foreign_products": False,
        "automatic_rollback": False,
        "resources": owned_resources(manifest),
        "display_attributes_unverified": [
            {"field": field, "guid": identity[field], "deletable": False}
            for field in ATTRIBUTE_FIELDS
        ],
        "unresolved": list(UNRESOLVED),
    }


def evaluate_synthetic_rollback(resources, before, executed_ids, after):
    """Validate a simulation's observation ledger before proposing *conceptual* inverse steps.

    The 'created-owned' mark is not evidence of actual Windows ownership.
    The returned steps must NOT be fed to a registry mutator/uninstaller.
    """
    identifiers = [item["id"] for item in resources]
    result = {
        "phase": "T4-B2",
        "status": "halt-manual-review",
        "candidate_rollback_order": [],
        "registration_authorized": False,
        "registry_writes": False,
        "automatic_rollback": False,
    }
    if len(set(identifiers)) != len(identifiers):
        return {**result, "reason": "duplicate planned resource"}
    if (not isinstance(before, dict) or not isinstance(after, dict) or
            not isinstance(executed_ids, list) or
            set(before) != set(identifiers) or set(after) != set(identifiers)):
        return {**result, "reason": "incomplete or inconsistent snapshot"}
    if any(type(resource_id) is not str for resource_id in executed_ids):
        return {**result, "reason": "invalid execution ledger"}
    if executed_ids != identifiers[:len(executed_ids)]:
        return {**result, "reason": "non-prefix, duplicate or foreign execution ledger"}
    if any(before[resource_id] != "absent" for resource_id in identifiers):
        return {**result, "reason": "occupied or unknown baseline; do not overwrite"}
    executed = set(executed_ids)
    for resource_id in identifiers:
        expected = "created-owned" if resource_id in executed else "absent"
        if after[resource_id] != expected:
            return {**result, "reason": "modified, missing, unknown or unverified resource"}
    # Synthetic reversal is always in strict inverse registration order.
    return {
        **result,
        "status": "simulation-reversible-not-authorized",
        "candidate_rollback_order": list(reversed(executed_ids)),
        "reason": "Synthetic states only; real ownership receipts and VM evidence absent",
    }


def simulate_prefix(manifest, applied_count):
    resources = owned_resources(manifest)
    if type(applied_count) is not int or not 0 <= applied_count <= len(resources):
        raise ValueError("simulation count out of range")
    identifiers = [item["id"] for item in resources]
    before = dict.fromkeys(identifiers, "absent")
    after = dict.fromkeys(identifiers, "absent")
    for resource_id in identifiers[:applied_count]:
        after[resource_id] = "created-owned"
    return evaluate_synthetic_rollback(resources, before, identifiers[:applied_count], after)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--simulate-prefix", type=int, default=0,
                        help="synthetic completed steps 0..8; no Windows access")
    args = parser.parse_args()
    try:
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        inventory = design_inventory(manifest)
        outcome = simulate_prefix(manifest, args.simulate_prefix)
        report = {**inventory, "simulation": outcome}
        exit_code = 0 if outcome["status"] == "simulation-reversible-not-authorized" else 2
    except (ValueError, TypeError, KeyError, OSError) as exc:
        report = {
            "phase": "T4-B2", "status": "inconclusive",
            "registration_authorized": False, "registry_writes": False,
            "automatic_rollback": False, "reason": str(exc),
        }
        exit_code = 2
    print(json.dumps(report, ensure_ascii=False, sort_keys=True, indent=2))
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
