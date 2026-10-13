// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
// v1.0 phase-2: deliberately constructed long contiguous ASCII runs.
// The labels are authored from specified chunks, NOT naturally sampled,
// independently annotated, or a blind holdout.
namespace Meltia.BoundaryEvaluation;

internal static class LongRunCorpus
{
    private static readonly string[] Latin = (
        "neuroscience compiler firmware cryptography semiconductor " +
        "microprocessor photonics interoperability virtualization " +
        "transcription geospatial hyperspectral").Split(
        ' ', StringSplitOptions.RemoveEmptyEntries);

    public static IReadOnlyList<GoldCase> Cases { get; } = Build();

    private static IReadOnlyList<GoldCase> Build()
    {
        var cases = new List<GoldCase>();
        string LongJapanese(int repeats) =>
            string.Concat(Enumerable.Repeat("nihongo", repeats));

        for (int i = 0; i < Latin.Length; i++)
        {
            var word = Latin[i];
            var second = Latin[(i + 5) % Latin.Length];

            cases.Add(GoldCorpus.Make($"L01-middle-{i:D2}", "L01-long-middle",
                (LongJapanese(16), false), (word, true),
                (LongJapanese(5), false)));
            cases.Add(GoldCorpus.Make($"L02-first-{i:D2}", "L02-long-english-first",
                (word, true), (LongJapanese(17), false)));
            cases.Add(GoldCorpus.Make($"L03-pair-{i:D2}", "L03-long-multi-island",
                (LongJapanese(11), false), (word, true), ("no", false),
                (second, true), (LongJapanese(7), false)));
            cases.Add(GoldCorpus.Make($"L04-negative-{i:D2}",
                "L04-long-japanese-only",
                (LongJapanese(17 + i), false)));
            cases.Add(GoldCorpus.Make($"L05-unicode-{i:D2}", "L05-long-unicode",
                ("😀", false), (LongJapanese(15), false), (word, true),
                ("。", false), (LongJapanese(4), false)));
        }
        if (cases.Count != 60 ||
            cases.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != cases.Count ||
            cases.Any(x => x.Raw.Length <= 96 ||
                x.Raw.Length > Meltia.Core.Boundary.MeltiaBoundaryEngine.MaxInputLength))
            throw new InvalidOperationException("Long-run corpus must contain 60 bounded cases");
        return cases;
    }
}
