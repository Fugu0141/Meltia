// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Text.RegularExpressions;

namespace Meltia.Core.Boundary;

/// <summary>
/// H10b/H10c OFFLINE prototype. A separate dual-language graph, NOT a replacement
/// for MeltiaBoundaryEngine and NEVER connected to TSF, Preview or auto-commit.
/// This intentionally has a small transparent seed model, not trained IME weights.
/// </summary>
public sealed partial class H10BoundaryPrototype
{
    public const int MaxInputLength = 1024;
    public const int MaxEnglishEdgeLength = 64;
    private const int MaxJapaneseEdgeLength = 12;
    private const int Beam = 8;
    private static readonly Regex Url = new(@"https?://[^\s]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Email = new(@"(?<![\w.@])[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Code = new(
        @"(?<![A-Za-z0-9_])(?:\.[A-Za-z][A-Za-z0-9_-]+|[A-Za-z][A-Za-z0-9_-]*\.[A-Za-z][A-Za-z0-9_-]+|[A-Za-z]+[0-9]+[A-Za-z0-9]*|[A-Za-z]+-[A-Za-z]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly H10CharacterEvidence Evidence = new();

    private enum Language { Japanese, English, Neutral }
    public sealed record Edge(Utf16Span Span, string Language, string Origin, double LocalScore);
    public sealed record Analysis(string Raw, IReadOnlyList<Edge> Edges,
        IReadOnlyList<BoundaryRoute> Routes, bool NeedsConfirmation)
    {
        // Experimental hypotheses must never mutate application text.
        public bool AutoCommitAllowed => false;
    }
    private sealed record Step(Utf16Span Span, Language Language, double Score, string Origin);
    private sealed record State(double Score, Language Last, Step? Edge, State? Previous);

    public Analysis Analyze(string raw, BoundaryEvidence? evidence = null) =>
        AnalyzeCore(raw, evidence);

    private Analysis AnalyzeCore(string raw, BoundaryEvidence? evidence,
        bool useSharedDecoder = false, bool useRouteProjector = false)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length > MaxInputLength) throw new ArgumentOutOfRangeException(nameof(raw));
        evidence ??= new BoundaryEvidence();
        var explicitJapanese = (evidence.ExplicitJapanese ?? []).Distinct().ToArray();
        var mandatory = (evidence.ExplicitEnglish ?? [])
            .Concat(evidence.Protected ?? []).Distinct().ToList();
        // H10 keeps distinct overlapping URL/email matches, unlike H11.
        // Later lock validation must still reject their conflicting ranges.
        ProtectedSpanDetector.AppendMatches(raw, mandatory,
            ProtectionOverlapPolicy.SkipExactDuplicate, Url, Email);
        foreach (var span in explicitJapanese.Concat(mandatory))
            Validate(raw, span);
        if (BoundarySpanValidator.HasCrossOverlap(mandatory, explicitJapanese) ||
            BoundarySpanValidator.HasDistinctOverlap(mandatory))
            throw new ArgumentException("Conflicting mandatory spans");
        if (BoundarySpanValidator.HasDistinctOverlap(explicitJapanese))
            throw new ArgumentException("Overlapping Japanese locks");

        // Phase4/E3: generator is shared but legacy decoding remains the oracle.
        var graph = new H10CandidateGenerator().Generate(
            new H10CandidateRequest(raw, mandatory, explicitJapanese));
        var byStart = graph.Buckets;

        // Three competing language states; backpointers avoid copying full paths
        // on every edge. Pruning is per language state, not by English island count.
        List<State>[,] states;
        if (useSharedDecoder)
        {
            states = ForwardLatticeDecoder.Decode(raw.Length, 3,
                (int)Language.Neutral,
                new State(0, Language.Neutral, null, null),
                graph, new H10ForwardTransition(), new H10ForwardBeam());
        }
        else
        {
        states = new List<State>[raw.Length + 1, 3];
        for (int i = 0; i <= raw.Length; i++)
            for (int lang = 0; lang < 3; lang++) states[i, lang] = [];
        states[0, (int)Language.Neutral].Add(new State(0, Language.Neutral, null, null));
        for (int i = 0; i < raw.Length; i++)
        {
            foreach (Language previous in Enum.GetValues<Language>())
            {
                foreach (var state in states[i, (int)previous].ToArray())
                foreach (var edge in byStart[i])
                {
                    double boundaryPenalty = edge.Language == Language.Neutral ||
                        previous == Language.Neutral || previous == edge.Language
                        ? 0 : -2.0;
                    // Neutral punctuation must not erase surrounding evidence.
                    var last = edge.Language == Language.Neutral ? previous : edge.Language;
                    var candidate = new State(state.Score + edge.Score + boundaryPenalty,
                        last, edge, state);
                    var dest = states[edge.Span.End, (int)last];
                    dest.Add(candidate);
                    if (dest.Count > Beam * 3)
                    {
                        dest.Sort((a, b) => b.Score.CompareTo(a.Score));
                        dest.RemoveRange(Beam, dest.Count - Beam);
                    }
                }
            }
        }
        }
        if (useRouteProjector)
            return new H10RouteProjector().Project(raw, states, graph);

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
        var all = byStart.SelectMany(x => x)
            .Select(x => new Edge(x.Span, x.Language.ToString(), x.Origin, x.Score))
            .ToArray();
        bool uncertain = routes.Count > 1 &&
            routes[0].Score - routes[1].Score < 1.5 &&
            !routes[0].EnglishSpans.SequenceEqual(routes[1].EnglishSpans);
        return new Analysis(raw, all, routes, uncertain);
    }

    private static void Validate(string raw, Utf16Span span)
    {
        if (span.Start < 0 || span.End > raw.Length || span.Start >= span.End)
            throw new ArgumentException("Invalid H10 span");
        // Preserve H10's error precedence: numeric range validation runs
        // before the shared surrogate-pair boundary check.
        if (BoundarySpanValidator.SplitsSurrogatePair(raw, span.Start) ||
            BoundarySpanValidator.SplitsSurrogatePair(raw, span.End))
            throw new ArgumentException("H10 span splits UTF-16 surrogate pair");
    }
}

/// <summary>Transparent, fixed-seed bi/tri-gram likelihood ratio; no user data.</summary>
internal sealed class H10CharacterEvidence
{
    private const string EnSeeds = """
        input output string function object client server network debug issue commit merge branch
        github linux kernel cache build deploy windows button token page browser source program
        computer science method result value database status monitor customer music video
        picture command changes code test file folder syntax editor python rust javascript
        typescript compile development language data review version system performance
        project reading writing control open close press format picture language example
        """;
    private const string JaSeeds = """
        nihongo watashi kyouha ashita kyouwa kyoumo konnichiwa ohayou sayounara
        kansha shimasu shita suru sareta yaritai tamesu kanousei
        dekiru dokoni sokoha koreha soreha naniwo nande douyatte
        sugoi kakkoii arigatou omoshiroi totemo nandaro desuka
        ima kyou kinou taberu nomimasu wakaranai daijoubu
        no ga wa ha ni wo de to kara made hitotsu futatsu
        shashin shigoto kodomo shiranai hatarakimasu
        """;
    private readonly Dictionary<string, int> _en = Build(EnSeeds);
    private readonly Dictionary<string, int> _ja = Build(JaSeeds);
    private readonly Dictionary<uint, int> _enPacked;
    private readonly Dictionary<uint, int> _jaPacked;

