#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Offline invariants for the Windows real-key E2E corpus (no Windows hooks)."""
import importlib.util
import json
import unittest
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[1] / "tools" / "generate_meltia_e2e_corpus.py"
spec = importlib.util.spec_from_file_location("meltia_e2e_corpus", SOURCE)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CorpusTests(unittest.TestCase):
    def test_full_corpus_has_exact_unique_balanced_distribution(self):
        rows = module.generate(10_000, 20261025)
        self.assertEqual(len(rows), 10_000)
        self.assertEqual(len({r["raw"] for r in rows}), 10_000)
        self.assertEqual({c: sum(r["category"] == c for r in rows)
                          for c in ("ja", "en", "mixed")},
                         {"ja": 3334, "en": 3333, "mixed": 3333})

    def test_label_offsets_and_coverage(self):
        for row in module.generate(10_000, 20261025):
            self.assertTrue(1 <= len(row["raw"]) <= 512)
            self.assertTrue(all(0x20 <= ord(ch) <= 0x7e for ch in row["raw"]))
            self.assertEqual("".join(s["raw"] for s in row["spans"]), row["raw"])
            self.assertEqual("".join(s["text"] for s in row["spans"]), row["expected"])
            end = 0
            for s in row["spans"]:
                self.assertEqual(s["start"], end)
                end = s["end"]
                self.assertEqual(len(s["raw"]), end - s["start"])
                self.assertIn(s["language"], ("ja", "en"))
            self.assertEqual(end, len(row["raw"]))
            json.dumps(row, ensure_ascii=False)

    def test_identical_seed_same_rows(self):
        first = module.generate(90, 42)
        self.assertEqual(first, module.generate(90, 42))
        self.assertNotEqual(first, module.generate(90, 43))
        self.assertNotEqual(first, module.generate(90, 42)[:-1])

    def test_fixed_field_regressions_are_present(self):
        rows = module.generate(10_000, 20261025)
        cases = {r["raw"] for r in rows}
        self.assertTrue({"commit", "node.js", "commitha", "Meltiawotamesu",
                         "kyouhagoogledekensaku", "Adosannno", "API"} <= cases)


if __name__ == "__main__":
    unittest.main()
