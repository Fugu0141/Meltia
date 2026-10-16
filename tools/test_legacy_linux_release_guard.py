#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Fugu0141
"""Issue #23: historical Linux Release publisher isolation and regression tests.

Safe by default: tests never dispatch historical workflows or write releases.
Pass --live to read the GitHub repository state and verify the admin gate.
"""
import json
import os
import re
import subprocess
import sys
import unittest
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = "Fugu0141/Meltia"
TAG = "v1.0.0-beta.1"
TAG_SHA = "7894860d9a8b0960bf6a25d27fec5c0712deb059"
RELEASE_ID = 410669509
OLD_PATH = ".github/workflows/linux.yml"
NEW_PATH = ".github/workflows/linux-artifacts.yml"
WINDOWS_ASSETS = {
    "meltia-windows-preview-win-x64-framework-dependent.zip":
        "sha256:e53039c79bcd04103dd05dd52fa766b4b788af075dca0c30c1b440df1c794ef7",
    "meltia-windows-preview-win-x64-framework-dependent.zip.sha256":
        "sha256:f660ab08da8bc035d36425f2911ed557526948a2928b450d7e312fd3414fb208",
}
WINDOWS_ASSET_IDS = {
    "meltia-windows-preview-win-x64-framework-dependent.zip": 633960588,
    "meltia-windows-preview-win-x64-framework-dependent.zip.sha256": 633960585,
}
FORBIDDEN = (
    re.compile(r"^\s*contents:\s*write\b", re.MULTILINE),
    re.compile(r"softprops/action-gh-release"),
    re.compile(r"\bgh\s+release\s+(?:create|upload|edit)\b"),
    re.compile(r"\b(?:curl|wget)\b[^\n]*(?:api|uploads)\.github\.com/repos/[^\s]+/releases\b"),
    re.compile(r"^\s*tags:\s*\[", re.MULTILINE),
)