    public H10CharacterEvidence()
    {
        // Derived from precisely the same seed counts. Keep the string-keyed
        // tables for the non-ASCII invariant-lowercase fallback.
        _enPacked = PackModel(_en);
        _jaPacked = PackModel(_ja);
    }

    // The top byte encodes gram length so embedded NUL characters do not
    // alias a shorter key. The remaining bytes are ordinal ASCII.
    private static uint Pack(ReadOnlySpan<char> key)
    {
        uint packed = 0;
        foreach (char c in key)
            packed = (packed << 8) | (byte)c;
        return packed | ((uint)key.Length << 24);
    }

    private static Dictionary<uint, int> PackModel(Dictionary<string, int> source)
    {
        var packed = new Dictionary<uint, int>(source.Count);
        foreach (var entry in source)
            packed.Add(Pack(entry.Key.AsSpan()), entry.Value);
        return packed;
    }

    private static Dictionary<string, int> Build(string samples)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var word in samples.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string padded = "^^" + word.ToLowerInvariant() + "$";
            for (int n = 1; n <= 3; n++)
            for (int i = 0; i + n <= padded.Length; i++)
            {
                string key = padded.Substring(i, n);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }
        return counts;
    }
    private static double LogLikelihood(string word, Dictionary<string, int> model)
    {
        string normalized = "^^" + word.ToLowerInvariant() + "$";
        double sum = 0;
        for (int i = 2; i < normalized.Length; i++)
        {
            string history = normalized.Substring(i - 2, 2);
            string eventKey = history + normalized[i];
            double hits = model.GetValueOrDefault(eventKey);
            double prefix = model.GetValueOrDefault(history);
            sum += Math.Log((hits + 0.4) / (prefix + 27 * 0.4));
        }
        return sum;
    }
    // Simulate ^^ + lowercase ASCII raw + $ without allocating a padded
    // string or trigram/bigram substrings. Index i is in the virtual buffer.
    private static char VirtualChar(ReadOnlySpan<char> raw, int i)
    {
        if (i < 2) return '^';
        if (i == raw.Length + 2) return (char)36; // terminal dollar sign
        char c = raw[i - 2];
        return c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
    }

