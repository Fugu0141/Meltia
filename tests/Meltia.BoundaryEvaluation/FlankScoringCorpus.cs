// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
// Synthetic counterfactuals, deliberately includes implausible English
// proper-name-like spellings to measure false negatives of flank penalties.
namespace Meltia.BoundaryEvaluation;

internal static class FlankScoringCorpus
{
    private static readonly string[] Words =
    [
        "neuroscience", "compiler", "firmware", "cryptography",
        "semiconductor", "photonics", "repository", "synchronization",
        "hyperspectral", "calibration", "microprocessor", "virtualization"
    ];

    public static IReadOnlyList<GoldCase> Cases { get; } = Build();

    private static IReadOnlyList<GoldCase> Build()
    {
        var result = new List<GoldCase>();
        for (int i = 0; i < Words.Length; i++)
        {
            var english = Words[i];
            result.Add(GoldCorpus.Make($"F01-prefix-{i:D2}",
                "F01-Japanese-prefix",
                ("nihongo", false), (english, true)));
            result.Add(GoldCorpus.Make($"F02-suffix-{i:D2}",
                "F02-Japanese-suffix",
                (english, true), ("nihongo", false)));
            result.Add(GoldCorpus.Make($"F03-both-{i:D2}",
                "F03-Japanese-both-flanks",
                ("nihongo", false), (english, true), ("nihongo", false)));

            // Adversarial *counter*-example to the hypothesis:
            // someone might genuinely intend the complete concatenated
            // spelling as a single proper name or identifier in English.
            // This is not evidence that these are ordinary English words.
            result.Add(GoldCorpus.Make($"F04-counter-{i:D2}",
                "F04-English-name-counterexample",
                ("nihongo" + english, true)));
        }
        if (result.Count != 48 ||
            result.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != 48)
            throw new InvalidOperationException("Flank-scoring corpus changed");
        return result;
    }
}
