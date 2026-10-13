// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
// 530 synthetic out-of-template robustness probes. Not independent natural data.
// OOV source: IncrementalBoundaryLab/research/oov_character_model_experiment.py
// SNAPSHOT_OOV and SNAPSHOT_PHON (2026-10-10); these words were used in H1-H9.
namespace Meltia.BoundaryEvaluation;

internal static class RobustnessCorpus
{
    private static readonly string[] English = (
        "knowledge strategy performance schedule feedback actually probably basically " +
        "weekend meeting project deadline release update review document customer feature " +
        "requirement database security backup laptop keyboard monitor printer wireless " +
        "calendar holiday airport hospital restaurant library teacher weather morning " +
        "evening already quickly slowly carefully suddenly yesterday together important " +
        "difficult possible available necessary beautiful wonderful terrible awesome amazing")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static readonly string[] Japanese = (
        "kuru iku matsu aruku hanasu yomu kaku miseru akeru shimeru oyogu hashiru " +
        "tsukuru nomu kiku utau odoru warau naku")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    // Contexts are deliberately different from H9 additional validation's PRE/POST lists;
    // the English/Japanese vocabulary still overlaps earlier research.
    private static readonly string[] Before = [
        "arigatou", "ima", "kyoukara", "koreha", "minnade", "bokutachiha",
        "sokode", "miruto", "sukoshi", "toriasaezu"
    ];
    private static readonly string[] After = [
        "dasouda", "womiru", "gaattayo", "ninaritai", "desukara",
        "tosimasu", "nohanasi", "wosagasu", "datoomou", "deii"
    ];

    public static IReadOnlyList<GoldCase> Cases { get; } = Build();

    private static IReadOnlyList<GoldCase> Build()
    {
        var output = new List<GoldCase>();
        for (int i = 0; i < English.Length; i++)
        {
            string w = English[i], b = Before[i % Before.Length],
                a = After[(i * 7 + 3) % After.Length];
            string w2 = English[(i * 13 + 9) % English.Length];
            string w3 = English[(i * 17 + 21) % English.Length];
            output.Add(GoldCorpus.Make($"OV-{i:D2}-plain", "R01-english-only",
                (w, true)));
            output.Add(GoldCorpus.Make($"OV-{i:D2}-start", "R02-english-start",
                (w, true), (a, false)));
            output.Add(GoldCorpus.Make($"OV-{i:D2}-middle", "R03-english-middle",
                (b, false), (w, true), (a, false)));
            output.Add(GoldCorpus.Make($"OV-{i:D2}-end", "R04-english-end",
                (b, false), (w, true)));
            output.Add(GoldCorpus.Make($"OV-{i:D2}-title", "R05-capitalized",
                (b, false), (w[..1].ToUpperInvariant() + w[1..], true), (a, false)));
            output.Add(GoldCorpus.Make($"OV-{i:D2}-two", "R06-two-islands",
                (b, false), (w, true), ("no", false), (w2, true), (a, false)));
            output.Add(GoldCorpus.Make($"OV-{i:D2}-three", "R07-three-islands",
                (b, false), (w, true), ("to", false), (w2, true), ("de", false),
                (w3, true), (a, false)));
        }
        for (int i = 0; i < Japanese.Length; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                string first = Japanese[i], second = Japanese[(i * 3 + j + 1) % Japanese.Length];
                string b = Before[(i + j) % Before.Length];
                string a = After[(i * 5 + j) % After.Length];
                output.Add(GoldCorpus.Make($"JA-{i:D2}-{j:D2}",
                    "R08-japanese-only", (b, false), (first, false),
                    (new[] { "no", "to", "ga", "de" }[j % 4], false),
                    (second, false), (a, false)));
            }
        }
        if (English.Length != 54 || Japanese.Length != 19 || output.Count != 530 ||
            output.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != output.Count)
            throw new InvalidOperationException("Robustness corpus snapshot changed");
        return output;
    }
}
