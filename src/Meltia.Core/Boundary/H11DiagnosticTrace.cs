// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Collections.ObjectModel;

namespace Meltia.Core.Boundary;

// Opt-in offline observer. Never serialize or persist raw text automatically.
public sealed partial class H11BoundaryPrototype
{
    public sealed record ScoreTerm(string Name, double Value);
    public sealed record DiagnosticEdge(
        Utf16Span RawUtf16Span, string Language, string Origin, double LocalScore,
        IReadOnlyList<ScoreTerm> Contributions, double RoundingResidual);
    public sealed record DiagnosticRejection(string Reason, int Count);
    public sealed record DiagnosticBeamDrop(
        int AtUtf16Position, string LastLanguage, double DroppedScore,
        double LowestKeptScore, string Reason);
    public sealed record DiagnosticStep(
        Utf16Span RawUtf16Span, string Language, string Origin,
        string PreviousLanguage, double PreviousScore, double LocalScore,
        double LanguageSwitchPenalty, double EnglishJapaneseSeamBonus,
        double CumulativeScore);
    public sealed record DiagnosticRoute(
        IReadOnlyList<Utf16Span> EnglishSpans, double Score,
        IReadOnlyList<DiagnosticStep> Steps);
    public sealed record GoldComparison(
        string Status, IReadOnlyList<Utf16Span> GoldEnglishSpans,
        IReadOnlyList<Utf16Span> Top1EnglishSpans, double Top1Score,
        double? BestGoldScore, double? Top1MinusGold,
        bool ExistsInCandidateGraph, bool SurvivedBeam,
        bool AppearsInTopK, IReadOnlyList<DiagnosticStep> BestGoldSteps);

    public sealed record DiagnosticAnalysis(Analysis Result, DiagnosticTrace Trace);

    /// <summary>
    /// Creates a trace only on explicit request; no gold label enters AnalyzeCore.
    /// Results are advisory and must never be fed back into host text mutations.
    /// </summary>
    public DiagnosticAnalysis AnalyzeWithTrace(string raw, BoundaryEvidence? evidence = null)
    {
        var collector = new TraceCollector();
        var result = AnalyzeCore(raw, evidence, collector);
        return new DiagnosticAnalysis(result, collector.Build(raw, result));
    }

    public sealed class DiagnosticTrace
    {
        private readonly string _raw;
        private readonly IReadOnlyList<Edge> _candidateEdges;
        private readonly IReadOnlyList<Node> _endNodes;

        internal DiagnosticTrace(string raw, IReadOnlyList<Edge> candidateEdges,
            IReadOnlyList<Node> endNodes,
            IReadOnlyList<DiagnosticEdge> edges,
            IReadOnlyList<DiagnosticRejection> rejected,
            IReadOnlyList<DiagnosticBeamDrop> drops,
            IReadOnlyList<DiagnosticRoute> routes,
            int totalDropped, int omittedDropEvents)
        {
            _raw = raw;
            _candidateEdges = candidateEdges;
            _endNodes = endNodes;
            Edges = edges;
            Rejections = rejected;
            BeamDrops = drops;
            Routes = routes;
            TotalBeamDropped = totalDropped;
            OmittedBeamDropEvents = omittedDropEvents;
        }

        public IReadOnlyList<DiagnosticEdge> Edges { get; }
        public IReadOnlyList<DiagnosticRejection> Rejections { get; }
        public IReadOnlyList<DiagnosticBeamDrop> BeamDrops { get; }
        public IReadOnlyList<DiagnosticRoute> Routes { get; }
        public int TotalBeamDropped { get; }
        public int OmittedBeamDropEvents { get; }
        public double? Top1MinusTop2 => Routes.Count > 1
            ? Routes[0].Score - Routes[1].Score : null;

