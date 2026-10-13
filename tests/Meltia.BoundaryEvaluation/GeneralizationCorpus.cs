// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
// Meltia v0.9 additional synthetic vocabulary-shift diagnostics.
// These lexical types intentionally do NOT occur in the previous 54-word
// R01-R07 study list. This is NOT a blind or natural typing holdout.
namespace Meltia.BoundaryEvaluation;

internal static class GeneralizationCorpus
{
    private static readonly string[] English = (
        "quaternion interoperability semiconductor hyperspectral thermodynamics " +
        "electrolysis bioinformatics geospatial algorithmic configurable " +
        "cryptography transcription virtualization microprocessor accelerometer " +
        "nanofabrication telemetry observability sustainability reconciliation " +
        "biodegradable neuroscience synchronization hydrodynamics computational " +
        "microbiology refactoring multilingual integrity discovery " +
        "accessibility autonomous metabolism resilience photonics spectrometry " +
        "semantics decomposition firmware calibration")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static readonly string[] Japanese = (
        "kanashii ureshii arigatou ohayou sasayaku yukkuri mayonaka " +
        "kimama hiragana katakana wakarimasu yomimasu tsukaimasu " +
        "nomimono komorebi yamagawa kotoshi ashita")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static readonly string[] Prefixes =
        ["koreha", "kyouha", "sokode", "watashiha", "ima", "ashitaha", "minnade"];
    private static readonly string[] Suffixes =
        ["dearu", "womiru", "toiu", "gaaru", "ninaru", "desu", "woerabu"];

    public static IReadOnlyList<GoldCase> Cases { get; } = Build();

    private static IReadOnlyList<GoldCase> Build()
    {
        var cases = new List<GoldCase>();
        for (int i = 0; i < English.Length; i++)
        {
            var word = English[i];
            var next = English[(i * 7 + 13) % English.Length];
            var third = English[(i * 11 + 21) % English.Length];
            var jpBefore = Prefixes[i % Prefixes.Length];
            var jpAfter = Suffixes[(i * 3 + 2) % Suffixes.Length];
            cases.Add(GoldCorpus.Make($"G-EN-{i:D2}-plain", "G01-english-only",
                (word, true)));
            cases.Add(GoldCorpus.Make($"G-EN-{i:D2}-middle", "G02-mixed-one",
                (jpBefore, false), (word, true), (jpAfter, false)));
            cases.Add(GoldCorpus.Make($"G-EN-{i:D2}-two", "G03-two-islands",
                (jpBefore, false), (word, true), ("no", false),
                (next, true), (jpAfter, false)));
            cases.Add(GoldCorpus.Make($"G-EN-{i:D2}-three", "G04-three-islands",
                (jpBefore, false), (word, true), ("to", false),
                (next, true), ("de", false), (third, true),
                (jpAfter, false)));
            cases.Add(GoldCorpus.Make($"G-EN-{i:D2}-title", "G05-capitalized",
                (jpBefore, false), (word[..1].ToUpperInvariant() + word[1..], true),
                (jpAfter, false)));
        }
        for (int i = 0; i < Japanese.Length; i++)
            for (int n = 0; n < 5; n++)
                cases.Add(GoldCorpus.Make($"G-JA-{i:D2}-{n}", "G06-japanese-only",
                    (Prefixes[(i + n) % Prefixes.Length], false),
                    (Japanese[i], false),
                    (new[] { "no", "to", "de", "ga", "ni" }[n], false),
                    (Japanese[(i * 5 + n + 1) % Japanese.Length], false),
                    (Suffixes[(i + n * 3) % Suffixes.Length], false)));

        if (English.Length != 40 || Japanese.Length != 18 ||
            cases.Count != 290 ||
            cases.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != cases.Count)
            throw new InvalidOperationException("Generalization corpus changed");
        return cases;
    }
}
