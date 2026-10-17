// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Text.Json;
using Meltia.Core.Boundary;

internal static class Phase4E2PipelineTests
{
    private static bool Same(H11BoundaryPrototype.Analysis a, H11BoundaryPrototype.Analysis b) =>
        Phase4E1ForwardDecoderTests.Same(a, b);

    private static void Compare(H11BoundaryPrototype h11, string raw,
        BoundaryEvidence? evidence = null, bool trace = false)
    {
        // Legacy search + projection vs E1 shared-search/legacy-projection
        // vs E2 shared-search/new-projection. All receive identical evidence.
        var legacy = h11.Analyze(raw, evidence);
        var e1 = h11.AnalyzeWithSharedDecoder(raw, evidence);
        var e2 = h11.AnalyzeWithPipeline(raw, evidence);
        if (!Same(legacy, e1) || !Same(legacy, e2))
            throw new Exception("E2 candidate/order/Top-K parity mismatch: " + raw);
        if (!trace) return;

        var original = h11.AnalyzeWithTrace(raw, evidence);
        var shared = h11.AnalyzeWithSharedDecoderTrace(raw, evidence);
        var pipeline = h11.AnalyzeWithPipelineTrace(raw, evidence);
        if (!Same(original.Result, pipeline.Result) ||
            !Same(shared.Result, pipeline.Result) ||
            !Phase4E1ForwardDecoderTests.SameTrace(original.Trace, pipeline.Trace) ||
            !Phase4E1ForwardDecoderTests.SameTrace(shared.Trace, pipeline.Trace))
            throw new Exception("E2 trace/drop/rejections/route step mismatch: " + raw);
    }

    public static void Run(Action<bool, string> check)
    {
        var h11 = new H11BoundaryPrototype();
        string root = Phase4E1ForwardDecoderTests.FindRepoRoot();
        int count = 0;
        foreach (var name in new[] { "issue_intents_v2.jsonl",
                     "issue_intents_extra_20261014.jsonl" })
        foreach (string line in File.ReadLines(Path.Combine(root,
                     "tests", "Meltia.H10Evaluation", name)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var json = JsonDocument.Parse(line);
            var raw = json.RootElement.GetProperty("raw").GetString()!;
            // Trace comparison also covers all rejected candidates, beam
            // drops including order, named score components and route steps.
            Compare(h11, raw, trace: true);
            count++;
        }
        check(count == 103,
            "E2 old/E1/new candidate and complete route/Trace parity on all 103 frozen cases");

        var cases = new (string, BoundaryEvidence?)[]
        {
            ("", null),
            ("tokyo", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 5)])),
            ("tokyo", new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(0, 5)])),
            ("tokyo", new BoundaryEvidence(Protected: [new Utf16Span(0, 5)])),
            ("tokyo", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 5)],
                Protected: [new Utf16Span(0, 5), new Utf16Span(0, 5)])),
            ("abc def", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 3)],
                ExplicitJapanese: [new Utf16Span(4, 7)])),
            ("https://example.com/x?a=hi@example.com", null),
            ("hello@example.com", null),
            ("😀a🌸GitHubwo", null),
            ("50ccgenntuki", null),
            ("Adoさんの", null),
            ("APIはGitHubde", null),
            ("kyouhaKingGnudeSPYAIRde", null),
            ("commitha", null),
            ("new yaml hotelltu", null),
            ("a I A", null)
        };
        foreach (var (raw, evidence) in cases) Compare(h11, raw, evidence, trace: true);
        check(true, "E2 protected/duplicate locks, empty, UTF-16, numeric, dictionary and ambiguous Trace parity");

        var rng = new Random(20261017);
        string[] words = ["nihongo", "kyo", "u", "github", "API",
            "de", "123", "😀", "🌸", " ", "tokyo", "A", "-"];
        for (int i = 0; i < 125; i++)
        {
            var raw = string.Concat(Enumerable.Range(0, rng.Next(1, 12))
                .Select(_ => words[rng.Next(words.Length)]));
            Compare(h11, raw);
        }
        check(true, "E2 125 seeded mixed-script raw inputs preserve ordered candidates and all Top-K routes");

        foreach (int length in new[] { 32, 128, 256, 512, 1024 })
        {
            var raw = string.Concat(Enumerable.Repeat("nihongo", (length + 6) / 7))
                [..length];
            Compare(h11, raw);
        }
        check(true, "E2 full-length 32/128/256/512/1024 UTF-16 route and candidate parity");

        var baseline = h11.AnalyzeWithPipeline("kyouhaGitHubde");
        _ = h11.AnalyzeWithPipeline("commit");
        check(Same(baseline, h11.AnalyzeWithPipeline("kyouhaGitHubde")),
            "E2 pipeline graph/DP/projector do not retain mutable state across calls");

        foreach (var (raw, evidence) in new (string, BoundaryEvidence?)[]
        {
            ("😀a", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(1, 2)])),
            ("abcdef", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 4)],
                ExplicitJapanese: [new Utf16Span(2, 6)])),
            ("abcdef", new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(0, 4),
                new Utf16Span(2, 6)])),
            (new string('a', H11BoundaryPrototype.MaxInputLength + 1), null)
        })
        {
            static Type? ExceptionType(Func<object> action)
            {
                try { _ = action(); return null; }
                catch (Exception ex) { return ex.GetType(); }
            }
            var oldType = ExceptionType(() => h11.Analyze(raw, evidence));
            var newType = ExceptionType(() => h11.AnalyzeWithPipeline(raw, evidence));
            var traceType = ExceptionType(() => h11.AnalyzeWithPipelineTrace(raw, evidence));
            if (oldType is null || newType != oldType || traceType != oldType)
                throw new Exception("E2 exception class differs from original: " + raw);
        }
        check(true, "E2 rejects invalid UTF-16 locks, contradictions and >1024 like legacy H11");
    }
}
