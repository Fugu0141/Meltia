// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Text.RegularExpressions;

namespace Meltia.Core.Boundary;

/// <summary>
/// H10b/H10c OFFLINE prototype. A separate dual-language graph, NOT a replacement
/// for MeltiaBoundaryEngine and NEVER connected to TSF, Preview or auto-commit.
/// This intentionally has a small transparent seed model, not trained IME weights.
/// </summary>
public sealed class H10BoundaryPrototype
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

    public Analysis Analyze(string raw, BoundaryEvidence? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length > MaxInputLength) throw new ArgumentOutOfRangeException(nameof(raw));
        evidence ??= new BoundaryEvidence();
        var explicitJapanese = (evidence.ExplicitJapanese ?? []).Distinct().ToArray();
        var mandatory = (evidence.ExplicitEnglish ?? [])
            .Concat(evidence.Protected ?? []).Distinct().ToList();
        foreach (Match match in Url.Matches(raw).Cast<Match>().Concat(Email.Matches(raw).Cast<Match>()))
        {
            var span = new Utf16Span(match.Index, match.Index + match.Length);
            if (!mandatory.Any(x => x == span)) mandatory.Add(span);
        }
        foreach (var span in explicitJapanese.Concat(mandatory))
            Validate(raw, span);
        foreach (var span in mandatory)
        {
            if (explicitJapanese.Any(x => x.Overlaps(span)) ||
                mandatory.Any(x => x != span && x.Overlaps(span)))
                throw new ArgumentException("Conflicting mandatory spans");
        }
        foreach (var span in explicitJapanese)
            if (explicitJapanese.Any(x => x != span && x.Overlaps(span)))
                throw new ArgumentException("Overlapping Japanese locks");

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
                var spelling = raw[i..end];
                if (!MeltiaRomaji.CanRead(spelling)) continue;
                // Both languages have their own likelihood, plus Japanese
                // phonetic admissibility. English evidence never deletes JA.
                var length = end - i;
                double likelihood = Evidence.Difference(spelling);
                double score = 0.30 * length - 0.55 -
                    likelihood * 0.46 * Math.Min(length, 12);
                Add(new Utf16Span(i, end), Language.Japanese, score, "romaji-fst");
            }
            for (int end = i + 2; end <= Math.Min(runEnd, i + MaxEnglishEdgeLength); end++)
            {
                var spelling = raw[i..end];
                var length = spelling.Length;
                double likelihood = Evidence.Difference(spelling);
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

        // Three competing language states; backpointers avoid copying full paths
        // on every edge. Pruning is per language state, not by English island count.
        var states = new List<State>[raw.Length + 1, 3];
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
        if (SplitsPair(raw, span.Start) || SplitsPair(raw, span.End))
            throw new ArgumentException("H10 span splits UTF-16 surrogate pair");
    }
    private static bool SplitsPair(string raw, int offset) =>
        offset > 0 && offset < raw.Length &&
        char.IsHighSurrogate(raw[offset - 1]) && char.IsLowSurrogate(raw[offset]);
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
    // Mean difference: English-positive, Japanese-negative.
    public double Difference(string raw)
    {
        double english = LogLikelihood(raw, _en);
        double japanese = LogLikelihood(raw, _ja);
        return Math.Clamp((english - japanese) / Math.Max(1, raw.Length), -2.0, 2.0);
    }
}
