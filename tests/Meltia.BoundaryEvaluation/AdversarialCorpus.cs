// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
// v1.0 Quality hardening: fixed, hand-authored intent-paired stress cases.
// NOT unseen, natural text, statistically representative or blind holdout.
// For A04 pairs identical bytes with opposing intended labels deliberately
// make perfect top1 impossible without explicit user intent.
namespace Meltia.BoundaryEvaluation;
internal static class AdversarialCorpus
{
    public static IReadOnlyList<GoldCase> Cases { get; } = Build();

    private static IReadOnlyList<GoldCase> Build()
    {
        var cases = new[] {
            GoldCorpus.Make("A01-jp-01", "A01-japanese-orthographic", ("nihongode", false), ("kaerimasu", false)),
            GoldCorpus.Make("A01-jp-02", "A01-japanese-orthographic", ("kyouha", false), ("atsui", false), ("desune", false)),
            GoldCorpus.Make("A01-jp-03", "A01-japanese-orthographic", ("yoroshiku", false), ("onegaishimasu", false)),
            GoldCorpus.Make("A01-jp-04", "A01-japanese-orthographic", ("ashitaha", false), ("arigatou", false)),
            GoldCorpus.Make("A01-jp-05", "A01-japanese-orthographic", ("watashiha", false), ("gakusei", false), ("desu", false)),
            GoldCorpus.Make("A01-jp-06", "A01-japanese-orthographic", ("nihongo", false), ("wo", false), ("benkyousuru", false)),
            GoldCorpus.Make("A01-jp-07", "A01-japanese-orthographic", ("ima", false), ("watashiga", false), ("iku", false)),
            GoldCorpus.Make("A01-jp-08", "A01-japanese-orthographic", ("sakuranoki", false), ("noshita", false)),
            GoldCorpus.Make("A01-jp-09", "A01-japanese-orthographic", ("korekara", false), ("kaimono", false)),
            GoldCorpus.Make("A01-jp-10", "A01-japanese-orthographic", ("katakana", false), ("hiragana", false)),
            GoldCorpus.Make("A01-jp-11", "A01-japanese-orthographic", ("japaneseno", false)),
            GoldCorpus.Make("A01-jp-12", "A01-japanese-orthographic", ("dokoniakerusonomamakyouno", false)),
            GoldCorpus.Make("A02-eng-01", "A02-English-one", ("watashiha", false), ("photonics", true), ("wobenkyou", false)),
            GoldCorpus.Make("A02-eng-02", "A02-English-one", ("ashitaha", false), ("compiler", true), ("wotukau", false)),
            GoldCorpus.Make("A02-eng-03", "A02-English-one", ("kyouha", false), ("repository", true), ("womiru", false)),
            GoldCorpus.Make("A02-eng-04", "A02-English-one", ("ima", false), ("neuroscience", true), ("toiu", false)),
            GoldCorpus.Make("A02-eng-05", "A02-English-one", ("koreha", false), ("blockchain", true), ("desu", false)),
            GoldCorpus.Make("A02-eng-06", "A02-English-one", ("shigotoha", false), ("rendering", true), ("desu", false)),
            GoldCorpus.Make("A02-eng-07", "A02-English-one", ("korekara", false), ("photolithography", true), ("woerabu", false)),
            GoldCorpus.Make("A02-eng-08", "A02-English-one", ("hydrodynamics", true), ("desu", false)),
            GoldCorpus.Make("A02-eng-09", "A02-English-one", ("datacenter", true)),
            GoldCorpus.Make("A02-eng-10", "A02-English-one", ("kinouha", false), ("synchronization", true), ("shita", false)),
            GoldCorpus.Make("A02-eng-11", "A02-English-one", ("shigotode", false), ("compiler", true), ("ga", false)),
            GoldCorpus.Make("A02-eng-12", "A02-English-one", ("kareha", false), ("firmware", true), ("woerabu", false)),
            GoldCorpus.Make("A03-multi-01", "A03-English-multiple", ("kyouha", false), ("github", true), ("no", false), ("repository", true), ("womiru", false)),
            GoldCorpus.Make("A03-multi-02", "A03-English-multiple", ("ashitaha", false), ("compiler", true), ("to", false), ("firmware", true), ("wotukau", false)),
            GoldCorpus.Make("A03-multi-03", "A03-English-multiple", ("koreha", false), ("photonics", true), ("no", false), ("semiconductor", true), ("desu", false)),
            GoldCorpus.Make("A03-multi-04", "A03-English-multiple", ("ima", false), ("rendering", true), ("de", false), ("synchronization", true), ("suru", false)),
            GoldCorpus.Make("A03-multi-05", "A03-English-multiple", ("kinouha", false), ("compiler", true), ("to", false), ("firmware", true), ("no", false), ("pipeline", true), ("womiru", false)),
            GoldCorpus.Make("A03-multi-06", "A03-English-multiple", ("koreha", false), ("photonics", true), ("no", false), ("semiconductor", true), ("de", false), ("calibration", true), ("suru", false)),
            GoldCorpus.Make("A03-multi-07", "A03-English-multiple", ("ashitaha", false), ("neuroscience", true), ("de", false), ("microbiology", true), ("woerabu", false)),
            GoldCorpus.Make("A03-multi-08", "A03-English-multiple", ("shigotoha", false), ("virtualization", true), ("to", false), ("observability", true), ("desu", false)),
            GoldCorpus.Make("A04-amb-01-ja", "A04-intent-ambiguous", ("tokyo", false)),
            GoldCorpus.Make("A04-amb-01-en", "A04-intent-ambiguous", ("tokyo", true)),
            GoldCorpus.Make("A04-amb-02-ja", "A04-intent-ambiguous", ("ime", false)),
            GoldCorpus.Make("A04-amb-02-en", "A04-intent-ambiguous", ("ime", true)),
            GoldCorpus.Make("A04-amb-03-ja", "A04-intent-ambiguous", ("sake", false)),
            GoldCorpus.Make("A04-amb-03-en", "A04-intent-ambiguous", ("sake", true)),
            GoldCorpus.Make("A04-amb-04-ja", "A04-intent-ambiguous", ("no", false)),
            GoldCorpus.Make("A04-amb-04-en", "A04-intent-ambiguous", ("no", true)),
            GoldCorpus.Make("A04-amb-05-ja", "A04-intent-ambiguous", ("take", false)),
            GoldCorpus.Make("A04-amb-05-en", "A04-intent-ambiguous", ("take", true)),
            GoldCorpus.Make("A04-amb-06-ja", "A04-intent-ambiguous", ("kaki", false)),
            GoldCorpus.Make("A04-amb-06-en", "A04-intent-ambiguous", ("kaki", true)),
            GoldCorpus.Make("A05-struct-01", "A05-structural", ("https://example.org/docs", true)),
            GoldCorpus.Make("A05-struct-02", "A05-structural", ("hello@example.org", true)),
            GoldCorpus.Make("A05-struct-03", "A05-structural", ("kyouha", false), ("https://example.org/docs", true), ("womiru", false)),
            GoldCorpus.Make("A05-struct-04", "A05-structural", ("koreha", false), ("API", true), ("de", false)),
            GoldCorpus.Make("A05-struct-05", "A05-structural", ("nihongo", false), ("😀", false), ("photonics", true)),
            GoldCorpus.Make("A05-struct-06", "A05-structural", ("ashitaha", false), ("CSharp", true), ("wotukau", false)),
            GoldCorpus.Make("A05-struct-07", "A05-structural", ("kyouha", false), ("firmware", true), (".", false), ("womiru", false)),
            GoldCorpus.Make("A05-struct-08", "A05-structural", ("me@example.org", true), (" ", false), ("docs", true)),
            GoldCorpus.Make("A06-alias-01", "A06-romaji-variation", ("sigotode", false), ("compiler", true), ("simasu", false)),
            GoldCorpus.Make("A06-alias-02", "A06-romaji-variation", ("shigotode", false), ("compiler", true), ("shimasu", false)),
            GoldCorpus.Make("A06-alias-03", "A06-romaji-variation", ("tyotto", false), ("photonics", true), ("womiru", false)),
            GoldCorpus.Make("A06-alias-04", "A06-romaji-variation", ("chotto", false), ("photonics", true), ("womiru", false)),
        };
        if (cases.Length != 56 ||
            cases.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != cases.Length)
            throw new InvalidOperationException("Adversarial quality corpus changed");
        var paired = cases.Where(x => x.Group == "A04-intent-ambiguous")
            .GroupBy(x => x.Raw, StringComparer.Ordinal).ToArray();
        if (paired.Length != 6 || paired.Any(g => g.Count() != 2 ||
            g.Select(x => x.EnglishSpans.Count > 0).Distinct().Count() != 2))
            throw new InvalidOperationException("Ambiguous pairs must have opposite intent");
        return cases;
    }
}
