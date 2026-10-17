#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Fugu0141
"""Compare old-main and PR-H11 *full Analyze* in one runner, using A-B-B-A.

Input reports must be produced from the unchanged Phase 0 benchmark CLI:
  base/full-a.json, head/full-a.json, head/full-b.json, base/full-b.json
The suite already includes 8 families at 32/128/256/512/1024 UTF-16 and
4 independently generated editor operation snapshots. No private typing data.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import pathlib
import statistics
from collections import defaultdict

MODEL = "H11-word-phonetic"
LENGTHS = (32, 128, 256, 512, 1024)
CATEGORIES = (
    "romaji-ja", "english", "mixed", "url", "symbols", "emoji",
    "explicit-en", "explicit-ja",
)
OTHER_MODELS = ("v1.0-flank-independent", "H10-dual-channel")
RUN_FIELDS = (
    "Schema", "Mode", "FixtureSetSha256", "DictionaryFileSha256",
    "EmbeddedDictionarySha256", "SdkVersion", "Runtime", "OSDescription",
    "Architecture", "CpuModel", "GCMode", "LogicalProcessorCount",
    "Frequency", "FixtureGenerator", "FixtureCount",
    "WarmupsPerFixtureAndModel", "RepetitionsPerFixtureAndModel",
)
IDENTITY = ("Model", "Family", "Operation", "Utf16Length", "RawSha256", "Evidence")


def read(path: pathlib.Path) -> dict:
    data = json.loads(path.read_text(encoding="utf-8"))
    assert data["Schema"] == "meltia-h11-synthetic-performance-v1", path
    assert data["Mode"] == "full", path
    assert data["FixtureCount"] == 44, (path, data["FixtureCount"])
    assert data["RepetitionsPerFixtureAndModel"] == 7, path
    assert data["DictionaryFileSha256"] == data["EmbeddedDictionarySha256"], path
    assert data["ActualCheckoutCommit"] not in ("", "local-unspecified"), path
    assert len(data["Measurements"]) == 44 * 3, (path, len(data["Measurements"]))
    return data


def key(row: dict) -> tuple:
    return tuple(row[f] for f in IDENTITY)


def percentile(values: list[float], fraction: float) -> float:
    assert values, "Empty measurement bucket"
    sorted_values = sorted(values)
    return sorted_values[math.ceil(fraction * len(values)) - 1]


def distribution(reports: list[dict], model: str, predicate) -> tuple:
    samples_time: list[float] = []
    samples_alloc: list[float] = []
    for report in reports:
        for row in report["Measurements"]:
            if row["Model"] == model and predicate(row):
                samples_time.extend(row["ElapsedMilliseconds"])
                samples_alloc.extend(row["AllocatedBytes"])
    assert samples_time and samples_alloc
    return (
        percentile(samples_time, 0.50),
        percentile(samples_time, 0.95),
        percentile(samples_alloc, 0.50),
        percentile(samples_alloc, 0.95),
    )


def fmt(v: float) -> str:
    return f"{v:.3f}"


def row_text(label: str, base: tuple, head: tuple) -> str:
    pct = ((head[0] / base[0] - 1) * 100) if base[0] else math.nan
    return (
        f"| {label} | {fmt(base[0])} | {fmt(head[0])} | "
        f"{pct:+.1f}% | {fmt(base[1])} | {fmt(head[1])} | "
        f"{base[2]:.0f} | {head[2]:.0f} | "
        f"{base[3]:.0f} | {head[3]:.0f} |"
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("base_a", type=pathlib.Path)
    parser.add_argument("head_a", type=pathlib.Path)
    parser.add_argument("head_b", type=pathlib.Path)
    parser.add_argument("base_b", type=pathlib.Path)
    parser.add_argument("--markdown", required=True, type=pathlib.Path)
    parser.add_argument("--json", required=True, type=pathlib.Path)
    args = parser.parse_args()
    ba, ha, hb, bb = (read(x) for x in
                      (args.base_a, args.head_a, args.head_b, args.base_b))
    reports = [ba, ha, hb, bb]

    for field in RUN_FIELDS:
        assert len({str(report[field]) for report in reports}) == 1, (
            "Pair environment/fixture disagreement", field,
            [report[field] for report in reports])
    assert ba["ActualCheckoutCommit"] == bb["ActualCheckoutCommit"], "Baseline SHA moved"
    assert ha["ActualCheckoutCommit"] == hb["ActualCheckoutCommit"], "PR HEAD SHA moved"
    assert ba["ActualCheckoutCommit"] != ha["ActualCheckoutCommit"], "Both versions same SHA"
    for model in OTHER_MODELS:
        source_hashes = [report["ModelSourceSha256"][model] for report in reports]
        assert len(set(source_hashes)) == 1, ("Unrelated engine source changed", model)
    assert len({report["ModelSourceSha256"][MODEL] for report in (ba, bb)}) == 1
    assert len({report["ModelSourceSha256"][MODEL] for report in (ha, hb)}) == 1

    by_key = [{key(row): row for row in report["Measurements"]} for report in reports]
    keys = set(by_key[0])
    assert len(keys) == 132
    assert all(set(by) == keys for by in by_key), "Fixture identities differ"
    for entry in keys:
        for by in by_key[1:]:
            assert by[entry]["RouteCount"] == by_key[0][entry]["RouteCount"], (
                "Route count differs", entry)
        # Avoid making benchmark correctness depend on a gold label. This is
        # strictly the same raw and evidence across base/HEAD, all engines.
        assert len({by[entry]["RouteCount"] for by in by_key}) == 1

    base = [ba, bb]
    head = [ha, hb]
    table_header = (
        "| 入力 | 旧p50 ms | D2 p50 ms | p50変化 | "
        "旧p95 ms | D2 p95 ms | 旧alloc p50 B | D2 alloc p50 B | "
        "旧alloc p95 B | D2 alloc p95 B |"
    )
    separator = "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|"
    lines = [
        "# H11 Phase 3 D2: 同一runner A-B-B-A全長ペア測定",
        "",
        f"- baseline: `{ba['ActualCheckoutCommit']}`",
        f"- PR HEAD: `{ha['ActualCheckoutCommit']}`",
        "- 順序: base → D2 → D2 → base（全実行を同一GitHub Actionsジョブで順番に行う）",
        "- 各レポートは.NET Release、各入力2回warmup・7回測定。"
        "入力集合8種×長さ5種類+4編集操作=44件、各バージョン2レポート。",
        f"- fixture SHA-256: `{ba['FixtureSetSha256']}`",
        f"- dictionary SHA-256: `{ba['DictionaryFileSha256']}`",
        f"- CPU: `{ba['CpuModel']}`; OS: `{ba['OSDescription']}`",
        f"- .NET SDK: `{ba['SdkVersion']}`; runtime: `{ba['Runtime']}`;"
        f" cores: {ba['LogicalProcessorCount']}; GC: {ba['GCMode']}",
        "- 50/95パーセンタイル：モデル=H11、同一カテゴリ内の全入力・7試行・2実行を"
        "結合しnearest-rankで集計。allocは現スレッドの各Analyze呼出し差分。"
        "カーソル移動/貼付/削除は**編集直後の全文Analyze**を測定し、編集操作"
        "そのものの遅延ではない。",
        "- 既存H10/旧Coreも同じrawで実行し、RouteCountが全件一致することを検証。"
        "これは候補や全Top-K fingerprintの検証に代わるものではない。",
        "- 1台のCI runnerでの観測値であり、自然文holdout・Windows実キー打鍵"
        "の性能保証でも、統計的な有意差の証明でもない。",
        "",
        "## 集約結果",
        "",
        table_header,
        separator,
    ]
    stats = {}
    for n in LENGTHS:
        predicate = lambda row, n=n: row["Operation"] == "full-decode" and row["Utf16Length"] == n
        old = distribution(base, MODEL, predicate)
        new = distribution(head, MODEL, predicate)
        lines.append(row_text(f"{n} UTF-16", old, new))
        stats[str(n)] = {"before": old, "after": new}
    edit = lambda row: row["Family"] == "editing"
    edit_old = distribution(base, MODEL, edit)
    edit_new = distribution(head, MODEL, edit)
    lines.append(row_text("editing (4操作)", edit_old, edit_new))
    stats["editing"] = {"before": edit_old, "after": edit_new}

    lines.extend(["", "## ファミリー別の退行確認", "", table_header, separator])
    for n in LENGTHS:
        for family in CATEGORIES:
            pred = lambda row, n=n, family=family: (
                row["Operation"] == "full-decode"
                and row["Utf16Length"] == n and row["Family"] == family)
            lines.append(row_text(f"{n}/{family}", distribution(base, MODEL, pred),
                                  distribution(head, MODEL, pred)))
    for op in ("cursor-move", "paste", "backspace", "delete-forward"):
        pred = lambda row, op=op: row["Family"] == "editing" and row["Operation"] == op
        lines.append(row_text("edit/" + op,
                              distribution(base, MODEL, pred),
                              distribution(head, MODEL, pred)))

    lines += ["", "同じ入力と同じ辞書でのオフライン比較。各数値は有限反復の観測であり、"
               "同一CPU内でも温度・GC・ホスト負荷の変動を含む。",
               "速度変化が小さい場合や分布間で方向が異なる場合は改善と断定しない。"]
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.write_text("\n".join(lines) + "\n", encoding="utf-8")
    summary = {
        "schema": "meltia-h11-d2-paired-full-v1",
        "base_commit": ba["ActualCheckoutCommit"],
        "head_commit": ha["ActualCheckoutCommit"],
        "fixture_sha256": ba["FixtureSetSha256"],
        "dictionary_sha256": ba["DictionaryFileSha256"],
        "sdk": ba["SdkVersion"],
        "runtime": ba["Runtime"],
        "cpu": ba["CpuModel"],
        "order": ["base", "D2", "D2", "base"],
        "stats": stats,
        "matched_keys": len(keys),
        "full_run_count": len(reports),
    }
    args.json.write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n",
                         encoding="utf-8")
    print("\n".join(lines))
    print(f"H11_D2_PAIRED_PASS fixtures={len(keys)} base={summary['base_commit']} "
          f"head={summary['head_commit']}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
