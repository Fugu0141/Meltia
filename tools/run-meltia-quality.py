#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Fugu0141
"""Run Meltia v1.0 diagnostic suites without GitHub-hosted Actions.

This script does NOT modify the input method, commit a conversion, or mark
a release as verified. Requires Python 3.10+ and .NET SDK 10 installed.
"""
from __future__ import annotations

import argparse
import datetime
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
CORE = "tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj"
BRIDGE = "tests/Meltia.CompositionBridge.Tests/Meltia.CompositionBridge.Tests.csproj"
EVAL = "tests/Meltia.BoundaryEvaluation/Meltia.BoundaryEvaluation.csproj"
BENCH = "tools/Meltia.BoundaryBenchmark/Meltia.BoundaryBenchmark.csproj"


def main() -> int:
    parser = argparse.ArgumentParser(description="Run read-only Meltia v1.0 quality gates")
    parser.add_argument("--tier", choices=("smoke", "full"), default="smoke")
    parser.add_argument("--output-dir", default="artifacts/meltia-quality")
    args = parser.parse_args()
    output = Path(args.output_dir)
    if not output.is_absolute():
        output = ROOT / output
    output.mkdir(parents=True, exist_ok=True)
    steps = [
        ("core-contracts", ["dotnet", "run", "--project", CORE, "-c", "Release"]),
        ("bridge-contracts", ["dotnet", "run", "--project", BRIDGE, "-c", "Release"]),
        ("core-47", ["dotnet", "run", "--project", EVAL, "-c", "Release",
                     "--", "--output", str(output / "core-47.json"), "--suite", "core"]),
        ("adversarial-56", ["dotnet", "run", "--project", EVAL, "-c", "Release",
                           "--", "--output", str(output / "adversarial-56.json"),
                           "--suite", "adversarial"]),
    ]
    if args.tier == "full":
        steps.extend([
            ("robustness-530", ["dotnet", "run", "--project", EVAL, "-c", "Release",
                                "--", "--output", str(output / "robustness-530.json"),
                                "--suite", "robustness"]),
            ("generalization-290", ["dotnet", "run", "--project", EVAL, "-c", "Release",
                                    "--", "--output", str(output / "generalization-290.json"),
                                    "--suite", "generalization"]),
            ("longrun-60", ["dotnet", "run", "--project", EVAL, "-c", "Release",
                            "--", "--output", str(output / "longrun-60.json"),
                            "--suite", "longrun"]),
            ("offline-benchmark", ["dotnet", "run", "--project", BENCH, "-c", "Release",
                                   "--", "--output", str(output / "offline-benchmark.json")]),
        ])
    exists = shutil.which("dotnet") is not None
    report = {
        "schema": "meltia/local-quality-v1",
        "tier": args.tier,
        "date_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "repo_root": str(ROOT),
        "environment": {"python": sys.version.split()[0], "dotnet_found": exists},
        "status": "not-run" if not exists else "running",
        "steps": [],
        "notes": [
            "Synthetic intent-labelled cases are not human-labelled blind natural text.",
            "Offline Core latency cannot establish Windows TSF typing latency.",
            "This script does not run Windows/macOS native build or release packaging."
        ],
    }
    if not exists:
        print("ERROR: .NET SDK 10 not found on PATH; quality tests were NOT run.", file=sys.stderr)
        report["status"] = "blocked-missing-dotnet"
    else:
        environment = os.environ.copy()
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        environment["DOTNET_NOLOGO"] = "1"
        for name, command in steps:
            log_path = output / (name + ".log")
            print("RUN", name, flush=True)
            started = time.perf_counter()
            with log_path.open("w", encoding="utf-8", errors="replace") as logfile:
                try:
                    process = subprocess.run(
                        command, cwd=ROOT, stdout=logfile, stderr=subprocess.STDOUT,
                        env=environment, check=False,
                    )
                    return_code = process.returncode
                except OSError as exc:
                    logfile.write(str(exc) + "\n")
                    return_code = 127
            seconds = round(time.perf_counter() - started, 3)
            entry = {"name": name, "return_code": return_code,
                     "duration_seconds": seconds,
                     "log": str(log_path.relative_to(output))}
            report["steps"].append(entry)
            print("PASS" if return_code == 0 else "FAIL", name,
                  "(" + str(seconds) + "s)", flush=True)
            if return_code != 0:
                lines = log_path.read_text(encoding="utf-8", errors="replace").splitlines()
                for line in lines[-14:]:
                    print("  " + line, file=sys.stderr)
        report["status"] = ("passed" if all(
            entry["return_code"] == 0 for entry in report["steps"]
        ) else "failed")
    summary = output / "summary.json"
    summary.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                       encoding="utf-8")
    print("Quality report:", summary, "status:", report["status"])
    if not exists:
        return 69
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