        /// <summary>
        /// AnalyzeWithTrace has already finished; gold only constrains this
        /// independent post-hoc search over the ORIGINAL candidate graph.
        /// No synthetic gold candidates or different Analyze call are created.
        /// </summary>
        public GoldComparison CompareToGold(IReadOnlyList<Utf16Span> gold)
        {
            ArgumentNullException.ThrowIfNull(gold);
            var expected = gold.ToArray();
            int previousEnd = 0;
            foreach (var span in expected)
            {
                if (span.Start < previousEnd || span.Start < 0 ||
                    span.End > _raw.Length || span.Start >= span.End ||
                    PairSplit(_raw, span.Start) || PairSplit(_raw, span.End))
                    throw new ArgumentException("Invalid gold UTF-16 span", nameof(gold));
                previousEnd = span.End;
            }

            var byStart = new List<Edge>[_raw.Length + 1];
            for (int i = 0; i <= _raw.Length; i++) byStart[i] = [];
            foreach (var edge in _candidateEdges) byStart[edge.Span.Start].Add(edge);
            var paths = new Dictionary<(Lang, bool), GoldNode>[_raw.Length + 1];
            for (int i = 0; i <= _raw.Length; i++) paths[i] = [];
            paths[0][(Lang.Neutral, false)] =
                new GoldNode(0, Lang.Neutral, false, null, null);
            for (int i = 0; i < _raw.Length; i++)
            foreach (var state in paths[i].Values)
            foreach (var edge in byStart[i])
            {
                if (!CompatibleWithGold(edge, expected)) continue;
                double transition = TransitionScore(
                    state.Last, state.PreviousEnglishLexical, edge);
                Lang nextLast = edge.Lang == Lang.Neutral ? state.Last : edge.Lang;
                bool nextLexical = edge.Lang == Lang.En &&
                    edge.Origin == "baseline-lexicon-word";
                var next = new GoldNode(state.Score + edge.Score + transition,
                    nextLast, nextLexical, edge, state);
                var key = (nextLast, nextLexical);
                if (!paths[edge.Span.End].TryGetValue(key, out var prior) ||
                    next.Score > prior.Score)
                    paths[edge.Span.End][key] = next;
            }
            GoldNode? best = paths[_raw.Length].Values
                .OrderByDescending(x => x.Score).FirstOrDefault();
            bool feasible = best is not null;
            bool survived = _endNodes.Any(node => CompatibleNode(node, expected));
            bool inTopK = Routes.Any(r => r.EnglishSpans.SequenceEqual(expected));
            string status = !feasible ? "missing_candidate" :
                inTopK && Routes[0].EnglishSpans.SequenceEqual(expected) ? "correct" :
                inTopK ? "ranking" :
                survived ? "topk_omitted" : "path_pruned";
            return new GoldComparison(status, expected,
                Routes[0].EnglishSpans, Routes[0].Score,
                best?.Score,
                best is null ? null : Routes[0].Score - best.Score,
                feasible, survived, inTopK,
                best is null ? [] : GoldSteps(best));
        }

        private static IReadOnlyList<DiagnosticStep> GoldSteps(GoldNode final)
        {
            var nodes = new Stack<GoldNode>();
            for (var current = final; current.Edge is not null; current = current.Prev!)
                nodes.Push(current);
            var steps = new List<DiagnosticStep>();
            while (nodes.TryPop(out var node))
            {
                var prior = node.Prev!;
                var edge = node.Edge!;
                bool swap = prior.Last != Lang.Neutral &&
                    edge.Lang != Lang.Neutral && prior.Last != edge.Lang;
                double switchCost = swap ? -1.6 : 0;
                double seam = edge.Lang == Lang.Ja && swap &&
                    prior.PreviousEnglishLexical && edge.Span.Length >= 2 ? 0.9 : 0;
                steps.Add(new DiagnosticStep(edge.Span, edge.Lang.ToString(),
                    edge.Origin, prior.Last.ToString(), prior.Score, edge.Score,
                    switchCost, seam, node.Score));
            }
            return steps;
        }

        private static bool CompatibleNode(Node node, Utf16Span[] gold)
        {
            for (var at = node; at.Edge is not null; at = at.Prev!)
                if (!CompatibleWithGold(at.Edge, gold)) return false;
            return true;
        }

