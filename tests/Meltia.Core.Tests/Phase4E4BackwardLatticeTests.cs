// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Text.Json;
using Meltia.Core.Boundary;

internal static class Phase4E4BackwardLatticeTests
{
    private static bool Same(IReadOnlyList<BoundaryRoute> old,
        IReadOnlyList<BoundaryRoute> shared)
    {
        if (old.Count != shared.Count) return false;
        for (int i = 0; i < old.Count; i++)
            if (!old[i].Score.Equals(shared[i].Score) ||
                !old[i].EnglishSpans.SequenceEqual(shared[i].EnglishSpans))
                return false;
        return true;
    }

    private static void Compare(string raw, IReadOnlyList<BoundaryCandidate> candidates,
        IReadOnlyList<Utf16Span> mandatory, IReadOnlyList<Utf16Span> japanese,
        double cost, int maxRoutes = 8, bool contextual = false, bool flank = false)
    {
        var old = MeltiaPhoneticLatticeDecoder.Decode(raw, candidates,
            mandatory, japanese, cost, maxRoutes, contextual, flank);
        var shared = MeltiaPhoneticBackwardComparison.Decode(raw, candidates,
            mandatory, japanese, cost, maxRoutes, contextual, flank);
        if (!Same(old, shared))
            throw new Exception("E4 backward v0.8 Top-K/score/order divergence: " + raw);
    }

    private static (Type?, string?) ExceptionOf(Func<object> call)
    {
        try { _ = call(); return (null, null); }
        catch (Exception ex) { return (ex.GetType(), ex.Message); }
    }

    public static void Run(Action<bool, string> check)
    {
        var modes = new (bool Unknown, bool Contextual, bool Flank)[]
        {
            (false, false, false), // v0.8
            (true, false, false),  // v0.8 + v0.7 proposals
            (true, true, false),   // v0.9 contextual bridge
            (true, true, true)     // v1.0 flank and contextual scoring
        };
        var inputs = new[]
        {
            "", "nihongo", "kyouhaGitHubde", "APIを使う", "😀a🌸GitHubwo",
            "hello@example.com", "https://example.com/abc?q=1",
            "kyouhaKingGnudeSPYAIRde", "Adoさんの", "commitha",
            "100GBwotamesu", "new yaml hotelltu", "foo bar baz",
            "a I A", "a-b", "niy", "k", "URL"
        };
        foreach (var (unknown, contextual, flank) in modes)
        {
            var engine = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
                ExperimentalUnknownLatinCandidates: unknown,
                ExperimentalPhoneticLatticeDecoder: true,
                ExperimentalContextualIslandScoring: contextual,
                ExperimentalJapaneseFlankScoring: flank));
            foreach (var raw in inputs)
            {
                var evidence = new BoundaryEvidence(
                    EnglishWords: ["GitHub", "KingGnu", "SPYAIR", "foo", "bar"]);
                var actual = engine.Analyze(raw, evidence);
                var hard = actual.Candidates.Where(x => x.Mandatory)
                    .Select(x => x.Span).ToArray();
                var old = MeltiaPhoneticLatticeDecoder.Decode(raw,
                    actual.Candidates, hard, [], 4.5, MeltiaBoundaryEngine.MaxRoutes,
                    contextual, flank);
                if (!Same(actual.Routes, old))
                    throw new Exception("E4 test harness failed to reproduce v0.8 engine routes");
                Compare(raw, actual.Candidates, hard, [], 4.5, contextual: contextual, flank: flank);
            }
        }
        check(true, "E4 v0.8/v0.9/flank candidate, Top-K diversity and score parity with engine");

        var random = new Random(20261017);
        var mode = new MeltiaBoundaryOptions(
            ExperimentalUnknownLatinCandidates: true,
            ExperimentalPhoneticLatticeDecoder: true);
        var mixed = new MeltiaBoundaryEngine(mode);
        string[] parts = ["kyo", "u", "a", "github", "A", "ni", "de",
            "🌸", "😀", " ", "100GB", ".", "-", "wa"];
        for (int i = 0; i < 110; i++)
        {
            var raw = string.Concat(Enumerable.Range(0, random.Next(1, 14))
                .Select(_ => parts[random.Next(parts.Length)]));
            var result = mixed.Analyze(raw,
                new BoundaryEvidence(EnglishWords: ["github", "Ado"]));
            Compare(raw, result.Candidates, result.Candidates.Where(x => x.Mandatory)
                .Select(x => x.Span).ToArray(), [], 4.5);
        }
        check(true, "E4 110 seeded mixed and emoji inputs preserve every suffix-diverse route");

        var corpus = Phase4E1ForwardDecoderTests.FindRepoRoot();
        var count = 0;
        foreach (var file in new[] { "issue_intents_v2.jsonl",
                 "issue_intents_extra_20261014.jsonl" })
        foreach (var line in File.ReadLines(Path.Combine(corpus,
            "tests", "Meltia.H10Evaluation", file)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var json = JsonDocument.Parse(line);
            var raw = json.RootElement.GetProperty("raw").GetString()!;
            var result = mixed.Analyze(raw);
            Compare(raw, result.Candidates, result.Candidates.Where(x => x.Mandatory)
                .Select(x => x.Span).ToArray(), [], 4.5);
            count++;
        }
        check(count == 103, "E4 103 frozen raws preserve v0.8 full routes (not gold accuracy)");

        foreach (var len in new[] { 32, 128, 256, 512, 1024 })
        {
            var raw = string.Concat(Enumerable.Repeat("nihongo", (len + 6) / 7))[..len];
            // No candidates, isolated phonetic suffixes exercise the reverse
            // DP including the maximum permitted UTF-16 length.
            Compare(raw, [], [], [], 4.5);
        }
        check(true, "E4 backwards decoder exact raw lengths 32-1024");

        const string candidateRaw = "ab12cd45ef";
        var candidates = new BoundaryCandidate[]
        {
            new(new Utf16Span(0, 2), "a", 5),
            new(new Utf16Span(2, 4), "b", 5),
            new(new Utf16Span(4, 6), "c", 5),
            new(new Utf16Span(6, 8), "d", 5),
            new(new Utf16Span(8, 10), "e", 5),
            new(new Utf16Span(0, 6), "f", 9),
            new(new Utf16Span(4, 10), "g", 9)
        };
        foreach (var k in new[] { 1, 2, 4, 8 })
            Compare(candidateRaw, candidates, [], [], 4.5, k);
        Compare(candidateRaw, candidates, [new Utf16Span(0, 2)],
            [new Utf16Span(8, 10)], 4.5);
        check(true, "E4 English-island count diversity, Top-K limits and forced locks match");

        var duplicate = new BoundaryCandidate[]
        {
            new(new Utf16Span(0, 3), "first", 3),
            new(new Utf16Span(0, 3), "second", 4)
        };
        var legacyFailure = ExceptionOf(() => MeltiaPhoneticLatticeDecoder.Decode(
            "abc", duplicate, [], [], 4.5, 8, experimentalJapaneseFlankScoring: true));
        var sharedFailure = ExceptionOf(() => MeltiaPhoneticBackwardComparison.Decode(
            "abc", duplicate, [], [], 4.5, 8, experimentalJapaneseFlankScoring: true));
        check(legacyFailure.Item1 is not null && legacyFailure == sharedFailure,
            "E4 duplicate flank candidate retains exception type and message");
    }
}
