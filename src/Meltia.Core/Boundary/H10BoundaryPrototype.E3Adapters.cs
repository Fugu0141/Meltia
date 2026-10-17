// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Text.RegularExpressions;

namespace Meltia.Core.Boundary;

public sealed partial class H10BoundaryPrototype
{
    internal Analysis AnalyzeWithSharedPipeline(string raw,
        BoundaryEvidence? evidence = null) =>
        AnalyzeCore(raw, evidence, useSharedDecoder: true, useRouteProjector: true);

    private sealed record H10CandidateRequest(string Raw,
        List<Utf16Span> Mandatory, Utf16Span[] ExplicitJapanese);

    private sealed class H10CandidateGenerator :
        IForwardCandidateGenerator<H10CandidateRequest, Step, H10CandidateGraph>
    {
        public H10CandidateGraph Generate(H10CandidateRequest request) =>
            new(BuildCandidateEdges(request));
    }

    private sealed class H10CandidateGraph(List<Step>[] buckets) :
        IForwardCandidateGraph<Step>
    {
        public List<Step>[] Buckets => buckets;
        public IReadOnlyList<Step> At(int utf16Position) => buckets[utf16Position];
        public IEnumerable<Step> InStartPositionOrder() =>
            buckets.SelectMany(bucket => bucket);
    }

    private sealed class H10ForwardTransition : IForwardTransition<State, Step>
    {
        public int End(Step edge) => edge.Span.End;

        public (State Node, int NextState) Advance(State state, Step edge)
        {
            // In H10, each State.Last equals its containing language bucket.
            // Neutral edges retain it, including in initial position.
            var previous = state.Last;
            double boundaryPenalty = edge.Language == Language.Neutral ||
                previous == Language.Neutral || previous == edge.Language
                ? 0 : -2.0;
            var last = edge.Language == Language.Neutral ? previous : edge.Language;
            var candidate = new State(state.Score + edge.Score + boundaryPenalty,
                last, edge, state);
            return (candidate, (int)last);
        }
    }

    private sealed class H10ForwardBeam : IForwardBeamRetention<State>
    {
        public int PruneAboveCount => Beam * 3;

        public void Prune(List<State> dest, int end, int nextState)
        {
            // Preserve H10's unstable List.Sort tie behavior, unlike v0.8.
            dest.Sort((a, b) => b.Score.CompareTo(a.Score));
            dest.RemoveRange(Beam, dest.Count - Beam);
        }
    }