        private static bool CompatibleWithGold(Edge edge, Utf16Span[] gold)
        {
            bool entirelyInside = gold.Any(x =>
                x.Start <= edge.Span.Start && edge.Span.End <= x.End);
            bool overlaps = gold.Any(x => x.Overlaps(edge.Span));
            return edge.Lang == Lang.En ? entirelyInside : !overlaps;
        }
    }

    private sealed record GoldNode(double Score, Lang Last, bool PreviousEnglishLexical,
        Edge? Edge, GoldNode? Prev);

    // This is the original transition rule copied for the independent gold
    // observer, not a replacement of the production/offline DP operation.
    private static double TransitionScore(Lang previousLast, bool previousLexical,
        Edge candidate)
    {
        bool swap = previousLast != Lang.Neutral &&
            candidate.Lang != Lang.Neutral && previousLast != candidate.Lang;
        double transition = swap ? -1.6 : 0;
        if (candidate.Lang == Lang.Ja && swap && previousLexical &&
            candidate.Span.Length >= 2)
            transition += 0.9;
        return transition;
    }

    private sealed class TraceCollector
    {
        private const int MaxDropEvents = 10000;
        private readonly List<Edge> _edges = [];
        private readonly Dictionary<Edge, int> _englishEndRuns =
            new(ReferenceEqualityComparer.Instance);
        private readonly List<Node> _endNodes = [];
        private readonly List<Node> _routes = [];
        private readonly Dictionary<string, int> _rejected = new(StringComparer.Ordinal);
        private readonly List<DiagnosticBeamDrop> _drops = [];
        private int _totalDropped;

        public void Accept(Edge edge, int? englishRunEnd)
        {
            _edges.Add(edge);
            if (englishRunEnd is not null) _englishEndRuns.Add(edge, englishRunEnd.Value);
        }
        public void Reject(string reason)
        {
            _rejected.TryGetValue(reason, out int count);
            _rejected[reason] = count + 1;
        }
        public void Drop(int position, Lang last, IEnumerable<Node> removed,
            double lowestKeptScore)
        {
            foreach (var node in removed)
            {
                _totalDropped++;
                if (_drops.Count < MaxDropEvents)
                    _drops.Add(new DiagnosticBeamDrop(position, last.ToString(),
                        node.Score, lowestKeptScore,
                        node.Score == lowestKeptScore
                            ? "stable-sort-tie-at-width" : "beam-width-overflow"));
            }
        }
        public void Selected(Node route) => _routes.Add(route);
        public void Finalists(IEnumerable<Node> nodes) => _endNodes.AddRange(nodes);

        public DiagnosticTrace Build(string raw, Analysis result)
        {
            var explained = _edges.Select(edge => ExplainEdge(raw, edge,
                _englishEndRuns.TryGetValue(edge, out int runEnd) ? runEnd : null)).ToArray();
            var routes = _routes.Select(RouteSteps).ToArray();
            if (routes.Length != result.Routes.Count ||
                routes.Where((x, i) => x.Score != result.Routes[i].Score ||
                    !x.EnglishSpans.SequenceEqual(result.Routes[i].EnglishSpans)).Any())
                throw new InvalidDataException("Trace changed H11 route ordering");
            foreach (var edge in explained)
            {
                double sum = edge.Contributions.Sum(x => x.Value);
                if (Math.Abs((sum + edge.RoundingResidual) - edge.LocalScore) > 1e-10)
                    throw new InvalidDataException("H11 trace score sum mismatch");
            }
            return new DiagnosticTrace(raw, _edges.ToArray(), _endNodes.ToArray(),
                explained, _rejected.OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => new DiagnosticRejection(x.Key, x.Value)).ToArray(),
                _drops.ToArray(), routes, _totalDropped,
                _totalDropped - _drops.Count);
        }

