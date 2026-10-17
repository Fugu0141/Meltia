// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Text.Json;
using Meltia.Core.Boundary;

internal static class Phase4E1ForwardDecoderTests
{
    private sealed record ToyEdge(int End, int NextState, double Score);
    private sealed record ToyNode(double Score, ToyNode? Prev);
    private sealed class ToyGraph(List<ToyEdge>[] graph) : IForwardCandidateProvider<ToyEdge>
    {
        public IReadOnlyList<ToyEdge> At(int utf16Position) => graph[utf16Position];
    }
    private sealed class ToyScorer : IForwardTransition<ToyNode, ToyEdge>
    {
        public int End(ToyEdge edge) => edge.End;
        public (ToyNode Node, int NextState) Advance(ToyNode previous, ToyEdge edge) =>
            (new ToyNode(previous.Score + edge.Score, previous), edge.NextState);
    }
    private sealed class ToyRetention : IForwardBeamRetention<ToyNode>
    {
        public int PruneAboveCount => 2;
        public void Prune(List<ToyNode> destination, int end, int state)
        {
            destination.Sort((a, b) => b.Score.CompareTo(a.Score));
            destination.RemoveRange(1, destination.Count - 1);
        }
    }

    public static void Run(Action<bool, string> check)
    {
        // Test the reusable traversal independently of H11: graph ordering,
        // terminal states, controlled beam pruning and forward-only validation.
        var toy = new List<ToyEdge>[] { [], [], [] };
        toy[0].Add(new ToyEdge(1, 0, 2));
        toy[0].Add(new ToyEdge(1, 0, 4));
        toy[0].Add(new ToyEdge(1, 0, 1));
        toy[1].Add(new ToyEdge(2, 1, 3));
        var result = ForwardLatticeDecoder.Decode(2, 2, 0,
            new ToyNode(0, null), new ToyGraph(toy),
            new ToyScorer(), new ToyRetention());
        check(result[0, 0].Count == 1 && result[0, 1].Count == 0 &&
              result[1, 0].Count == 1 && result[1, 0][0].Score == 4 &&
              result[2, 1].Count == 1 && result[2, 1][0].Score == 7 &&
              result[2, 1][0].Prev == result[1, 0][0],
              "E1 generic forward DP respects candidate order, pruning and Prev references");

        toy[0].Clear();
        toy[0].Add(new ToyEdge(0, 0, 0));
        bool rejected = false;
        try
        {
            ForwardLatticeDecoder.Decode(2, 2, 0, new ToyNode(0, null),
                new ToyGraph(toy), new ToyScorer(), new ToyRetention());
        }
        catch (ArgumentException) { rejected = true; }
        check(rejected, "E1 generic DP rejects non-forward edges");

        toy[0][0] = new ToyEdge(3, 0, 0);
        rejected = false;
        try
        {
            ForwardLatticeDecoder.Decode(2, 2, 0, new ToyNode(0, null),
                new ToyGraph(toy), new ToyScorer(), new ToyRetention());
        }
        catch (ArgumentException) { rejected = true; }
        check(rejected, "E1 generic DP rejects edges beyond the UTF-16 input");

        var h11 = new H11BoundaryPrototype();
        var root = FindRepoRoot();
        int frozenCount = 0;
        foreach (var source in new[] { "issue_intents_v2.jsonl",
                     "issue_intents_extra_20261014.jsonl" })
        {
            foreach (var line in File.ReadLines(Path.Combine(root,
                "tests", "Meltia.H10Evaluation", source)))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var parsed = JsonDocument.Parse(line);
                var raw = parsed.RootElement.GetProperty("raw").GetString()!;
                Compare(h11, raw, null);
                var legacyTrace = h11.AnalyzeWithTrace(raw);
                var sharedTrace = h11.AnalyzeWithSharedDecoderTrace(raw);
                if (!Same(legacyTrace.Result, sharedTrace.Result) ||
                    !SameTrace(legacyTrace.Trace, sharedTrace.Trace))
                    throw new Exception("E1 frozen Trace mismatch: " + raw);
                frozenCount++;
            }
        }
        check(frozenCount == 103,
            "E1 shared decoder equals legacy H11 on all 103 frozen raws, candidates, Top-K scores and full trace");

