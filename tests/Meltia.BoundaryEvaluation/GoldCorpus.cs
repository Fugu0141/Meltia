// SPDX-License-Identifier: GPL-3.0-or-later
// Synthetic intent-labeled test suite v1.1: examples are NOT human-validated natural typing.
// H9/H8 study cases are cited in docs/MELTIA_BOUNDARY_EVALUATION.md.
using Meltia.Core.Boundary;

namespace Meltia.BoundaryEvaluation;

internal sealed record GoldCase(string Id, string Group, string Raw,
    IReadOnlyList<Utf16Span> EnglishSpans);

internal static class GoldCorpus
{
    public static IReadOnlyList<GoldCase> Cases { get; } = Build();

    // Build raw and [start,end) from independent labeled chunks. This prevents
    // the off-by-one errors identified in the earlier H9 research reports.
    private static GoldCase Make(string id, string group, params (string Text, bool English)[] pieces)
    {
        if (pieces.Length == 0) throw new ArgumentException("No gold chunks");
        var spans = new List<Utf16Span>();
        var raw = "";
        foreach (var (text, english) in pieces)
        {
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("Empty gold chunk");
            int start = raw.Length;
            raw += text;
            if (english) spans.Add(new Utf16Span(start, raw.Length));
        }
        if (raw.Length > MeltiaBoundaryEngine.MaxInputLength)
            throw new ArgumentException("Gold sample exceeds core limit");
        foreach (var span in spans)
        {
            if (span.Start < 0 || span.End > raw.Length || span.Start >= span.End ||
                (span.Start > 0 && span.Start < raw.Length &&
                 char.IsHighSurrogate(raw[span.Start - 1]) && char.IsLowSurrogate(raw[span.Start])) ||
                (span.End > 0 && span.End < raw.Length &&
                 char.IsHighSurrogate(raw[span.End - 1]) && char.IsLowSurrogate(raw[span.End])))
                throw new InvalidOperationException("Invalid UTF-16 gold label: " + id);
        }
        return new GoldCase(id, group, raw, spans);
    }

    private static IReadOnlyList<GoldCase> Build()
    {
        var cases = new[] {
            Make("S01-commit-first", "S01-start", ("commit",true),("simasita",false)),
            Make("S01-github-first", "S01-start", ("github",true),("de",false),("issue",true)),
            Make("S01-network-first", "S01-start", ("network",true),("miru",false)),
            Make("S02-commit-middle", "S02-middle", ("kyouha",false),("commit",true),("simasita",false)),
            Make("S02-customer-middle", "S02-middle", ("shigotode",false),("customer",true),("ga",false)),
            Make("S02-strategy-h9", "S02-middle", ("kinouha",false),("strategy",true),("wakaru",false)),
            Make("S02-delegate", "S02-middle", ("handanwo",false),("delegate",true),("suru",false)),
            Make("S03-english-last", "S03-end", ("kyouha",false),("commit",true)),
            Make("S03-strategy-last", "S03-end", ("kinouha",false),("strategy",true)),
            Make("S03-github-last", "S03-end", ("soredeha",false),("github",true)),
            Make("S04-commit-only", "S04-english-only", ("commit",true)),
            Make("S04-strategy-only", "S04-english-only", ("strategy",true)),
            Make("S04-theory-only", "S04-english-only", ("theory",true)),
            Make("S05-h9-2-islands", "S05-multi-island", ("kinouha",false),
                ("strategy",true),("no",false),("requirement",true),("toiu",false)),
            Make("S05-artist-pair", "S05-multi-island", ("kyouha",false),
                ("KingGnu",true),("de",false),("SPYAIR",true),("de",false)),
            Make("S05-three-islands", "S05-multi-island", ("kyouha",false),
                ("commit",true),("de",false),("github",true),("no",false),
                ("SPYAIR",true),("da",false)),
            Make("S06-english-adjacent", "S06-adjacent", ("commit",true),("push",true)),
            Make("S06-english-adjacent-mixed", "S06-adjacent", ("kyouha",false),
                ("commit",true),("push",true),("ga",false)),
            Make("S07-shi-alias", "S07-alias", ("shigotode",false),
                ("customer",true),("ga",false)),
            Make("S07-si-alias", "S07-alias", ("sigotode",false),
                ("customer",true),("ga",false)),
            Make("S07-tsu-alias", "S07-alias", ("tukau",false),
                ("strategy",true),("yo",false)),
            Make("S08-ime-upper", "S08-acronym", ("IME",true)),
            Make("S08-api", "S08-acronym", ("API",true)),
            Make("S08-cpu", "S08-acronym", ("kyouha",false),("CPU",true),("de",false)),
            Make("S08-ime-lower-intent", "S08-acronym", ("ime",true)),
            Make("S09-node", "S09-structural", ("node.js",true),("wotukau",false)),
            Make("S09-url", "S09-structural", ("https://example.com",true)),
            Make("S09-domain-h9", "S09-structural", ("myunknownsite.com",true),
                ("niarimasu",false)),
            Make("S09-symbol-h9", "S09-structural", ("korekara",false),
                ("knowledge",true),(".",false),("wakaru",false)),
            Make("S09-identifier", "S09-structural", ("foo_bar",true)),
            Make("S10-upper-h9", "S10-capitalization", ("kinouha",false),
                ("STRATEGY",true),("toiu",false)),
            Make("S10-name", "S10-capitalization", ("kyouha",false),
                ("Aimer",true),("de",false)),
            Make("S10-tokyo-upper", "S10-capitalization", ("Tokyo",true)),
            Make("S11-nihongo", "S11-japanese-only", ("nihongo",false)),
            Make("S11-hennkann", "S11-japanese-only", ("hennkannnikannsiteha",false)),
            Make("S11-japanese-negative-h9", "S11-japanese-only",
                ("dokoniakerusonomamakyouno",false)),
            Make("S11-japaneseno-jp", "S11-japanese-only", ("japaneseno",false)),
            Make("S11-ashita", "S11-japanese-only", ("ashitahaNIHONGOde",false)),
            Make("S12-oov-latin", "S12-unknown-latin-intent", ("qzbx",true)),
            Make("S12-unknown-word", "S12-unknown-latin-intent", ("supercalifragilistic",true)),
            Make("S12-unknown-mixed", "S12-unknown-latin-intent",
                ("kyouha",false),("qzbx",true),("de",false)),
            Make("S13-unusual-split", "S13-phonetic-interruption",
                ("ka",false),("strategy",true),("ru",false)),
            Make("S13-unusual-double", "S13-phonetic-interruption",
                ("shi",false),("commit",true),("ta",false)),
            Make("AMB-tokyo-ja", "AMB-explicit-intent", ("tokyo",false)),
            Make("AMB-tokyo-en", "AMB-explicit-intent", ("tokyo",true)),
            Make("AMB-ime-ja", "AMB-explicit-intent", ("ime",false)),
            Make("AMB-ime-en", "AMB-explicit-intent", ("ime",true)),
        };
        if (cases.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != cases.Length)
            throw new InvalidOperationException("Duplicate corpus ID");
        return cases;
    }
}