    private static double LogLikelihoodAscii(ReadOnlySpan<char> raw,
        Dictionary<uint, int> model)
    {
        double sum = 0;
        for (int i = 2; i < raw.Length + 3; i++)
        {
            char a = VirtualChar(raw, i - 2);
            char b = VirtualChar(raw, i - 1);
            char c = VirtualChar(raw, i);
            uint history = (2u << 24) | ((uint)a << 8) | b;
            uint eventKey = (3u << 24) | ((uint)a << 16) | ((uint)b << 8) | c;
            double hits = model.GetValueOrDefault(eventKey);
            double prefix = model.GetValueOrDefault(history);
            // Floating-point expression and left-to-right addition order
            // match the old string-keyed implementation exactly.
            sum += Math.Log((hits + 0.4) / (prefix + 27 * 0.4));
        }
        return sum;
    }

    // Mean difference: English-positive, Japanese-negative.
    // Keep public string signature and the legacy null exception contract.
    public double Difference(string raw)
    {
        _ = raw.Length; // null used to throw NullReferenceException
        return Difference(raw.AsSpan());
    }

    internal double Difference(ReadOnlySpan<char> raw)
    {
        for (int i = 0; i < raw.Length; ++i)
        {
            if (raw[i] <= 0x7F) continue;
            // Unicode ToLowerInvariant is not ASCII case-folding. Preserve
            // previous behavior, including surrogate/punctuation semantics.
            string source = raw.ToString();
            double enLegacy = LogLikelihood(source, _en);
            double jaLegacy = LogLikelihood(source, _ja);
            return Math.Clamp((enLegacy - jaLegacy) / Math.Max(1, raw.Length), -2.0, 2.0);
        }
        double english = LogLikelihoodAscii(raw, _enPacked);
        double japanese = LogLikelihoodAscii(raw, _jaPacked);
        return Math.Clamp((english - japanese) / Math.Max(1, raw.Length), -2.0, 2.0);
    }
}