        var cases = new (string Raw, BoundaryEvidence? Evidence)[]
        {
            ("tokyo", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 5)])),
            ("tokyo", new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(0, 5)])),
            ("tokyo", new BoundaryEvidence(Protected: [new Utf16Span(0, 5)])),
            ("abc def", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 3)],
                ExplicitJapanese: [new Utf16Span(4, 7)])),
            ("hello@example.com", null),
            ("https://example.com/docs?q=1", null),
            ("😀a🌸GitHubwo", null),
            ("kyouhaAimerde", null),
            ("Meltiawotamesu", null),
            ("50ccgenntuki", null),
            ("APIはGitHubde", null),
            ("kyouhaKingGnudeSPYAIRde", null),
            ("Adoさんの", null),
            ("", null)
        };
        foreach (var (raw, evidence) in cases) Compare(h11, raw, evidence);
        check(true, "E1 shared/legacy H11 agree on protected, locked, Unicode, romanized and empty inputs");

        // Compare special-case evidence+Trace beyond the 103 label-only raw
        // inputs. Gold labels never enter either engine at inference time.
        foreach (var (raw, evidence) in cases.Take(12))
        {
            var previous = h11.AnalyzeWithTrace(raw, evidence);
            var shared = h11.AnalyzeWithSharedDecoderTrace(raw, evidence);
            if (!Same(previous.Result, shared.Result) ||
                !SameTrace(previous.Trace, shared.Trace))
                throw new Exception("E1 trace mismatch: " + raw);
        }
        check(true, "E1 trace parity: candidates, dropped order, rejected reasons and route step scores");

        var random = new Random(20261017);
        string[] chunks = ["kyo", "u", "nihongo", "github", "API", "de",
            "a", "🌸", "😀", " ", "-", "100GB", "to", "ha", "123"];
        for (int i = 0; i < 125; i++)
        {
            var raw = string.Concat(Enumerable.Range(0, random.Next(1, 11))
                .Select(_ => chunks[random.Next(chunks.Length)]));
            Compare(h11, raw, null);
        }
        check(true, "E1 125 seeded mixed and emoji raw inputs preserve all routes");

        foreach (int length in new[] { 32, 128, 256, 512, 1024 })
        {
            var raw = string.Concat(Enumerable.Repeat("nihongo", (length + 6) / 7))
                [..length];
            Compare(h11, raw, null);
        }
        check(true, "E1 shared decoder equals H11 from 32 to 1024 UTF-16 characters");

        // Test that a previous decode cannot mutate state retained by the next.
        var first = h11.AnalyzeWithSharedDecoder("kyouhaGitHubde");
        _ = h11.AnalyzeWithSharedDecoder("commit");
        check(Same(first, h11.AnalyzeWithSharedDecoder("kyouhaGitHubde")),
            "E1 shared decoder does not share mutable DP state across calls");

        foreach (var (raw, evidence) in new (string, BoundaryEvidence?)[]
        {
            ("😀a", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(1, 2)])),
            ("abcdef", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 4)],
                ExplicitJapanese: [new Utf16Span(2, 6)])),
            (new string('a', H11BoundaryPrototype.MaxInputLength + 1), null)
        })
        {
            Type? legacy = null, common = null;
            try { h11.Analyze(raw, evidence); }
            catch (Exception e) { legacy = e.GetType(); }
            try { h11.AnalyzeWithSharedDecoder(raw, evidence); }
            catch (Exception e) { common = e.GetType(); }
            if (legacy is null || common != legacy)
                throw new Exception("E1 exception class changed for rejected input");
        }
        check(true, "E1 shared decoder preserves invalid lock/UTF-16/maximum length exceptions");
    }

    private static void Compare(H11BoundaryPrototype h11, string raw, BoundaryEvidence? evidence)
    {
        var old = h11.Analyze(raw, evidence);
        var shared = h11.AnalyzeWithSharedDecoder(raw, evidence);
        if (!Same(old, shared)) throw new Exception("E1 H11 route/edge mismatch: " + raw);
    }

    private static bool Same(H11BoundaryPrototype.Analysis a,
        H11BoundaryPrototype.Analysis b)
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

    private static bool SameTrace(H11BoundaryPrototype.DiagnosticTrace a,
        H11BoundaryPrototype.DiagnosticTrace b)
    {
        if (a.TotalBeamDropped != b.TotalBeamDropped ||
            a.OmittedBeamDropEvents != b.OmittedBeamDropEvents ||
            a.BeamDrops.Count != b.BeamDrops.Count ||
            a.Rejections.Count != b.Rejections.Count ||
            a.Edges.Count != b.Edges.Count ||
            a.Routes.Count != b.Routes.Count)
            return false;
        if (!a.BeamDrops.SequenceEqual(b.BeamDrops) ||
            !a.Rejections.SequenceEqual(b.Rejections)) return false;
        for (int i = 0; i < a.Edges.Count; i++)
        {
            var x = a.Edges[i];
            var y = b.Edges[i];
            if (x.RawUtf16Span != y.RawUtf16Span || x.Language != y.Language ||
                x.Origin != y.Origin || !x.LocalScore.Equals(y.LocalScore) ||
                !x.RoundingResidual.Equals(y.RoundingResidual) ||
                !x.Contributions.SequenceEqual(y.Contributions)) return false;
        }
        for (int i = 0; i < a.Routes.Count; i++)
        {
            var x = a.Routes[i]; var y = b.Routes[i];
            if (!x.Score.Equals(y.Score) ||
                !x.EnglishSpans.SequenceEqual(y.EnglishSpans) ||
                !x.Steps.SequenceEqual(y.Steps)) return false;
        }
        return true;
    }

    private static string FindRepoRoot()
    {
        foreach (var starting in new[] { Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory })
        {
            DirectoryInfo? at = new DirectoryInfo(starting);
            while (at is not null)
            {
                if (File.Exists(Path.Combine(at.FullName, "tests",
                    "Meltia.H10Evaluation", "issue_intents_v2.jsonl")))
                    return at.FullName;
                at = at.Parent;
            }
        }
        throw new FileNotFoundException("E1 frozen corpus missing from checkout");
    }
}
