#!/usr/bin/env python3
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
OLD_PATH = ".github/workflows/linux.yml"
NEW_PATH = ".github/workflows/linux-artifacts.yml"
WINDOWS_ASSETS = {
    "meltia-windows-preview-win-x64-framework-dependent.zip":
        "sha256:e53039c79bcd04103dd05dd52fa766b4b788af075dca0c30c1b440df1c794ef7",
    "meltia-windows-preview-win-x64-framework-dependent.zip.sha256":
        "sha256:f660ab08da8bc035d36425f2911ed557526948a2928b450d7e312fd3414fb208",
}
FORBIDDEN = (
    re.compile(r"^\s*contents:\s*write\b", re.MULTILINE),
    re.compile(r"softprops/action-gh-release"),
    re.compile(r"\bgh\s+release\s+(?:create|upload|edit)\b"),
    re.compile(r"\b(?:curl|wget)\b[^\n]*/releases(?:/|\b)"),
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


def verify_live():
    """Read-only remote audit; fail closed if legacy workflow state is unknown."""
    tag = api_get(f"/repos/{REPO}/git/ref/tags/{TAG}")
    assert tag["object"]["sha"] == TAG_SHA, "Public tag target moved!"
    release = api_get(f"/repos/{REPO}/releases/tags/{TAG}")
    assert release["tag_name"] == TAG
    assert not release["draft"], "Published beta Release is no longer published"
    assets = {a["name"]: a.get("digest") for a in release["assets"]}
    assert assets == WINDOWS_ASSETS, "Public Release asset names/digests changed!"
    workflows = api_get(f"/repos/{REPO}/actions/workflows?per_page=100")
    old = [w for w in workflows["workflows"] if w["path"] == OLD_PATH]
    assert len(old) == 1, (
        "Cannot identify the old workflow ID/state. Do not claim risk is blocked."
    )
    assert old[0]["state"] == "disabled_manually", (
        f"Old Linux workflow must be disabled_manually; observed {old[0]['state']!r}"
    )
    print(f"PASS: legacy workflow ID={old[0]['id']} disabled; "
          f"tag SHA and {len(assets)} published Windows assets unchanged.")
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
        self.assertTrue(hazards("push:\n  tags: ['v*']"))
        self.assertFalse(hazards("permissions:\n  contents: read\n"
                                 "uses: actions/upload-artifact@sha"))


if __name__ == "__main__":
    if "--live" in sys.argv:
        sys.argv.remove("--live")
        try:
            verify_live()
        except Exception as exc:
            print(f"FAIL: live Issue #23 administrative gate: {exc}", file=sys.stderr)
            sys.exit(1)
    unittest.main()