        private static DiagnosticRoute RouteSteps(Node last)
        {
            var stack = new Stack<Node>();
            for (var node = last; node.Edge is not null; node = node.Prev!)
                stack.Push(node);
            var steps = new List<DiagnosticStep>();
            var english = new List<Utf16Span>();
            foreach (var node in stack)
            {
                var edge = node.Edge!;
                var prior = node.Prev!;
                bool swap = prior.Last != Lang.Neutral &&
                    edge.Lang != Lang.Neutral && prior.Last != edge.Lang;
                double switchCost = swap ? -1.6 : 0;
                double seam = edge.Lang == Lang.Ja && swap &&
                    prior.Edge is { Lang: Lang.En, Origin: "baseline-lexicon-word" } &&
                    edge.Span.Length >= 2 ? 0.9 : 0;
                steps.Add(new DiagnosticStep(edge.Span, edge.Lang.ToString(),
                    edge.Origin, prior.Last.ToString(), prior.Score,
                    edge.Score, switchCost, seam, node.Score));
                if (edge.Lang != Lang.En) continue;
                if (english.Count > 0 && english[^1].End == edge.Span.Start)
                    english[^1] = new Utf16Span(english[^1].Start, edge.Span.End);
                else english.Add(edge.Span);
            }
            return new DiagnosticRoute(english, last.Score, steps);
        }
    }

    private static DiagnosticEdge ExplainEdge(string raw, Edge edge, int? latinRunEnd)
    {
        var features = new List<ScoreTerm>();
        void Add(string name, double value) => features.Add(new ScoreTerm(name, value));
        var word = raw[edge.Span.Start..edge.Span.End];
        int length = word.Length;
        switch (edge.Origin)
        {
            case "mandatory":
                Add("explicit-or-protected-lock", 100);
                break;
            case "structural":
                Add("structure-base", 15);
                Add("structure-length", Math.Min(length, 16) * .3);
                break;
            case "literal":
                Add("literal-japanese-anchor", edge.Lang == Lang.Ja ? 0.85 : 0);
                Add("literal-neutral", edge.Lang == Lang.Neutral ? -0.03 : 0);
                break;
            case "unfinished-romaji":
                Add("unfinished-romaji", -2.65);
                break;
            case "robust-full-romaji":
                Add("romaji-full-length", .68 * length);
                Add("romaji-full-bias", 5.8);
                Add("romaji-fallback-cost", -2 * RomajiFallbackCost(word));
                break;
            case "phonetic-run":
                Add("phonetic-length", .39 * length);
                Add("phonetic-bias", -.62);
                Add("character-likelihood", -Model.Difference(word) *
                    .5 * Math.Min(length, 12));
                break;
            case "numeric-latin":
                Add("numeric-base", 4.5);
                Add("numeric-length", .34 * length);
                int firstLetter = 0;
                while (firstLetter < length && char.IsAsciiDigit(word[firstLetter]))
                    firstLetter++;
                bool seam = edge.Span.End < raw.Length &&
                    char.IsAsciiLetter(raw[edge.Span.End]) &&
                    Consonant(raw[edge.Span.End]) &&
                    edge.Span.End + 1 < raw.Length &&
                    "aeiou".Contains(char.ToLowerInvariant(raw[edge.Span.End + 1]));
                bool doubles = length >= firstLetter + 2 &&
                    word[^1] == word[^2];
                Add("numeric-seam-doubled", seam && doubles ? 5 : 0);
                break;
            case "baseline-lexicon-word":
            case "orthographic-word":
                ExplainEnglish(raw, edge, word, latinRunEnd, Add);
                break;
            default:
                throw new InvalidDataException("Unrecognized H11 edge origin");
        }
        // Keep floating-point association differences explicit. The stored
        // LocalScore is authoritative, never replaced by reconstructed terms.
        double residual = edge.Score - features.Sum(x => x.Value);
        return new DiagnosticEdge(edge.Span, edge.Lang.ToString(),
            edge.Origin, edge.Score, features, residual);
    }

    private static void ExplainEnglish(string raw, Edge edge, string word,
        int? latinRunEnd, Action<string, double> add)
    {
        int i = edge.Span.Start, end = edge.Span.End;
        int endRun = latinRunEnd ??
            throw new InvalidDataException("Missing original English run boundary");
        bool readable = RomajiReadable(word);
        double difference = Model.Difference(word);
        bool lexical = Lexicon.Contains(word.ToLowerInvariant());
        bool naturalWord = word.Length >= 4 &&
            (Lexicon.Contains(word[..^1].ToLowerInvariant()) && word.EndsWith('s') ||
             word.EndsWith("ing", StringComparison.OrdinalIgnoreCase) &&
                Lexicon.Contains(word[..^3].ToLowerInvariant()));
        bool compound = !lexical && word.Length >= 6 &&
            Enumerable.Range(3, word.Length - 5).Any(split =>
                Lexicon.Contains(word[..split].ToLowerInvariant()) &&
                Lexicon.Contains(word[split..].ToLowerInvariant()));
        bool derivedPrefix = !lexical && word.Length >= 6 &&
            new[] { "dis", "re", "pre", "un", "sub" }.Any(prefix =>
                word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                word.Length >= prefix.Length + 4 &&
                Lexicon.Contains(word[prefix.Length..].ToLowerInvariant()));
        double lexBonus = lexical ? 2.3 + Math.Min(word.Length, 12) * .38 :
            compound ? 5.5 : derivedPrefix ? 3.5 :
            naturalWord ? 2.2 : 0;
        double capsBonus = char.IsUpper(word[0]) ? 1.55 : 0;
        if (word.Length <= 6 && word.All(char.IsUpper)) capsBonus += 1.4;
        bool isolatedOne = word.Length == 1 &&
            (word.Equals("a", StringComparison.OrdinalIgnoreCase) || word == "I") &&
            i > 0 && char.IsWhiteSpace(raw[i - 1]) &&
            (end == raw.Length || char.IsWhiteSpace(raw[end]));
        add("orthographic-base", -1.45);
        add("character-likelihood",
            difference * Math.Min(word.Length, 14) * .70);
        add("orthographic-length", .11 * word.Length);
        add(lexical ? "dictionary-lexicon" :
            compound ? "dictionary-compound" :
            derivedPrefix ? "dictionary-prefix" :
            naturalWord ? "dictionary-inflection" : "dictionary-unmatched",
            lexBonus);
        add("capitalization", capsBonus);
        add("romaji-readability-penalty", readable ? -2.25 : 0);
        bool rightSeam = end < endRun && word.Length >= 2 &&
            (!readable || lexical) &&
            JapaneseSeams.Any(p => raw.AsSpan(end, endRun - end)
                .StartsWith(p.AsSpan(), StringComparison.OrdinalIgnoreCase));
        add("right-japanese-seam",
            rightSeam ? (!readable && Consonant(word[^1]) ? 5.7 : 2.8) : 0);
        add("internal-capitalized-island", end == endRun &&
            word.Any(char.IsUpper) && word.Any(char.IsLower) &&
            word.Skip(1).Any(char.IsUpper) ? 5.8 : 0);
        add("acronym-before-ja", word.Length >= 2 && word.All(char.IsUpper) &&
            end < endRun && char.IsLower(raw[end]) &&
            (lexical || word.Length >= 3) ? 5.5 : 0);
        add("capitalized-after-space", end == endRun &&
            i > 0 && char.IsWhiteSpace(raw[i - 1]) &&
            char.IsUpper(word[0]) && word.Length >= 3 ? 2.8 : 0);
        add("terminal-dictionary-word", end == endRun &&
            lexical && !readable ? 2.1 : 0);
        add("next-to-japanese-script", end == endRun &&
            end < raw.Length && char.IsLetter(raw[end]) &&
            !char.IsAsciiLetter(raw[end]) &&
            (lexical || char.IsUpper(word[0])) ? 3.2 : 0);
        add("isolated-one-letter-English", isolatedOne ? 2.0 : 0);
    }
}