    private sealed class H10RouteProjector :
        IForwardRouteProjector<State, Step, Analysis>
    {
        public Analysis Project(string raw, List<State>[,] states,
            IForwardCandidateGraph<Step> graph)
        {
        var finalists = Enum.GetValues<Language>()
            .SelectMany(lang => states[raw.Length, (int)lang])
            .OrderByDescending(x => x.Score).Take(Beam * 3).ToArray();
        if (finalists.Length == 0)
            throw new ArgumentException("No H10 path satisfies mandatory ranges");
        var routes = new List<BoundaryRoute>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in finalists)
        {
            var edges = new List<Step>();
            for (var p = state; p.Edge is not null; p = p.Previous!)
                edges.Add(p.Edge);
            edges.Reverse();
            var english = new List<Utf16Span>();
            foreach (var edge in edges)
            {
                if (edge.Language != Language.English) continue;
                if (english.Count > 0 && english[^1].End == edge.Span.Start)
                    english[^1] = new Utf16Span(english[^1].Start, edge.Span.End);
                else english.Add(edge.Span);
            }
            var key = string.Join(";", english.Select(x => $"{x.Start}:{x.End}"));
            if (!unique.Add(key)) continue;
            routes.Add(new BoundaryRoute(english, state.Score));
            if (routes.Count == Beam) break;
        }
        var all = graph.InStartPositionOrder()
            .Select(x => new Edge(x.Span, x.Language.ToString(), x.Origin, x.Score))
            .ToArray();
        bool uncertain = routes.Count > 1 &&
            routes[0].Score - routes[1].Score < 1.5 &&
            !routes[0].EnglishSpans.SequenceEqual(routes[1].EnglishSpans);
        return new Analysis(raw, all, routes, uncertain);
        }
    }

    private static List<Step>[] BuildCandidateEdges(H10CandidateRequest request)
    {
        var raw = request.Raw;
        var mandatory = request.Mandatory;
        var explicitJapanese = request.ExplicitJapanese;
        var byStart = new List<Step>[raw.Length + 1];
        for (int i = 0; i <= raw.Length; i++) byStart[i] = [];
        void Add(Utf16Span span, Language language, double score, string origin)
        {
            if (span.Start < 0 || span.End > raw.Length || span.Length <= 0) return;
            if (mandatory.Any(x => x.Overlaps(span) && x != span)) return;
            if (explicitJapanese.Any(x => x.Overlaps(span) && language == Language.English)) return;
            if (mandatory.Any(x => x == span) && language != Language.English) return;
            byStart[span.Start].Add(new Step(span, language, score, origin));
        }

        // Structural tokens are scored as evidence, not forced, except URL/email
        // and explicit locks. This prevents a hyphen from always meaning English.
        foreach (Match match in Code.Matches(raw))
        {
            var span = new Utf16Span(match.Index, match.Index + match.Length);
            Add(span, Language.English, 4.0 + Math.Min(match.Length, 20) * 0.20,
                "structural-latin");
        }
        foreach (var span in mandatory.OrderBy(x => x.Start))
            Add(span, Language.English, 100, "mandatory-latin");

        for (int i = 0; i < raw.Length; i++)
        {
            // A UTF-16 low-surrogate cannot be a graph vertex. Even an
            // unreachable fallback edge would violate the candidate contract.
            if (i > 0 && char.IsHighSurrogate(raw[i - 1]) &&
                char.IsLowSurrogate(raw[i])) continue;
            if (mandatory.Any(x => x.Start <= i && i < x.End)) continue;
            char c = raw[i];
            if (!char.IsAsciiLetter(c))
            {
                int step = char.IsHighSurrogate(c) && i + 1 < raw.Length &&
                    char.IsLowSurrogate(raw[i + 1]) ? 2 : 1;
                var span = new Utf16Span(i, i + step);
                Add(span, char.IsLetter(c) ? Language.Japanese : Language.Neutral,
                    char.IsLetter(c) ? 0.1 : -0.10, "literal-preservation");
                continue;
            }
            // Fallback keeps typing lossless even for incomplete syllables.
            Add(new Utf16Span(i, i + 1), Language.Japanese, -2.3, "incomplete-romaji");
            int runEnd = i;
            while (runEnd < raw.Length && char.IsAsciiLetter(raw[runEnd]) &&
                !mandatory.Any(x => x.Start <= runEnd && runEnd < x.End)) runEnd++;
            for (int end = i + 1; end <= Math.Min(runEnd, i + MaxJapaneseEdgeLength); end++)
            {
                if (!MeltiaRomaji.CanRead(raw.AsSpan(i, end - i))) continue;
                var spelling = raw[i..end];
                // Both languages have their own likelihood, plus Japanese
                // phonetic admissibility. English evidence never deletes JA.
                var length = end - i;
                double likelihood = Evidence.Difference(raw.AsSpan(i, length));
                double score = 0.30 * length - 0.55 -
                    likelihood * 0.46 * Math.Min(length, 12);
                Add(new Utf16Span(i, end), Language.Japanese, score, "romaji-fst");
            }
            for (int end = i + 2; end <= Math.Min(runEnd, i + MaxEnglishEdgeLength); end++)
            {
                var spelling = raw[i..end];
                var length = spelling.Length;
                double likelihood = Evidence.Difference(raw.AsSpan(i, length));
                double caseEvidence = char.IsUpper(spelling[0]) ? 1.25 : 0.0;
                // A complete capitalized or acronym island is weak orthographic
                // evidence, not a mandatory English lock.
                if (spelling.All(char.IsUpper) && length is >= 2 and <= 6)
                    caseEvidence += 1.0;
                double score = -0.85 + likelihood * 0.68 * Math.Min(length, 14)
                    + caseEvidence;
                Add(new Utf16Span(i, end), Language.English, score,
                    "independent-english-spelling");
            }
        }

        return byStart;
    }
}
