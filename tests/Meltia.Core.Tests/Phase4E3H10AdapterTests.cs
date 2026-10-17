// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Text.Json;
using Meltia.Core.Boundary;

internal static class Phase4E3H10AdapterTests
{
    private static bool Equal(H10BoundaryPrototype.Analysis a, H10BoundaryPrototype.Analysis b)
    {
        if (a.Raw != b.Raw || a.NeedsConfirmation != b.NeedsConfirmation ||
            a.AutoCommitAllowed != b.AutoCommitAllowed ||
            a.Edges.Count != b.Edges.Count || a.Routes.Count != b.Routes.Count)
            return false;
        for (int i = 0; i < a.Edges.Count; i++)
            if (a.Edges[i] != b.Edges[i]) return false;
        for (int i = 0; i < a.Routes.Count; i++)
            if (!a.Routes[i].Score.Equals(b.Routes[i].Score) ||
                !a.Routes[i].EnglishSpans.SequenceEqual(b.Routes[i].EnglishSpans))
                return false;
        return true;
    }

    private static void Compare(H10BoundaryPrototype h10, string raw,
        BoundaryEvidence? evidence = null)
    {
        var legacy = h10.Analyze(raw, evidence);
        var shared = h10.AnalyzeWithSharedPipeline(raw, evidence);
        if (!Equal(legacy, shared))
            throw new Exception("E3 H10 candidates/Top-K/score/uncertainty mismatch: " + raw);
    }

    private static (Type?, string?) Error(Func<object> analyze)
    {
        try { _ = analyze(); return (null, null); }
        catch (Exception ex) { return (ex.GetType(), ex.Message); }
    }

    public static void Run(Action<bool, string> check)
    {
        var h10 = new H10BoundaryPrototype();
        var root = Phase4E1ForwardDecoderTests.FindRepoRoot();
        int frozen = 0;
        foreach (var name in new[] { "issue_intents_v2.jsonl",
                     "issue_intents_extra_20261014.jsonl" })
        foreach (var line in File.ReadLines(Path.Combine(root,
                     "tests", "Meltia.H10Evaluation", name)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var json = JsonDocument.Parse(line);
            Compare(h10, json.RootElement.GetProperty("raw").GetString()!);
            frozen++;
        }
        check(frozen == 103, "E3 H10 shared and legacy match all 103 issue raws (all edges/routes/scores)");

        var cases = new (string Raw, BoundaryEvidence? Evidence)[]
        {
            ("", null),
            ("tokyo", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 5)])),
            ("tokyo", new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(0, 5)])),
            ("tokyo", new BoundaryEvidence(Protected: [new Utf16Span(0, 5)])),
            ("tokyo", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 5)],
                Protected: [new Utf16Span(0, 5)])),
            ("abc def", new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(4, 7)],
                ExplicitEnglish: [new Utf16Span(0, 3)])),
            ("https://example.com/a", null),
            ("hello@example.com", null),
            ("😀a🌸GitHubwo", null),
            ("APIはGitHubde", null),
            ("kyouhaKingGnudeSPYAIRde", null),
            ("123GBwo", null),
            ("Adoさんの", null),
            ("commitha", null),
            ("kkkkkkk", null),
            ("jjjjjjj", null),
            ("abc".PadRight(80, 'a'), null)
        };
        foreach (var (raw, evidence) in cases) Compare(h10, raw, evidence);
        check(true, "E3 H10 empty/mandatory/Unicode/URL/English and dense-beam route parity");

        var rng = new Random(20261017);
        string[] parts = ["kyo", "a", "nihongo", "Github", "API", "de",
            "😀", "🌸", " ", "100GB", "tokyo", "-", "I", "A"];
        for (int i = 0; i < 120; i++)
        {
            var raw = string.Concat(Enumerable.Range(0, rng.Next(1, 10))
                .Select(_ => parts[rng.Next(parts.Length)]));
            Compare(h10, raw);
        }
        check(true, "E3 H10 fixed-seed 120 mixed and surrogate samples match every route");

        foreach (var len in new[] { 32, 128, 256, 512, 1024 })
        {
            var raw = string.Concat(Enumerable.Repeat("nihongo", (len + 6) / 7))[..len];
            Compare(h10, raw);
        }
        check(true, "E3 H10 32-1024 UTF-16 candidate and Top-K parity");

        foreach (var (raw, evidence) in new (string, BoundaryEvidence?)[]
        {
            ("😀a", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(1, 2)])),
            ("abcdef", new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(0, 4),
                new Utf16Span(2, 6)])),
            ("abcdef", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 4)],
                ExplicitJapanese: [new Utf16Span(2, 6)])),
            ("https://example.com", new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(0, 5)])),
            (new string('a', H10BoundaryPrototype.MaxInputLength + 1), null),
            ("https://a.co/x?m=a@b.co", null),
            ("a", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 2)]))
        })
        {
            var before = Error(() => h10.Analyze(raw, evidence));
            var after = Error(() => h10.AnalyzeWithSharedPipeline(raw, evidence));
            if (before.Item1 is null || before != after)
                throw new Exception("E3 H10 exception class/message contract mismatch: " + raw);
        }
        check(true, "E3 H10 legacy exception classes AND messages preserved");

        var retained = h10.AnalyzeWithSharedPipeline("kyouhaGitHubde");
        _ = h10.AnalyzeWithSharedPipeline("commit");
        check(Equal(retained, h10.AnalyzeWithSharedPipeline("kyouhaGitHubde")),
            "E3 H10 graph and decoder isolated between analyses");
    }
}
