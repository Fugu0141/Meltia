#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Deterministic synthetic corpus for the *real Windows keyboard* E2E harness.

Labels are hypotheses, not an independent natural-language holdout.
One JSONL row is exactly one word or one sentence.  No OS data is modified.
"""
from __future__ import annotations

import argparse
import hashlib
import itertools
import json
import random
from pathlib import Path

# (romaji, kana); ASCII keys can be delivered using SendInput virtual keys.
JA_HEAD = [
    ("kyouha", "きょうは"), ("ashitaha", "あしたは"),
    ("watashiha", "わたしは"), ("watashitachiha", "わたしたちは"),
    ("konshuha", "こんしゅうは"), ("raishuha", "らいしゅうは"),
    ("nihongode", "にほんごで"), ("gakkoude", "がっこうで"),
    ("ie de".replace(" ", ""), "いえで"), ("sumahode", "すまほで"),
    ("pasokonde", "ぱそこんで"), ("minnade", "みんなで"),
    ("yuugatani", "ゆうがたに"), ("asani", "あさに"),
    ("maiban", "まいばん"), ("maiasa", "まいあさ"),
    ("korekara", "これから"), ("sorekara", "それから"),
    ("tugitugi", "つぎつぎ"), ("isshoni", "いっしょに"),
]
JA_ACTION = [
    ("nihongowotamesu", "にほんごをためす"),
    ("kensakuwosuru", "けんさくをする"),
    ("benkyouwosuru", "べんきょうをする"),
    ("bunshouwokaku", "ぶんしょうをかく"),
    ("memo wokaku".replace(" ", ""), "めもをかく"),
    ("koodowokaku", "こーどをかく"),
    ("nyuuryokuwotamesu", "にゅうりょくをためす"),
    ("henkanwokakunin", "へんかんをかくにん"),
    ("mojiwonyuuryoku", "もじをにゅうりょく"),
    ("setsumeiwoyomu", "せつめいをよむ"),
    ("kekkaokakunin", "けっかをかくにん"),
    ("tegamiwokaku", "てがみをかく"),
    ("sukosizututamesu", "すこしずつためす"),
    ("tanjunnakotowosuru", "たんじゅんなことをする"),
    ("otowokiku", "おとをきく"),
    ("tokiniwatomaru", "ときにはとまる"),
]
JA_END = [
    ("", ""), ("yoteidesu", "よていです"),
    ("tumoridesu", "つもりです"), ("kamosiremasen", "かもしれません"),
    ("koto nishita".replace(" ", ""), "ことにした"),
    ("toomoimasu", "とおもいます"), ("noga suki".replace(" ", ""), "のがすき"),
    ("node ganbarimasu".replace(" ", ""), "のでがんばります"),
    ("node tanoshii".replace(" ", ""), "のでたのしい"),
    ("kotosiga dekimasu".replace(" ", ""), "ことができます"),
    ("youni narimashita".replace(" ", ""), "ようになりました"),
    ("tokigaarimasu", "ときがあります"),
    ("nomo ii".replace(" ", ""), "のもいい"),
    ("dekiru hazu".replace(" ", ""), "できるはず"),
    ("dattato omoimasu".replace(" ", ""), "だったとおもいます"),
    ("datta hazudesu".replace(" ", ""), "だったはずです"),
    ("mono desu".replace(" ", ""), "ものです"),
]
EN_WORDS = [
    "commit", "branch", "merge", "linux", "kernel", "node", "python", "react",
    "github", "docker", "compile", "release", "preview", "source", "program",
    "function", "service", "system", "example", "command", "update", "package",
    "message", "design", "window", "keyboard", "model", "memory", "network",
    "picture", "screen", "debug", "quality", "feature", "input", "language",
    "project", "server", "client", "render", "layout", "buffer", "runtime",
    "process", "output", "history", "cache", "version",
]
EN_EXAMPLES = [
    "commit", "seeyouagain", "API", "IME", "OpenAI",
    "node.js", "https://example.com", "github", "Linux", "GitHub",
]
MIXED_EXAMPLES = [
    # (sequence of (language, raw, desired *visible* string))
    [("en", "commit", "commit"), ("ja", "ha", "は")],
    [("en", "Meltia", "Meltia"), ("ja", "wotamesu", "をためす")],
    [("ja", "kyouha", "きょうは"), ("en", "google", "google"), ("ja", "dekensaku", "でけんさく")],
    [("en", "Ado", "Ado"), ("ja", "sannno", "さんの")],
    [("ja", "kyouha", "きょうは"), ("en", "GitHub", "GitHub"), ("ja", "desu", "です")],
    [("en", "node.js", "node.js"), ("ja", "wotamesu", "をためす")],
]


def build_case(category: str, pieces: list[tuple[str, str, str]]) -> dict:
    offset = 0
    spans = []
    for language, raw, text in pieces:
        if not raw:
            continue
        spans.append({
            "start": offset, "end": offset + len(raw),
            "language": language, "raw": raw, "text": text,
        })
        offset += len(raw)
    return {
        "category": category,
        "raw": "".join(part[1] for part in pieces),
        "expected": "".join(part[2] for part in pieces),
        "spans": spans,
    }


def candidates(category: str, rng: random.Random):
    if category == "ja":
        for item in [
            [("ja", "kyouha", "きょうは")],
            [("ja", "nihongowotamesu", "にほんごをためす")],
            [("ja", "konnichiwa", "こんにちは")],
        ]:
            yield build_case(category, item)
        combinations = list(itertools.product(JA_HEAD, JA_ACTION, JA_END))
        rng.shuffle(combinations)
        for head, action, end in combinations:
            yield build_case(category, [
                ("ja", head[0], head[1]), ("ja", action[0], action[1]),
                ("ja", end[0], end[1])
            ])
    elif category == "en":
        for word in EN_EXAMPLES:
            yield build_case(category, [("en", word, word)])
        # Distinct CamelCase technical identifier strings, not dictionary accuracy labels.
        combinations = list(itertools.product(EN_WORDS, EN_WORDS, ["", "Test", "Handler", "Factory"]))
        rng.shuffle(combinations)
        for left, right, tail in combinations:
            word = left.capitalize() + right.capitalize() + tail
            yield build_case(category, [("en", word, word)])
    elif category == "mixed":
        for piece in MIXED_EXAMPLES:
            yield build_case(category, piece)
        combinations = list(itertools.product(JA_HEAD, EN_WORDS, JA_ACTION))
        rng.shuffle(combinations)
        for head, english, action in combinations:
            yield build_case(category, [
                ("ja", head[0], head[1]), ("en", english, english),
                ("ja", action[0], action[1])
            ])
    else:
        raise ValueError(f"unsupported category: {category}")


def generate(count: int, seed: int):
    if count < 3 or count > 100_000:
        raise ValueError("count must be 3..100000")
    rng = random.Random(seed)
    quotas = {"ja": count // 3 + (count % 3 > 0),
              "en": count // 3 + (count % 3 > 1),
              "mixed": count // 3}
    rows = []
    seen = set()
    for category, quota in quotas.items():
        total = 0
        for row in candidates(category, rng):
            if row["raw"] in seen:
                continue
            seen.add(row["raw"])
            total += 1
            row["id"] = f"{category}-{total:05d}"
            rows.append(row)
            if total == quota:
                break
        if total < quota:
            raise ValueError(f"not enough distinct {category} samples for requested {count} rows")
    assert len(rows) == count
    return rows


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--count", type=int, default=10_000)
    parser.add_argument("--seed", type=int, default=20261025)
    parser.add_argument("--output", type=Path, default=Path("artifacts/meltia-e2e/input-10000.jsonl"))
    args = parser.parse_args()
    rows = generate(args.count, args.seed)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8", newline="\n") as file:
        for row in rows:
            file.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")
    digest = hashlib.sha256(args.output.read_bytes()).hexdigest()
    print(f"{args.output}: {len(rows)} cases (ja/en/mixed); SHA-256={digest}")


if __name__ == "__main__":
    main()