def hazards(text):
    """Conservative policy for the artifact-only Linux CI, not a YAML evaluator."""
    return [pattern.pattern for pattern in FORBIDDEN if pattern.search(text)]


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def api_get(path):
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    headers = {"Accept": "application/vnd.github+json",
               "User-Agent": "Meltia-Issue-23-read-only-audit"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request("https://api.github.com" + path, headers=headers)
    with urllib.request.urlopen(req, timeout=20) as response:
        return json.load(response)


def verify_live_snapshot(tag, release, workflows):
    """Validate a read-only API snapshot; runtime checks must survive python -O."""
    if tag.get("object", {}).get("sha") != TAG_SHA:
        raise RuntimeError("Public tag target moved!")
    if release.get("id") != RELEASE_ID or release.get("tag_name") != TAG:
        raise RuntimeError("Public beta Release identity changed!")
    if release.get("draft") is not False:
        raise RuntimeError("Published beta Release is no longer published")
    assets = {
        asset["name"]: (asset.get("id"), asset.get("digest"))
        for asset in release.get("assets", [])
    }
    expected = {
        name: (WINDOWS_ASSET_IDS[name], digest)
        for name, digest in WINDOWS_ASSETS.items()
    }
    if len(release.get("assets", [])) != len(expected) or assets != expected:
        raise RuntimeError("Public Release asset IDs/names/digests changed!")
    old = [w for w in workflows.get("workflows", [])
           if w.get("path") == OLD_PATH]
    if len(old) != 1 or not isinstance(old[0].get("id"), int):
        raise RuntimeError(
            "Cannot identify the old workflow ID/state. Do not claim risk is blocked."
        )
    if old[0].get("state") != "disabled_manually":
        raise RuntimeError(
            f"Old Linux workflow must be disabled_manually; observed {old[0].get('state')!r}"
        )
    return old[0]["id"], len(assets)


def fetch_historical_runs(workflow_id):
    """Read every recorded run for the retired workflow (GET requests only)."""
    all_runs = []
    page = 1
    expected_count = None
    while True:
        snapshot = api_get(
            f"/repos/{REPO}/actions/workflows/{workflow_id}/runs"
            f"?per_page=100&page={page}"
        )
        count = snapshot.get("total_count")
        batch = snapshot.get("workflow_runs")
        if not isinstance(count, int) or not isinstance(batch, list):
            raise RuntimeError("Cannot audit historical workflow runs")
        if expected_count is None:
            expected_count = count
        elif count != expected_count:
            raise RuntimeError("Historical workflow run list changed during audit")
        all_runs.extend(batch)
        if len(all_runs) >= expected_count:
            break
        if not batch:
            raise RuntimeError("Historical workflow runs were truncated")
        page += 1
    return {"total_count": expected_count, "workflow_runs": all_runs}


def verify_historical_runs(workflow_id, history):
    """Fail closed when an old run might have a tag ref on re-run.

    GitHub re-runs keep the original GITHUB_REF. The old publisher step only
    runs on refs/tags/v*, not on branch pushes or refs/pull/*/merge.
    """
    count = history.get("total_count")
    runs = history.get("workflow_runs")
    if not isinstance(count, int) or not isinstance(runs, list):
        raise RuntimeError("Historical workflow run audit is incomplete")
    if count < 1 or len(runs) != count:
        raise RuntimeError("Historical workflow run count is incomplete")
    ids = set()
    for run in runs:
        run_id = run.get("id")
        if not isinstance(run_id, int) or run_id in ids:
            raise RuntimeError("Missing or duplicated historical workflow run ID")
        ids.add(run_id)
        if run.get("workflow_id") != workflow_id or run.get("path") != OLD_PATH:
            raise RuntimeError(f"Historical workflow run {run_id} has wrong identity")
        event = run.get("event")
        branch = run.get("head_branch")
        # Tag pushes can report the tag as head_branch. A missing ref or a
        # v-prefixed name is ambiguous: refuse to assert historical safety.
        if (event not in ("push", "pull_request")
                or not isinstance(branch, str) or not branch
                or branch.startswith("v")):
            raise RuntimeError(
                f"Historical run {run_id} may re-run a Release publisher "
                f"(event={event!r}, head_branch={branch!r})"
            )
    return count


def verify_live():
    """Read-only remote audit; never dispatch, re-run or write any release."""
    tag = api_get(f"/repos/{REPO}/git/ref/tags/{TAG}")
    release = api_get(f"/repos/{REPO}/releases/tags/{TAG}")
    workflows = api_get(f"/repos/{REPO}/actions/workflows?per_page=100")
    workflow_id, asset_count = verify_live_snapshot(tag, release, workflows)
    history = fetch_historical_runs(workflow_id)
    historical_run_count = verify_historical_runs(workflow_id, history)
    print(f"PASS: legacy workflow ID={workflow_id} disabled_manually; "
          f"tag SHA and {asset_count} published Windows assets unchanged.")
    print(f"PASS: {historical_run_count} historical runs audited; none was "
          "a tag/manual run eligible for the old Release upload step.")
    print("NOTE: this verifies recorded runs and present settings; it does "
          "not establish a permanent GitHub platform guarantee.")
    print(f"INFO: existing release immutable={release.get('immutable')!r}; "
          "changing the repository setting alone is not retroactive.")


class LegacyLinuxGuardTests(unittest.TestCase):
    def test_reproduction_old_tag_is_release_writer(self):
        """Reproduce by reading the historical commit; never execute the workflow."""
        historical = git("show", f"{TAG_SHA}:{OLD_PATH}")
        self.assertIn("workflow_dispatch:", historical)
        self.assertIn("softprops/action-gh-release@", historical)
        self.assertIn("contents: write", historical)
        self.assertIn("refs/tags/v", historical)
        self.assertTrue(hazards(historical))

    def test_tag_target_is_not_replaced(self):
        self.assertEqual(git("rev-parse", f"refs/tags/{TAG}"), TAG_SHA)

    def test_legacy_workflow_path_retired_on_default_branch(self):
        self.assertFalse((ROOT / OLD_PATH).exists(),
                         "Keeping linux.yml live keeps historical workflow ID live")

    def test_new_linux_ci_keeps_tests_and_only_artifacts(self):
        content = (ROOT / NEW_PATH).read_text(encoding="utf-8")
        self.assertEqual(hazards(content), [],
                         "Artifact-only CI must never regain Release-write privileges")
        self.assertIn("actions/upload-artifact@", content)
        self.assertIn("NativeAOT boundary tests", content)
        self.assertIn("IBus and real Mozc integration tests", content)
        self.assertIn("Private IBus daemon tests", content)
        self.assertIn("Test fcitx5 addon", content)
        self.assertIn("branches: [main]", content)
        self.assertIn("workflow_dispatch:", content)
        self.assertIn(NEW_PATH, content)
        self.assertNotIn(OLD_PATH, content)
        self.assertIn("contents: read", content)

    def test_guard_flags_accidental_publisher_reintroduction(self):
        self.assertTrue(hazards("permissions:\n  contents: write\n"))
        self.assertTrue(hazards("uses: softprops/action-gh-release@deadbeef"))
        self.assertTrue(hazards("run: gh release upload v1 file.zip"))
        self.assertTrue(hazards("run: curl -X POST https://uploads.github.com/repos/a/b/releases/1/assets"))
        self.assertFalse(hazards("curl -fsSL https://github.com/bazelbuild/bazelisk/releases/download/v1.29.0/bazelisk-linux-amd64"))
        self.assertTrue(hazards("push:\n  tags: ['v*']"))
        self.assertFalse(hazards("permissions:\n  contents: read\n"
                                 "uses: actions/upload-artifact@sha"))


    @staticmethod
    def _live_fixture():
        tag = {"object": {"sha": TAG_SHA}}
        release = {
            "id": RELEASE_ID,
            "tag_name": TAG,
            "draft": False,
            "assets": [
                {"name": name, "id": WINDOWS_ASSET_IDS[name], "digest": digest}
                for name, digest in WINDOWS_ASSETS.items()
            ],
        }
        workflows = {"workflows": [
            {"path": OLD_PATH, "id": 12345, "state": "disabled_manually"}
        ]}
        return tag, release, workflows

    def test_live_gate_accepts_intact_snapshot(self):
        self.assertEqual(verify_live_snapshot(*self._live_fixture()), (12345, 2))

    def test_live_gate_rejects_active_or_missing_workflow(self):
        tag, release, workflows = self._live_fixture()
        workflows["workflows"][0]["state"] = "active"
        with self.assertRaisesRegex(RuntimeError, "disabled_manually"):
            verify_live_snapshot(tag, release, workflows)
        workflows["workflows"] = []
        with self.assertRaisesRegex(RuntimeError, "Cannot identify"):
            verify_live_snapshot(tag, release, workflows)

    def test_live_gate_rejects_moved_tag(self):
        tag, release, workflows = self._live_fixture()
        tag["object"]["sha"] = "different"
        with self.assertRaisesRegex(RuntimeError, "tag target moved"):
            verify_live_snapshot(tag, release, workflows)

    def test_live_gate_rejects_replaced_release_or_asset(self):
        tag, release, workflows = self._live_fixture()
        release["id"] += 1
        with self.assertRaisesRegex(RuntimeError, "identity changed"):
            verify_live_snapshot(tag, release, workflows)
        release["id"] = RELEASE_ID
        release["assets"][0]["id"] += 1
        with self.assertRaisesRegex(RuntimeError, "asset IDs/names/digests changed"):
            verify_live_snapshot(tag, release, workflows)

    @staticmethod
    def _history_fixture():
        return {"total_count": 2, "workflow_runs": [
            {"id": 1, "workflow_id": 12345, "path": OLD_PATH,
             "event": "push", "head_branch": "main"},
            {"id": 2, "workflow_id": 12345, "path": OLD_PATH,
             "event": "pull_request", "head_branch": "feature/ci"},
        ]}

    def test_history_audit_accepts_only_branch_and_pr_runs(self):
        self.assertEqual(
            verify_historical_runs(12345, self._history_fixture()), 2
        )

    def test_history_audit_rejects_tag_or_manual_runs(self):
        history = self._history_fixture()
        history["workflow_runs"][0]["head_branch"] = "v1.0.0-beta.1"
        with self.assertRaisesRegex(RuntimeError, "may re-run"):
            verify_historical_runs(12345, history)
        history["workflow_runs"][0]["head_branch"] = "main"
        history["workflow_runs"][0]["event"] = "workflow_dispatch"
        with self.assertRaisesRegex(RuntimeError, "may re-run"):
            verify_historical_runs(12345, history)

    def test_history_audit_rejects_incomplete_or_misattributed_runs(self):
        history = self._history_fixture()
        history["total_count"] = 3
        with self.assertRaisesRegex(RuntimeError, "count is incomplete"):
            verify_historical_runs(12345, history)
        history = self._history_fixture()
        history["workflow_runs"][1]["workflow_id"] = 9876
        with self.assertRaisesRegex(RuntimeError, "wrong identity"):
            verify_historical_runs(12345, history)
        history = self._history_fixture()
        history["workflow_runs"][1]["id"] = 1
        with self.assertRaisesRegex(RuntimeError, "duplicated"):
            verify_historical_runs(12345, history)

    def test_live_gate_rejects_added_asset_or_draft(self):
        tag, release, workflows = self._live_fixture()
        release["assets"].append({"name": "Meltype-linux.zip", "id": 1,
                                  "digest": "sha256:unexpected"})
        with self.assertRaisesRegex(RuntimeError, "asset IDs/names/digests changed"):
            verify_live_snapshot(tag, release, workflows)
        release["assets"].pop()
        release["draft"] = True
        with self.assertRaisesRegex(RuntimeError, "no longer published"):
            verify_live_snapshot(tag, release, workflows)


if __name__ == "__main__":
    if "--live" in sys.argv:
        sys.argv.remove("--live")
        try:
            verify_live()
        except Exception as exc:
            print(f"FAIL: live Issue #23 administrative gate: {exc}", file=sys.stderr)
            sys.exit(1)
    unittest.main()
