// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Text.Json;
using Meltia.Core.Boundary;

internal static class Phase4E5LegacyBackwardTests
{
    private static bool Same(BoundaryResult a, BoundaryResult b)
    {
        if (a.Raw != b.Raw || a.Truncated != b.Truncated ||
            a.AutoCommitAllowed != b.AutoCommitAllowed ||
            !a.Candidates.SequenceEqual(b.Candidates) ||
            a.Routes.Count != b.Routes.Count)
            return false;
        for (int i = 0; i < a.Routes.Count; i++)
        {
            if (!a.Routes[i].Score.Equals(b.Routes[i].Score) ||
                !a.Routes[i].EnglishSpans.SequenceEqual(b.Routes[i].EnglishSpans))
                return false;
        }
        if (a.Review is null || b.Review is null)
            return a.Review is null && b.Review is null;
        var x = a.Review;
        var y = b.Review;
        return x.StableEnglishSpans.SequenceEqual(y.StableEnglishSpans) &&
            x.ContestedEnglishSpans.SequenceEqual(y.ContestedEnglishSpans) &&
            x.UnresolvedReadableEnglish.SequenceEqual(y.UnresolvedReadableEnglish) &&
            x.TopTwoScoreGap == y.TopTwoScoreGap &&
            x.CompetitiveRouteCount == y.CompetitiveRouteCount &&
            x.NeedsConfirmation == y.NeedsConfirmation &&
            x.Reason == y.Reason;
    }

    private static void Compare(MeltiaBoundaryEngine engine, string raw,
        BoundaryEvidence? evidence = null)
    {
        var old = engine.Analyze(raw, evidence);
        var shared = engine.AnalyzeWithSharedBackward(raw, evidence);
        if (!Same(old, shared))
            throw new Exception("E5 legacy backward candidates/Top-K/review divergence: " + raw);
    }

    private static (Type?, string?) Error(Func<BoundaryResult> fn)
    {
        try { _ = fn(); return (null, null); }
        catch (Exception e) { return (e.GetType(), e.Message); }
    }

    public static void Run(Action<bool, string> check)
    {
        var modes = new[]
        {
            new MeltiaBoundaryOptions(),
            new MeltiaBoundaryOptions(ExperimentalFragmentationPenalty: true),
            new MeltiaBoundaryOptions(ExperimentalUnknownLatinCandidates: true,
                ExperimentalLongRunWindowing: true),
            new MeltiaBoundaryOptions(ExperimentalUnknownLatinCandidates: true,
                ExperimentalDiverseCandidatePruning: true),
            new MeltiaBoundaryOptions(ExperimentalStrongHostAdvisory: true,
                ExperimentalUncertaintyReview: true),
            // Experimental v0.8 itself still uses the ORIGINAL phonetic
            // decoder on both paths; E4 tests its shared comparison separately.
            new MeltiaBoundaryOptions(ExperimentalPhoneticLatticeDecoder: true)
        };
        var cases = new (string, BoundaryEvidence?)[]
        {
            ("", null), ("nihongo", null), ("kyouhaGitHubde", new BoundaryEvidence(
                EnglishWords: ["GitHub", "go"], ProperNames: ["KingGnu"])),
            ("Adoさんの", null), ("APIはGitHubde", new BoundaryEvidence(
                EnglishWords: ["api", "GitHub"])),
            ("50ccgenntuki", null), ("😀a🌸GitHubwo", null),
            ("hello@example.com", null),
            ("https://example.com/docs?q=1", null),
            ("tokyo", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 5)])),
            ("tokyo", new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(0, 5)])),
            ("tokyo", new BoundaryEvidence(Protected: [new Utf16Span(0, 5)])),
            ("tokyo", new BoundaryEvidence(MeltypeEnglish: [new Utf16Span(0, 5)])),
            ("abc def", new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(4, 7)],
                ExplicitEnglish: [new Utf16Span(0, 3)])),
            ("a I A", null), ("new yaml hotelltu", null),
            ("ok ok ok ok ok ok ", new BoundaryEvidence(ExplicitEnglish:
                Enumerable.Range(0, 6).Select(i => new Utf16Span(i * 3, i * 3 + 2)).ToArray()))
        };
        foreach (var options in modes)
        {
            var engine = new MeltiaBoundaryEngine(options);
            foreach (var (raw, evidence) in cases)
                Compare(engine, raw, evidence);
        }
        check(true, "E5 all legacy options, review, host advice, locks and Unicode preserve full routes");

        var repo = Phase4E1ForwardDecoderTests.FindRepoRoot();
        int count = 0;
        var plain = new MeltiaBoundaryEngine();
        foreach (var file in new[] { "issue_intents_v2.jsonl",
                 "issue_intents_extra_20261014.jsonl" })
        foreach (var line in File.ReadLines(Path.Combine(repo,
            "tests", "Meltia.H10Evaluation", file)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var j = JsonDocument.Parse(line);
            Compare(plain, j.RootElement.GetProperty("raw").GetString()!);
            count++;
        }
        check(count == 103, "E5 103 frozen raw inputs preserve legacy Core edges and Top-K");

        var seed = new Random(20261017);
        string[] chunks = ["ni", "hon", "go", "API", "de", "Ado", " ",
            "😀", "🌸", "100GB", "-", "kyo", "ga", "watashi"];
        var review = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
            ExperimentalUncertaintyReview: true,
            ExperimentalUnknownLatinCandidates: true));
        for (int i = 0; i < 125; i++)
        {
            var raw = string.Concat(Enumerable.Range(0, seed.Next(1, 12))
                .Select(_ => chunks[seed.Next(chunks.Length)]));
            Compare(review, raw, new BoundaryEvidence(
                EnglishWords: ["API", "GitHub", "Ado", "go"]));
        }
        check(true, "E5 125 seeded mixed cases preserve score and uncertainty report fields");

        foreach (var len in new[] { 32, 128, 256, 512, 1024 })
        {
            var raw = string.Concat(Enumerable.Repeat("nihongo", (len + 6) / 7))[..len];
            Compare(plain, raw);
        }
        check(true, "E5 default Core 32/128/256/512/1024 UTF-16 route parity");

        foreach (var (raw, evidence) in new (string, BoundaryEvidence?)[]
        {
            ("😀a", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(1, 2)])),
            ("abcdef", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 4)],
                ExplicitJapanese: [new Utf16Span(2, 6)])),
            ("abcdef", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 4),
                new Utf16Span(2, 6)])),
            (new string('a', MeltiaBoundaryEngine.MaxInputLength + 1), null)
        })
        {
            var old = Error(() => plain.Analyze(raw, evidence));
            var shared = Error(() => plain.AnalyzeWithSharedBackward(raw, evidence));
            if (old.Item1 is null || old != shared)
                throw new Exception("E5 legacy input/lock exception differs");
        }
        check(true, "E5 legacy UTF-16/max length/overlap exception class and message parity");

        var first = review.AnalyzeWithSharedBackward("kyouhaGitHubde");
        _ = review.AnalyzeWithSharedBackward("githubwo");
        check(Same(first, review.AnalyzeWithSharedBackward("kyouhaGitHubde")),
            "E5 old Core shared backward DP has no mutable state leak");
    }
}
