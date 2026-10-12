// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
// Purpose-built for candidate-cap and range distribution experiments.
// Synthetic authored intent, not human-labelled naturally typed text.
namespace Meltia.BoundaryEvaluation;

internal static class CandidateBudgetCorpus
{
    private static readonly string[] English = [
        "compiler", "firmware", "cryptography", "semiconductor",
        "photonics", "neuroscience", "microprocessor", "hyperspectral",
        "virtualization", "interoperability", "repository", "synchronization"
    ];

    public static IReadOnlyList<GoldCase> Cases { get; } = Build();

    private static IReadOnlyList<GoldCase> Build()
    {
        var result = new List<GoldCase>();
        for (int i = 0; i < 12; i++)
        {
            string a = English[i], b = English[(i + 4) % English.Length];
            string c = English[(i + 7) % English.Length];

            // More than one region can compete for a 128-candidate budget.
            result.Add(GoldCorpus.Make($"B01-spread-{i:D2}",
                "B01-spatially-distributed",
                (string.Concat(Enumerable.Repeat("nihongo", 12)), false),
                (a, true), ("no", false), (b, true),
                (string.Concat(Enumerable.Repeat("nihongo", 12)), false),
                (c, true), ("desu", false)));

            var chunks = new List<(string, bool)> { ("watashiha", false) };
            for (int j = 0; j < 16; j++)
            {
                chunks.Add((English[(i + j) % English.Length], true));
                chunks.Add((j % 2 == 0 ? "no" : "de", false));
            }
            chunks.Add(("nihongodesu", false));
            result.Add(GoldCorpus.Make($"B02-dense-{i:D2}",
                "B02-dense-multi-island", [..chunks]));

            result.Add(GoldCorpus.Make($"B03-jp-{i:D2}",
                "B03-japanese-negative",
                (string.Concat(Enumerable.Repeat("nihongo", 25 + i)), false)));
        }
        if (result.Count != 36 ||
            result.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != 36 ||
            result.Any(c => c.Raw.Length <= 96 ||
                c.Raw.Length > Meltia.Core.Boundary.MeltiaBoundaryEngine.MaxInputLength))
            throw new InvalidOperationException("Candidate-budget corpus contract changed");
        return result;
    }
}
